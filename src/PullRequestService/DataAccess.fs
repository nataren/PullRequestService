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
namespace MindTouch
open System
open System.Collections.Generic

open MindTouch.Dream

open FSharp.Data.Json
open FSharp.Data.Json.Extensions

open log4net

module DataAccess =
    open Data
    type Github(owner, token) =
        let owner = owner
        let token = token
        let logger = LogManager.GetLogger typedefof<Github>
        let GITHUB_API = Plug.New(new XUri("https://api.github.com"))
        let AUTH_PAIR = new KeyValuePair<_, _>("Authorization", "token " + token)

        let Json(payload : string, headers : seq<KeyValuePair<string, string>>) =
            new DreamMessage(DreamStatus.Ok, new DreamHeaders(headers), MimeType.JSON, payload)
        
        let Auth = Json("", [| AUTH_PAIR |])

        member this.ClosePullRequest (prUri : XUri) =
            logger.DebugFormat("Will try to close '{0}'", prUri)
            Plug.New(prUri)
                .Post(Json("""{ "state" : "closed"  }""", [| AUTH_PAIR; new KeyValuePair<_, _>("X-HTTP-Method-Override", "PATCH") |]))
    
        member this.MergePullRequest (prUri : XUri) =
            logger.DebugFormat("Will try to merge '{0}'", prUri)
            let mergePlug = Plug.New(prUri.At("merge"))
            mergePlug.Put(Json("{}", [| AUTH_PAIR |]))

        member this.WebHookExist repo publicUri =
            let hooks = JsonValue.Parse(GITHUB_API.At("repos", owner, repo, "hooks").Get(Auth).ToText()).AsArray()
            let isPullRequestEvent (events : JsonValue[]) = Seq.exists (fun (event : JsonValue) -> event.AsString() = "pull_request") events
            hooks |> Seq.exists (fun (hook : JsonValue) -> isPullRequestEvent(hook?events.AsArray()) && hook?name.AsString() = "web" && hook?config?url.AsString() = publicUri)
        
        member this.CreateWebHook repo (publicUri : string) =
            let createHook = Json(String.Format("""{{ "name" : "web", "events" : ["pull_request"], "config" : {{ "url" : "{0}", "content_type" : "json" }} }}""",  publicUri), [| AUTH_PAIR |])
            GITHUB_API.At("repos", owner, repo, "hooks").Post(createHook)

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
            JsonValue.Parse(GITHUB_API.At("repos", owner, repo, "pulls").Get(Auth).ToText()).AsArray()

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

        member this.ProcessPullRequestType pollAction prEventType =
            match prEventType with
            | Invalid i -> this.ClosePullRequest i
            | UnknownMergeability uri -> this.PollPullRequest uri pollAction
            | AutoMergeable uri -> this.MergePullRequest uri
            | Skip -> DreamMessage.Ok(MimeType.JSON, JsonValue.String("Pull request needs to be handled by a human since is not targeting an open branch or the master branch").ToString())

        member this.GetPullRequestDetails (prUri : XUri) =
            Plug.New(prUri).Get(Auth)

        member this.GetRepoBranches(repo) =
            JsonValue.Parse(GITHUB_API.At("repos", owner, repo, "branches").Get(Auth).ToText()).AsArray()


      
