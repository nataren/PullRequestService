(*
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

type PullRequest = MindTouch.PullRequest.t
open log4net
type t(owner, token) =
    let owner = owner
    let token = token
    let logger = LogManager.GetLogger typedefof<t>
    let authPair = new KeyValuePair<_, _>("Authorization", "token " + token)
    let api = Plug.New(new XUri("https://api.github.com"))

    let Json(payload : string, headers : seq<KeyValuePair<string, string>>) =
         new DreamMessage(DreamStatus.Ok, new DreamHeaders(headers), MimeType.JSON, payload)

    let Auth = Json("", [| authPair |])

    let sortByReleaseDate branch =
        MindTouch.DateUtils.getBranchDate <| branch?name.AsString()
    
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
        let auth = Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token) |])
        let hooks = JsonValue.Parse(api.At("repos", owner, repo, "hooks").Get(auth).ToText()).AsArray()
        let isPullRequestEvent (events : JsonValue[]) = Seq.exists (fun (event : JsonValue) -> event.AsString() = "pull_request") events
        hooks |> Seq.exists (fun (hook : JsonValue) -> isPullRequestEvent(hook?events.AsArray()) && hook?name.AsString() = "web" && hook?config?url.AsString() = publicUri)
        
    member this.CreateWebHook repo (publicUri : string) =
        let createHook = Json(String.Format("""{{ "name" : "web", "events" : ["pull_request"], "config" : {{ "url" : "{0}", "content_type" : "json" }} }}""",  publicUri), [| new KeyValuePair<_, _>("Authorization", "token " + token) |])
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
        let auth = Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token) |])
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

    member this.ProcessPullRequestType pollAction mergedPrHandler prEventType =
        match prEventType with
        | PullRequest.Invalid (prUri, commentsUri) -> this.CommentOnPullRequest commentsUri "This pull request is invalid because its target is the master branch, it will be closed" |> ignore; this.ClosePullRequest prUri |> ignore; DreamMessage.Ok() 
        | PullRequest.AutoMergeable uri -> this.MergePullRequest uri
        | PullRequest.UnknownMergeability uri -> this.PollPullRequest uri pollAction
        | PullRequest.OpenedNotLinkedToYouTrackIssue (prUri, commentsUri) -> this.CommentOnPullRequest commentsUri "This just opened pull request is not bound to a YouTrack issue, it will be closed" |> ignore; this.ClosePullRequest prUri
        | PullRequest.ReopenedNotLinkedToYouTrackIssue uri -> this.CommentOnPullRequest uri "This just *reopened* pull request is not bound to a YouTrack issue, it will be ignored but human intervention is required" |> ignore; DreamMessage.Ok()
        | PullRequest.Merged mergedPrMetadata -> mergedPrHandler mergedPrMetadata |> ignore; DreamMessage.Ok()
        | PullRequest.Skip uri -> this.CommentOnPullRequest uri "This pull request is going to be ignored, human intervention required" |> ignore; DreamMessage.Ok(MimeType.JSON, JsonValue.String("Pull request needs to be handled by a human since is not targeting an open branch or the master branch").ToString())

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
        JsonValue.Parse(api.At("repos", owner, repo, "branches").Get(Auth).ToText()).AsArray()
    
    member this.GetBranch repo branch =
        JsonValue.Parse(api.At("repos", owner, repo, "branches", branch).Get(Auth).ToText())

    member this.GetBranches repo =
        (this.GetRepoBranchesInfo repo)
        |> Seq.map (fun branch -> branch?name.AsString())
        |> Seq.filter (fun branch -> branch.StartsWith("release_"))
        |> Seq.map (fun branch -> this.GetBranch repo branch)
        |> Seq.sortBy sortByReleaseDate

    member this.DeleteReference ref = 
        ()

    member this.CreateTag =
        ()

    member this.ArchiveBranches repo branchesToKeep =
        let branches = this.GetBranches repo
        branches
        |> Seq.sortBy sortByReleaseDate
        |> Seq.take (Seq.length branches - branchesToKeep)




