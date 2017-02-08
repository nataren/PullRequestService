﻿(*
 * MindTouch.Github A client to the Github API
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
module MindTouch.Github
open System
open System.Collections.Generic

open MindTouch.Dream

open FSharp.Data.Json
open FSharp.Data.Json.Extensions
open System.Globalization

open log4net

type PR = MindTouch.Domain.PullRequest

type MergeException =
    inherit Exception
    val Repo : string
    val Source_ : string
    val Target : string
    val CommitMessage : string
    new (repo : string, source : string, target : string, commitMessage : string, innerException : Exception) = {
        inherit Exception("Error merging changes", innerException)
        Repo = repo
        Source_ = source
        Target = target
        CommitMessage = commitMessage
    }

type t(owner, token) =
    let owner = owner
    let token = token
    let logger = LogManager.GetLogger typedefof<t>
    let authPair = new KeyValuePair<_, _>("Authorization", "token " + token)
    let githubApiVersion = new KeyValuePair<_,_>("Accept", "application/vnd.github.v3+json")
    let api = Plug.New(new XUri("https://api.github.com"))

    let Json(payload : string, headers : seq<KeyValuePair<string, string>>) =
         new DreamMessage(DreamStatus.Ok, new DreamHeaders(headers), MimeType.JSON, payload)

    let Auth payload = Json(payload, [| authPair; githubApiVersion |])

    let sortByReleaseDate (branch : JsonValue) =
        let branchname = branch?name.AsString()
        logger.DebugFormat("Trying to parse date {0}", branchname)
        MindTouch.DateUtils.getBranchDate <| branchname.Substring 8

    let GetArchiveLightweightTagPayload (prefix : String) (branch : JsonValue) =
        let tag = String.Format("""{{ "ref" : "refs/tags/{0}", "sha" : "{1}" }}""", String.Format("{0}{1}", prefix, branch?name.AsString()), branch?commit?sha.AsString())
        logger.DebugFormat("Builded payload for tag '{0}'", tag)
        tag
    
    member this.CommentOnPullRequest (commentUri : XUri) (comment : String) =
        logger.DebugFormat("Going to create a comment at '{0}'", commentUri)
        let msg = Json(String.Format("""{{ "body" : "{0}"}}""", comment), [| new KeyValuePair<_, _>("Authorization", "token " + token) |])
        Plug.New(commentUri).Post(msg)

    member this.ClosePullRequest (prUri : XUri) =
        logger.DebugFormat("Will try to close '{0}'", prUri)
        Plug.New(prUri)
            .Post(Json("""{ "state" : "closed"  }""", [| new KeyValuePair<_, _>("Authorization", "token " + token); new KeyValuePair<_, _>("X-HTTP-Method-Override", "PATCH") |]))

    member this.MergePullRequest (prUri : XUri) =
        logger.DebugFormat("Will try to merge '{0}'", prUri)
        let mergePlug = Plug.New(prUri.At("merge"))
        mergePlug.Put(Json("{}", [| new KeyValuePair<_, _>("Authorization", "token " + token) |]))

    member this.WebHookExist repo publicUri =
        let auth = Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token); githubApiVersion |])
        let hooks = JsonValue.Parse(api.At("repos", owner, repo, "hooks").Get(auth).ToText()).AsArray()
        let isPullRequestEvent (events : JsonValue[]) = Seq.exists (fun (event : JsonValue) -> event.AsString() = "pull_request") events
        hooks |> Seq.exists (fun (hook : JsonValue) -> isPullRequestEvent(hook?events.AsArray()) && hook?name.AsString() = "web" && hook?config?url.AsString() = publicUri)
        
    member this.CreateWebHook repo (publicUri : string) =
        let createHook = Json(String.Format("""{{ "name" : "web", "events" : ["pull_request"], "config" : {{ "url" : "{0}", "content_type" : "json" }} }}""",  publicUri), [| new KeyValuePair<_, _>("Authorization", "token " + token); githubApiVersion |])
        api.At("repos", owner, repo, "hooks").Post(createHook)

    member this.CreateWebHooks repos (publicUri : string) =
        repos
        |> Seq.filter (fun repo -> not(this.WebHookExist repo publicUri))
        |> Seq.iter (fun repo ->
            try
                this.CreateWebHook repo publicUri |> ignore
            with
            | ex -> raise(new Exception(String.Format("Repo '{0}' failed to initialize ({1})", repo, ex))))
        
    member this.GetOpenPullRequests (repo : string) =
        logger.DebugFormat("Getting the open pull requests for repo '{0}'", repo)
        let auth = Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token); githubApiVersion |])
        JsonValue.Parse(api.At("repos", owner, repo, "pulls").Get(auth).ToText()).AsArray()

    member this.ProcessPullRequests pollAction prs =
        prs |> Seq.iter (fun pr -> pollAction(new XUri(pr?url.AsString())))

    member this.ProcessRepos (repos : string[]) pollAction =
        repos
        |> Array.map (fun repo->
            try
                logger.DebugFormat("Processing repo '{0}'", repo)
                this.GetOpenPullRequests repo
            with
            | ex -> JsonValue.Parse("[]").AsArray())
        |> Array.concat
        |> Array.sortBy (fun pr -> pr?created_at.AsDateTime())
        |> this.ProcessPullRequests pollAction

    member this.PollPullRequest (prUri : XUri) action =
        let msg = String.Format("Will queue '{0}' for status polling", prUri)
        logger.DebugFormat(msg)
        action prUri
        DreamMessage.Ok(MimeType.JSON, JsonValue.String(msg).ToString())

    member this.ProcessPullRequestType pollAction (mergedPrHandler : MindTouch.Domain.MergedPullRequestMetadata -> Unit) prEventType =
        match prEventType with
        | PR.Invalid (prUri, commentsUri) -> this.CommentOnPullRequest commentsUri "This pull request is invalid because its target is the master branch, it will be closed" |> ignore; this.ClosePullRequest prUri |> ignore; DreamMessage.Ok()
        | PR.AutoMergeable uri -> this.MergePullRequest uri
        | PR.UnknownMergeability uri -> this.PollPullRequest uri pollAction
        | PR.OpenedNotLinkedToYouTrackIssue (prUri, commentsUri) -> this.CommentOnPullRequest commentsUri "This just opened pull request is not bound to a YouTrack issue, it will be closed" |> ignore; this.ClosePullRequest prUri
        | PR.ReopenedNotLinkedToYouTrackIssue (repoName, uri) -> this.CommentOnPullRequest uri "This just *reopened* pull request is not bound to a YouTrack issue, it will be ignored but contact your Technical Lead so it gets handled" |> ignore; DreamMessage.Ok()
        | PR.Merged mergedPrMetadata -> mergedPrHandler mergedPrMetadata; DreamMessage.Ok()
        | PR.Skip (repoName, uri) -> this.CommentOnPullRequest uri "This pull request is going to be ignored, contact your Technical Lead so it gets handled" |> ignore; DreamMessage.Ok(MimeType.JSON, JsonValue.String("Pull request needs to be handled by a human since is not targeting an open branch").ToString())
        | PR.ClosedAndNotMerged uri -> DreamMessage.Ok(MimeType.JSON, String.Format("Pull Request '{0}' was closed and not merged", uri.ToString()))
        | PR.TargetsExplicitlyFrozenBranch (repoName, uri) -> this.CommentOnPullRequest uri "This pull request targets an explicitly frozen branch, contact your Technical Lead so it gets handled" |> ignore; DreamMessage.Ok(MimeType.JSON, JsonValue.String("Pull request needs to be handled by a human since it is targetting an explicitly frozen branch").ToString())
        | PR.TargetsSpecificPurposeBranch uri -> DreamMessage.Ok(MimeType.JSON, "PullRequest targets specific purpose branch, will ignore")

    member this.GetPullRequestDetails (prUri : XUri) =
        logger.DebugFormat("Will fetch the details of pull request '{0}'", prUri.ToString())
        Plug.New(prUri).Get(Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token) |]))
  
    member this.GetIssueDetails (issueUri : XUri) =
        logger.DebugFormat("Will fetch the details of the issue '{0}'", issueUri.ToString())
        JsonValue.Parse(Plug.New(issueUri).Get(Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token) |])).ToText())

    member this.GetIssueEvents (issue : JsonValue) =
        let issueEventsUri = issue?events_url.AsString()
        logger.DebugFormat("Will fetch the issue's events of '{0}'", issueEventsUri)
        JsonValue.Parse(Plug.New(issueEventsUri).Get(Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token) |])).ToText()).AsArray()

    member this.IsReopenedPullRequest (pr : JsonValue) =
        let issueUri = new XUri(pr?issue_url.AsString())
        let issueDetails = this.GetIssueDetails(issueUri)
        let events = this.GetIssueEvents(issueDetails)
        if events.None() then
            false
        else
            let lastEvent = events |> Seq.last
            logger.DebugFormat("The last event was '{0}'", lastEvent.ToString())
            let isReopened = "reopened".EqualsInvariantIgnoreCase (lastEvent?event.AsString())
            logger.DebugFormat("Is '{0}' reopened? {1}", issueUri.ToString(), isReopened)
            isReopened

    member this.GetRepoBranchesInfo repo =
        JsonValue.Parse(api.At("repos", owner, repo, "branches").Get(Auth "").ToText()).AsArray()
    
    member this.GetBranch repo branch =
        JsonValue.Parse(api.At("repos", owner, repo, "branches", branch).Get(Auth "").ToText())

    member this.GetBranches repo =
        (this.GetRepoBranchesInfo repo)
        |> Seq.map (fun branch -> branch?name.AsString())
        |> Seq.filter (fun branch -> branch.StartsWith("release_"))
        |> Seq.map (fun branch -> this.GetBranch repo branch)
        |> Seq.toArray
    
    member this.CreateLightweightTag (owner : String) (repo : String) (prefix : String) branch =
        try
            let refCreationPlug = api.At("repos", owner, repo, "git", "refs")
            let payload = GetArchiveLightweightTagPayload prefix branch
            logger.DebugFormat("Will hit '{0}' with '{1}'", refCreationPlug.ToString(), payload)
            Some <| refCreationPlug.Post(Auth payload)
        with
            | ex -> logger.DebugExceptionMethodCall(ex, "Failed trying to create a lightweight tag for prefix = '{0}', branch = '{1}', '{2}'", prefix, branch?name.AsString(), ex.ToString()); None

    member this.DeleteBranch repo refLevel (branch : JsonValue) : DreamMessage option =
        let branchname = branch?name.AsString()
        logger.DebugFormat("Will attempt to delete branch '{0}/{1}'", refLevel, branchname)
        try
            Some <| api.At("repos", owner, repo, "git", "refs", refLevel, branchname).Delete(Auth "")
        with
        | ex -> logger.DebugExceptionMethodCall(ex, "Failed trying to delete ref '{0}' on repo '{1}'", branchname, repo); None

    member this.ArchiveBranches repo numberOfBranchesToKeep : DreamMessage option [] option =
        logger.DebugFormat("Will attempt to archive release branches for repo '{0}', number of branches to keep '{1}'", repo, numberOfBranchesToKeep)
        let allBranches = this.GetBranches repo
        let sortedBranches =
            allBranches
            |> Seq.choose (fun s ->
                               let branchname = s?name.AsString() in
                               logger.DebugFormat("branchname {0}", branchname)
                               let (valid, result) = DateTime.TryParseExact(branchname.Substring 8, MindTouch.DateUtils.DATE_PATTERN, null, DateTimeStyles.None) in
                                   if valid then Some s else None)
            |> Seq.sortBy sortByReleaseDate
            |> Seq.toArray
        logger.DebugFormat("Sorted branches: {0}", String.Join(", ", sortedBranches |> Seq.map (fun b -> b?name.AsString())))
        let availableReleaseBranches = Seq.length sortedBranches
        let numberOfBranchesToArchive = availableReleaseBranches - numberOfBranchesToKeep
        if numberOfBranchesToArchive <= 0 then
            logger.DebugFormat("There are {0} release branches, but the minimum to keep is {1}, therefore I will not do anything", availableReleaseBranches, numberOfBranchesToKeep)
            (None : DreamMessage option [] option)
        else 
            let branchesToArchive = sortedBranches |> Seq.take (numberOfBranchesToArchive)
            let branchesToArchiveMap = branchesToArchive |> Seq.map (fun branch -> (branch?name.AsString(), branch)) |> Map.ofSeq<string, JsonValue>

            logger.DebugFormat("sortedBranches: {0}", String.Join(", ", sortedBranches |> Seq.map (fun b -> b?name.AsString())))
            logger.DebugFormat("branchesToArchive for repo '{1}': '{0}'", String.Join(", ", branchesToArchive |> Seq.map (fun branch -> branch?name.AsString())), repo)
            let tagCreationMessages = branchesToArchive |> Seq.map (fun branch -> this.CreateLightweightTag owner repo "archive_" branch) |> Seq.toArray

            logger.DebugFormat("tagCreationMessages: {0}", String.Join(", ", tagCreationMessages |> Seq.map (fun msg -> match msg with | Some x -> x.Status | None -> DreamStatus.InternalError)))
            let createdTagMessages = tagCreationMessages |> Seq.filter (fun msg -> match msg with | Some x -> x.Status = DreamStatus.Created | None -> false)
            let createdTagObjects = createdTagMessages |> Seq.map (fun resp -> match resp with | Some x -> JsonValue.Parse(x.ToText()) | None -> JsonValue.Object([] |> Map.ofSeq<string, JsonValue>))
            let createdTagNames = createdTagObjects |> Seq.map (fun reference -> reference?ref.AsString()) |> Seq.toArray

            logger.DebugFormat("Created tag names: '{0}'", String.Join(", ", createdTagNames))
            if Seq.isEmpty createdTagNames then
                logger.DebugFormat("No tags were created")
                (None : DreamMessage option [] option)
            else
                let branchesToDelete =
                    createdTagNames
                    |> Seq.map (fun reference -> reference.Split('/'))
                    |> Seq.map (fun components -> components |> Seq.last)
                    |> Seq.map (fun name -> name.Substring 8)
                    |> Seq.toArray

                logger.DebugFormat("branchesToDelete for repo '{0}': '{1}'", repo, String.Join(", ", branchesToDelete))
                branchesToDelete
                |> Seq.filter (fun branch -> Map.containsKey branch branchesToArchiveMap)
                |> Seq.map (fun branch -> this.DeleteBranch repo "heads" branchesToArchiveMap.[branch])
                |> Seq.toArray
                |> Some

    member this.MergeBranch (repo : string) (source : string) (target : string) (commitMessage : string) =
        let mergePayload = String.Format("""{{ "base": "{0}", "head": "{1}", "commit_message": "{2}" }}""", target, source, commitMessage)
        try
            api.At("repos", owner, repo, "merges").Post(Auth mergePayload)
        with
            | :? DreamResponseException as ex  ->
                logger.ErrorExceptionFormat(ex, "Error found when trying to merge branches on repo '{0}', source '{1}', target '{2}': '{3}'", repo, source, target, ex.Message)
                if ex.Response.Status = DreamStatus.Conflict then
                    raise(new MergeException(repo, source, target, commitMessage, ex))
                else
                    raise ex

    member this.ProcessMergedPullRequest (prMetadata : MindTouch.Domain.MergedPullRequestMetadata) =
        let repo = prMetadata.Repo
        let branches = this.GetBranches repo
        let release = prMetadata.Release.Date
        let branchesToPropagateTo =
            branches
            |> Seq.choose (fun s ->
                               let branchname = s?name.AsString() in
                               let (valid, result) = DateTime.TryParseExact(branchname.Substring 8, MindTouch.DateUtils.DATE_PATTERN, null, DateTimeStyles.None) in
                                   if valid && (result.Date > release) then Some(branchname, result) else None)
            |> Seq.sortBy (fun (_, date) -> date)
            |> Seq.map (fun (branchname, _) -> branchname)
            |> Seq.toArray
        
        // The commit we need to propagate
        let commit = if String.IsNullOrEmpty prMetadata.MergeCommitSHA then prMetadata.Head?sha.AsString() else prMetadata.MergeCommitSHA

        // Hotfix
        (if DateTime.UtcNow.Subtract(new TimeSpan(7, 0, 0)) > release then
            let mergingMessage = String.Format("Merge pull request #{0} : Auto-merging {1} to {2}", prMetadata.PrNumber, prMetadata.Head?label.AsString(), "master")
            logger.Debug (repo + ": " + mergingMessage)
            this.MergeBranch repo commit "master" mergingMessage |> ignore
            branchesToPropagateTo |> Seq.append [| "master" |]
        else
            // Late check-in
            branchesToPropagateTo |> Seq.append [| prMetadata.Base?ref.AsString() |])
       |> Seq.pairwise
       |> Seq.iter (fun (src, target) ->
                        let mergingMessage = String.Format("Merge pull request #{0} : Auto-merging {1} to {2}", prMetadata.PrNumber, src, target)
                        logger.Debug (repo + ": " + mergingMessage)
                        this.MergeBranch repo src target mergingMessage |> ignore)
