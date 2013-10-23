namespace MindTouch
open System

open MindTouch.Dream

open FSharp.Data.Json
open FSharp.Data.Json.Extensions

module Data =
    type PullRequestType =
    | Invalid of XUri
    | AutoMergeable of XUri
    | UnknownMergeability of XUri
    | Skip

    let DATE_PATTERN = "yyyyMMdd"

    let IsMergeable pr =
        let mergeable = pr?mergeable
        not(pr?merged.AsBoolean()) && mergeable <> JsonValue.Null && mergeable.AsBoolean() &&
        pr?mergeable_state.AsString().EqualsInvariantIgnoreCase("clean")

    let OpenPullRequest (state : string) =
        state.EqualsInvariantIgnoreCase("opened") || state.EqualsInvariantIgnoreCase("reopened")

    let InvalidPullRequest pr =
        pr?``base``?ref.AsString().EqualsInvariantIgnoreCase("master")
    
    let targetOpenBranch targetBranchDate =
        (targetBranchDate - DateTime.UtcNow.Date).Days >= 6

    let getTargetBranchDate pr =
        let targetBranch = pr?``base``?ref.AsString()
        DateTime.ParseExact(targetBranch.Substring(targetBranch.Length - DATE_PATTERN.Length), DATE_PATTERN, null)

    let AutoMergeablePullRequest pr =
        IsMergeable pr && targetOpenBranch(getTargetBranchDate pr)

    let UnknownMergeabilityPullRequest pr =
        targetOpenBranch(getTargetBranchDate pr) && pr?mergeable_state.AsString().EqualsInvariantIgnoreCase("unknown")

    let DeterminePullRequestType pr =
        let pullRequestUrl = pr?url.AsString()
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