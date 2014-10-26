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

type t =
| Invalid of XUri * XUri
| AutoMergeable of XUri
| UnknownMergeability of XUri
| OpenedNotLinkedToYouTrackIssue of XUri * XUri
| ReopenedNotLinkedToYouTrackIssue of string * XUri
| Merged of MindTouch.YouTrack.MergedPullRequestMetadata
| Skip of string * XUri
| Closed of XUri

let logger = LogManager.GetLogger typedefof<t>

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

let DeterminePullRequestType reopenedPullRequest youtrackValidator youtrackIssuesFilter pr =
    let pullRequestUri = pr?url.AsString()
    let prUri = new XUri(pullRequestUri)
    let branchName = pr?head?ref.AsString()
    let state = pr?state
    let commentsUri = GetCommentsUrl pr
    let notValidInYouTrack = fun() -> not << youtrackValidator << GetTicketNames <| branchName
    let repoName = pr?head?repo?name.AsString()
    logger.DebugFormat("PR: {0}, target: {1}, state: {2}", prUri.ToString(), branchName, state)

    // Clasify the kind of pull request we are getting
    if IsClosedPullRequest state then
        Closed prUri
    else if IsInvalidPullRequest pr then
        Invalid (prUri, commentsUri)
    else if IsOpenPullRequest state && reopenedPullRequest pr && notValidInYouTrack() then
        ReopenedNotLinkedToYouTrackIssue(repoName, commentsUri)
    else if IsOpenPullRequest state && notValidInYouTrack() then
        OpenedNotLinkedToYouTrackIssue (prUri, commentsUri)
    else if IsMergedPullRequest pr then Merged {
        HtmlUri = new XUri(pr?html_url.AsString())
        LinkedYouTrackIssues = branchName |> GetTicketNames |> youtrackIssuesFilter
        Author = (pr?user?login.AsString());
        Message = (pr?body.AsString());
        Release = getTargetBranchDate pr }
    else if IsUnknownMergeabilityPullRequest pr then
        UnknownMergeability prUri
    else if (IsOpenPullRequest state || reopenedPullRequest pr) && IsAutoMergeablePullRequest pr then
        AutoMergeable prUri
    else
        Skip(repoName, commentsUri)

let DeterminePullRequestTypeFromEvent reopenedPullRequest youtrackValidator youtrackIssuesFilter prEvent =
    let pr = prEvent?pull_request
    let branchName = pr?head?ref.AsString()
    let notValidInYouTrack = fun() -> not << youtrackValidator << GetTicketNames <| branchName
    let commentsUri = GetCommentsUrl pr
    if IsReopenedPullRequestEvent prEvent && notValidInYouTrack() then
        ReopenedNotLinkedToYouTrackIssue(pr?head?repo?name.AsString(), commentsUri)
    else
        DeterminePullRequestType reopenedPullRequest youtrackValidator youtrackIssuesFilter pr




