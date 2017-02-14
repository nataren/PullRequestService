﻿(*
 * MindTouch PullRequestService - a DreamService that awaits pull requests
 * notifications from Github and acts upon it
 *
 * Copyright (C) 2006-2013 MindTouch, Inc.
 * www.mindtouch.com  oss@mindtouch.com
 *
 * For community documentation and downloads visit help.mindtouch.us;
 * please review the licensing section.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *)
[<RequireQualifiedAccess>]
module MindTouch.PullRequest
open System
open System.Text
open System.Threading
open MindTouch.Extensions.Time
open MindTouch.Tasking
open Microsoft.FSharp.Collections

open MindTouch.Dream

open FSharp.Data.Json
open FSharp.Data.Json.Extensions
open log4net

type PR = MindTouch.Domain.PullRequest

let logger = LogManager.GetLogger typedefof<PR>
let RETRY_COUNT = 10

let IsMergeable pr =
    let mergeable = pr?mergeable
    not(pr?merged.AsBoolean()) && mergeable <> JsonValue.Null && mergeable.AsBoolean() &&
        pr?mergeable_state.AsString().EqualsInvariantIgnoreCase("clean")

let IsMergedPullRequestEvent evnt =
    let merged = evnt?pull_request?merged
    let action = evnt?action
    action <> JsonValue.Null && action.AsString().EqualsInvariantIgnoreCase("closed") &&
        merged <> JsonValue.Null && merged.AsBoolean()

let IsMergedPullRequest pr =
    let merged = pr?merged
    merged <> JsonValue.Null && merged.AsBoolean()

let matches (str : string) strs =
    Seq.exists (fun s -> str.EqualsInvariantIgnoreCase(s)) strs
   
let IsOpenPullRequest (state : JsonValue) =
    let isOpen = state <> JsonValue.Null && state.AsString().EqualsInvariantIgnoreCase("open")
    logger.DebugFormat("isOpen: {0}", isOpen)
    isOpen

let IsReopenedPullRequestEvent (evnt : JsonValue) =
    let action = evnt?action
    let state = evnt?pull_request?state
    action <> JsonValue.Null && action.AsString().EqualsInvariantIgnoreCase("reopened") &&
        state <> JsonValue.Null && state.AsString().EqualsInvariantIgnoreCase("open")

let IsClosedPullRequest (state : JsonValue) =
    state <> JsonValue.Null && "closed".EqualsInvariantIgnoreCase(state.AsString())

let IsTargettingSpecificPurposeBranch (branchName : string) =
    branchName <> null && branchName.ContainsInvariantIgnoreCase("_master")

let IsTargettingExplicitlyFrozenBranch (repo : string) (pr : JsonValue) isTargetingRepoFrozenBranch =
    let targetBranch = pr?``base``?ref.AsString()
    isTargetingRepoFrozenBranch repo targetBranch

let IsInvalidPullRequest pr =
    pr?``base``?ref.AsString().EqualsInvariantIgnoreCase("master")
   
let targetOpenBranch targetBranchDate =
    (targetBranchDate - DateTime.UtcNow) >= TimeSpan.FromHours(138.)

let getTargetBranchDate pr =
    let targetBranch = pr?``base``?ref.AsString() in
        MindTouch.DateUtils.getBranchDate targetBranch

let IsAutoMergeablePullRequest pr =
    IsMergeable pr && targetOpenBranch(getTargetBranchDate pr)

let IsUnknownMergeabilityPullRequest pr =
    targetOpenBranch(getTargetBranchDate pr) && pr?mergeable_state.AsString().EqualsInvariantIgnoreCase("unknown")

let GetTicketNames (branchName : string) =
    branchName.Split('_') |> Seq.filter (fun s -> s.Contains("-")) |> Seq.map (fun s -> s.ToUpper())

let GetCommentsUrl (pr : JsonValue) =
    new XUri(pr?comments_url.AsString())

let DeterminePullRequestType reopenedPullRequest youtrackValidator youtrackIssuesFilter isTargettingExplicitlyFrozenBranch pr : PR =
    let pullRequestUri = pr?url.AsString()
    let prUri = new XUri(pullRequestUri)
    let branchName = pr?head?ref.AsString()
    let state = pr?state
    let commentsUri = GetCommentsUrl pr
    let notValidInYouTrack = fun() -> not << youtrackValidator << GetTicketNames <| branchName
    let repoName = pr?head?repo?name.AsString()
    logger.DebugFormat("PR: {0}, target: {1}, state: {2}", prUri.ToString(), branchName, state)

    // Classify the kind of pull request we are getting
    if IsTargettingSpecificPurposeBranch branchName then
        PR.TargetsSpecificPurposeBranch prUri
    else if IsClosedPullRequest state && not (IsMergedPullRequest pr) then
        PR.ClosedAndNotMerged prUri
    else if IsInvalidPullRequest pr then
        PR.Invalid (prUri, commentsUri)
    else if IsOpenPullRequest state && notValidInYouTrack() then
        PR.OpenedNotLinkedToYouTrackIssue (prUri, commentsUri)
    else if IsOpenPullRequest state && IsTargettingExplicitlyFrozenBranch repoName pr isTargettingExplicitlyFrozenBranch then
        PR.TargetsExplicitlyFrozenBranch (repoName, commentsUri)
    else if IsOpenPullRequest state && reopenedPullRequest pr && notValidInYouTrack() then
        PR.ReopenedNotLinkedToYouTrackIssue(repoName, commentsUri)
    else if IsMergedPullRequest pr then PR.Merged {
        Repo = repoName
        HtmlUri = new XUri(pr?html_url.AsString())
        LinkedYouTrackIssues = branchName |> GetTicketNames |> youtrackIssuesFilter
        RepoSshUrl = (pr?``base``?repo?ssh_url.AsString());
        Author = (pr?user?login.AsString());
        Message = (pr?body.AsString());
        Release = getTargetBranchDate pr;
        Head = pr?head;
        Base = pr?``base``;
        MergeCommitSHA = pr?merge_commit_sha.AsString() 
        PrNumber = pr?number.AsInteger()}
    else if IsUnknownMergeabilityPullRequest pr then
        PR.UnknownMergeability prUri
    else if (IsOpenPullRequest state || reopenedPullRequest pr) && IsAutoMergeablePullRequest pr then
        PR.AutoMergeable prUri
    else
        PR.Skip(repoName, commentsUri)

