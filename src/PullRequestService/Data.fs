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
namespace MindTouch
open System

open MindTouch.Dream

open FSharp.Data.Json
open FSharp.Data.Json.Extensions

open MindTouch.YouTrack

module Data =
    type PullRequestType =
    | Invalid of XUri
    | AutoMergeable of XUri
    | UnknownMergeability of XUri
    | OpenedNotLinkedToYouTrackIssue of XUri
    | ReopenedNotLinkedToYouTrackIssue of XUri
    | Merged of XUri
    | Skip of XUri

    let DATE_PATTERN = "yyyyMMdd"

    let IsMergeable pr =
        let mergeable = pr?mergeable
        not(pr?merged.AsBoolean()) && mergeable <> JsonValue.Null && mergeable.AsBoolean() &&
        pr?mergeable_state.AsString().EqualsInvariantIgnoreCase("clean")

    let IsMergedPullRequest pr =
        let merged = pr?merged
        let action = pr?action
        action <> JsonValue.Null && action.AsString().EqualsInvariantIgnoreCase("closed") && merged <> JsonValue.Null && merged.AsBoolean()

    let IsOpenPullRequest (state : string) =
        state.EqualsInvariantIgnoreCase("open") || state.EqualsInvariantIgnoreCase("opened")

    let IsReopenedPullRequest (state : string) = 
        state.EqualsInvariantIgnoreCase("reopened")

    let IsClosedPullRequest (state : string) =
        state.EqualsInvariantIgnoreCase("closed") || state.EqualsInvariantIgnoreCase("close")

    let IsInvalidPullRequest pr =
        pr?``base``?ref.AsString().EqualsInvariantIgnoreCase("master")
       
    let targetOpenBranch targetBranchDate =
        (targetBranchDate - DateTime.Now) >= TimeSpan(6,2,0,0)
    
    let getTargetBranchDate pr =
        let targetBranch = pr?``base``?ref.AsString()
        DateTime.ParseExact(targetBranch.Substring(targetBranch.Length - DATE_PATTERN.Length), DATE_PATTERN, null)

    let IsAutoMergeablePullRequest pr =
        IsMergeable pr && targetOpenBranch(getTargetBranchDate pr)

    let IsUnknownMergeabilityPullRequest pr =
        targetOpenBranch(getTargetBranchDate pr) && pr?mergeable_state.AsString().EqualsInvariantIgnoreCase("unknown")
    
    let GetTicketNames (branchName : string) =
        branchName.Split('_') |> Seq.filter (fun s -> s.Contains("-")) |> Seq.map (fun s -> s.ToUpper())

    let DeterminePullRequestType youTrackValidator pr =
        let pullRequestUri = pr?url.AsString()
        let prUri = new XUri(pullRequestUri)
        let branchName = pr?head?ref.AsString()
        let state = pr?state.AsString()
        let commentsUri = new XUri(pr?``_links``?comments?href.AsString())
        let notValidInYouTrack = fun() -> not << youTrackValidator << GetTicketNames <| branchName

        if IsOpenPullRequest state && notValidInYouTrack() then
            OpenedNotLinkedToYouTrackIssue commentsUri
        else if IsReopenedPullRequest state && notValidInYouTrack() then
            ReopenedNotLinkedToYouTrackIssue commentsUri
        else if IsMergedPullRequest pr then
            Merged prUri
        else if not <| IsOpenPullRequest state || IsReopenedPullRequest state then
            Skip commentsUri
        else if IsInvalidPullRequest pr then
            Invalid prUri
        else if IsUnknownMergeabilityPullRequest pr then
            UnknownMergeability prUri
        else if IsAutoMergeablePullRequest pr then
            AutoMergeable prUri
        else
            Skip commentsUri

    let DeterminePullRequestTypeFromEvent youtrackValidator prEvent =
        let pr = prEvent?pull_request
        let prUri = new XUri(pr?url.AsString())
        if not <| IsOpenPullRequest(prEvent?action.AsString()) then
            Skip prUri
        else DeterminePullRequestType youtrackValidator pr
