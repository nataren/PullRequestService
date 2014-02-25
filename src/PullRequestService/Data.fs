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
    | NotBoundToYouTrackTicket of XUri
    | Skip

    let DATE_PATTERN = "yyyyMMdd"

    let IsMergeable pr =
        let mergeable = pr?mergeable
        not(pr?merged.AsBoolean()) && mergeable <> JsonValue.Null && mergeable.AsBoolean() &&
        pr?mergeable_state.AsString().EqualsInvariantIgnoreCase("clean")

    let OpenPullRequest (state : string) =
        state.EqualsInvariantIgnoreCase("open") || state.EqualsInvariantIgnoreCase("opened") || state.EqualsInvariantIgnoreCase("reopened")

    let InvalidPullRequest pr =
        pr?``base``?ref.AsString().EqualsInvariantIgnoreCase("master")
       
    let targetOpenBranch targetBranchDate =
        (targetBranchDate - DateTime.Now) >= TimeSpan(6,2,0,0)
    
    let getTargetBranchDate pr =
        let targetBranch = pr?``base``?ref.AsString()
        DateTime.ParseExact(targetBranch.Substring(targetBranch.Length - DATE_PATTERN.Length), DATE_PATTERN, null)

    let AutoMergeablePullRequest pr =
        IsMergeable pr && targetOpenBranch(getTargetBranchDate pr)

    let UnknownMergeabilityPullRequest pr =
        targetOpenBranch(getTargetBranchDate pr) && pr?mergeable_state.AsString().EqualsInvariantIgnoreCase("unknown")

    let DeterminePullRequestType pr youTrackValidator =
        let pullRequestUrl = pr?url.AsString()
        if not <| youTrackValidator pr
            NotBoundToYouTrackTicket (new XUri(pr?``_links``?comments?href.AsString()))
        if not <| OpenPullRequest(pr?state.AsString()) then
            Skip
        else if InvalidPullRequest pr then
            Invalid (new XUri(pullRequestUrl))
        else if UnknownMergeabilityPullRequest pr then
            UnknownMergeability (new XUri(pullRequestUrl))
        else if AutoMergeablePullRequest pr then
            AutoMergeable (new XUri(pullRequestUrl))
        else
            Skip

    let DeterminePullRequestTypeFromEvent prEvent =
        if not <| OpenPullRequest(prEvent?action.AsString()) then
            Skip
        else DeterminePullRequestType <| prEvent?pull_request