let DeterminePullRequestTypeFromEvent reopenedPullRequest youtrackValidator youtrackIssuesFilter isTargetingRepoFrozenBranch prEvent =
    let pr = prEvent?pull_request
    let branchName = pr?head?ref.AsString()
    let notValidInYouTrack = fun() -> not << youtrackValidator << GetTicketNames <| branchName
    let commentsUri = GetCommentsUrl pr
    if IsReopenedPullRequestEvent prEvent && notValidInYouTrack() then
        PR.ReopenedNotLinkedToYouTrackIssue(pr?head?repo?name.AsString(), commentsUri)
    else
        DeterminePullRequestType reopenedPullRequest youtrackValidator youtrackIssuesFilter isTargetingRepoFrozenBranch pr

let ProcessMergedPullRequest (fromEmail : string) (toEmail : string) (email : MindTouch.Email.t) (github : MindTouch.Github.t) (youtrack : MindTouch.YouTrack.t) (prMetadata : MindTouch.Domain.MergedPullRequestMetadata) : Unit =

    // Update the YouTrack ticket
    try
        youtrack.ProcessMergedPullRequest prMetadata
    with
    | ex -> logger.ErrorFormat("Error processing youtrack part of pull request: '{0}'", ex.Message)

    // Propagate the changes forward
    let mutable loop = true
    let mutable i = 0
    while loop do
        i <- i + 1
        loop <- i < RETRY_COUNT
        try
            github.ProcessMergedPullRequest prMetadata
            loop <- false
        with
        | :? MindTouch.Github.MergeException as ex ->
            logger.ErrorFormat("HTTP error during merge operation: {0}", ex.Message)
            let release = "release_" + prMetadata.Release.ToSafeUniversalTime().ToString(MindTouch.DateUtils.DATE_PATTERN)
            let subject = prMetadata.Author + ", there was an error propagating your changes to repository " + ex.Repo + ", on " + GlobalClock.UtcNow.ToString("f") + " " + prMetadata.RepoSshUrl + " " + ex.Source_  + " " + ex.Target + " " +  prMetadata.MergeCommitSHA
            let sourceAndTargetPropagationMessage = String.Format("Could not propagate the changes made to '{0}' from '{1}' to '{2}'.", release, prMetadata.HtmlUri, ex.Target)
            let callToAction = "You must propagate your changes by  fixing the conflicts, and submitting a pull request to the conflicting branch."
            let message = String.Format("This service takes care of propagating changes across the different release branches.\n{8}\n{7}\n\nRepo='{0}'\nOriginal PR='{1}'\nAuthor='{2}'\nOriginal release branch='{3}'\nTarget branch='{4}'\nError='{5}'\nCommit='{6}'",
                                ex.Repo,
                                prMetadata.HtmlUri,
                                prMetadata.Author,
                                release,
                                ex.Target,
                                ex.InnerException.Message,
                                ex.Source_,
                                callToAction,
                                sourceAndTargetPropagationMessage)

            let htmlMessage = String.Format("This service takes care of propagating changes across the different release branches.<p>{8}</p><p>{7}</p><p>Repo='{0}'<br/>Original PR='{1}'<br/>Author='{2}'<br/>Original release branch='{3}'<br/>Target branch='{4}'<br/>Error='{5}'<br/>Commit='{6}'</p>",
                                ex.Repo,
                                prMetadata.HtmlUri,
                                prMetadata.Author,
                                release,
                                ex.Target,
                                ex.InnerException.Message,
                                ex.Source_,
                                callToAction,
                                sourceAndTargetPropagationMessage)

            let textBody = String.Format("{0}\n\n", message)
            let htmlBody = String.Format("<html><body>{0}</body></html>", htmlMessage)
            let resp = email.SendEmail(
                                fromEmail,
                                toEmail,
                                subject,
                                textBody.ToString(),
                                htmlBody.ToString(),
                                Seq.ofList [])
            let emailSent = resp.HttpStatusCode = Net.HttpStatusCode.OK
            if emailSent then
                loop <- false
            else
                logger.ErrorFormat("Could not email from '{0}' to '{1}' about merge conflict: '{2}'", fromEmail, toEmail, textBody, resp.ToString())
                AsyncUtil.Sleep((10).Seconds())
         | ex ->
            AsyncUtil.Sleep((2. ** float(i)).Seconds())
            logger.ErrorFormat("Unexpected error while processing merged operation: {0}", ex.Message)