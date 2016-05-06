(*
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

open MindTouch.Dream

open FSharp.Data.Json
open FSharp.Data.Json.Extensions
open log4net

type PR = MindTouch.Domain.PullRequest

let logger = LogManager.GetLogger typedefof<PR>

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
        Author = (pr?user?login.AsString());
        Message = (pr?body.AsString());
        Release = getTargetBranchDate pr;
        Head = pr?head;
        MergeCommitSHA = pr?merge_commit_sha.AsString() }
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

let ProcessMergedPullRequest (github : MindTouch.Github.t) (youtrack : MindTouch.YouTrack.t) (prMetadata : MindTouch.Domain.MergedPullRequestMetadata) : Unit =

        // Update the YouTrack ticket
        try
            youtrack.ProcessMergedPullRequest prMetadata
        with
        | ex -> Console.WriteLine(ex)

        // Propagate the changes forward
        try
            github.ProcessMergedPullRequest prMetadata
        with
        | ex -> Console.WriteLine(ex)