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
namespace mindtouch

open System
open System.Collections.Generic
open Autofac
open MindTouch.Dream
open MindTouch.Tasking
open MindTouch.Xml
open FSharp.Data.Json
open FSharp.Data.Json.Extensions

exception MissingConfig of string

[<DreamService(
    "MindTouch Github Pull Request Service",
    "Copyright (C) 2013 MindTouch Inc.",
    SID = [| "sid://mindtouch.com/2013/05/pullrequestservice" |])>]
[<DreamServiceConfig("github.token", "string", "A Github's personal API access token")>]
[<DreamServiceConfig("github.owner", "string", "The owner of the repos we want to watch")>]
[<DreamServiceConfig("github.repos", "string", "Comma separated list of repos to watch")>]
type PullRequestService() =
    inherit DreamService()
    let GITHUB_API = Plug.New(new XUri("https://api.github.com"))
    let mutable token = None
    let mutable owner = None
                
    override this.Start(config : XDoc, container : ILifetimeScope, result : Result) =
        
        // Gather
        token <- Some config.["github.token"].AsText
        owner <- Some config.["github.owner"].AsText
        let repos = Some config.["github.repos"].AsText
        
        // Validate
        this.ValidateConfig("github.token", token)
        this.ValidateConfig("github.owner", owner)
        this.ValidateConfig("github.repos", repos)
        
        // Use
        this.CreateWebHooks(repos.Value.Split(','))
        result.Return()
        Seq.empty<IYield>.GetEnumerator()
        
    [<DreamFeature("POST:notify", "Receive a pull request notification")>]
    member this.HandleGithubMessage(context : DreamContext, request : DreamMessage) =
        let pr = JsonValue.Parse(request.ToText())
        if this.InvalidPullRequest(pr)
        then this.ClosePullRequest(pr)
        else DreamMessage.Ok(MimeType.JSON, "Pull request does not target master branch, will ignore"B)

    [<DreamFeature("GET:status", "Check the service's status")>]
    member this.GetStatus(context : DreamContext, request : DreamMessage) =
        DreamMessage.Ok(MimeType.JSON, "Running ...")
        
    member this.Json(payload : string, extraHeader : KeyValuePair<string, string>) =
        new DreamMessage(DreamStatus.Ok, new DreamHeaders([| new KeyValuePair<string, string>("Authorization", "token " + token.Value); extraHeader |]), MimeType.JSON, payload)
        
    member this.ValidateConfig(key, value) =
        match value with
        | None -> raise(MissingConfig(key))
        | _ -> ()

    member this.InvalidPullRequest(pr : JsonValue) =
        let action = pr?action.AsString()
        (action = "opened" || action = "reopened") && pr.["pull_request"].["base"].["ref"].AsString() = "master"
        
    member this.ClosePullRequest(pr : JsonValue) =
        Plug.New(new XUri(pr?pull_request?url.AsString())).Post(this.Json("""{ "state" : "closed"  }""", new KeyValuePair<string, string>("X-HTTP-Method-Override", "PATCH")))
        
    member this.CreateWebHooks(repos) =
        repos
        |> Seq.filter (fun repo -> not(this.WebHookExist(repo)))
        |> Seq.iter (fun repo -> this.CreateWebHook(repo))
        
    member this.WebHookExist repo =
        let auth = this.Json("", new KeyValuePair<string, string>())
        let hooks : JsonValue[] = JsonValue.Parse(GITHUB_API.At("repos", owner.Value, repo, "hooks").Get(auth).ToText()).AsArray()
        let isPullRequestEvent (events : JsonValue[]) = Seq.exists (fun (event : JsonValue) -> event.AsString() = "pull_request") events
        Seq.exists (fun (hook : JsonValue) -> isPullRequestEvent(hook?events.AsArray()) && hook?name.AsString() = "web" && hook?config?url.AsString() = this.Self.Uri.At("notify").AsPublicUri().ToString()) hooks
        
    member this.CreateWebHook repo =
        let createHook = this.Json(String.Format("""{{ "name" : "web", "events" : ["pull_request"], "config" : {{ "url" : "{0}", "content_type" : "json" }} }}""", this.Self.Uri.At("notify").AsPublicUri().ToString()), new KeyValuePair<string, string> ())
        try
            ignore(GITHUB_API.At("repos", owner.Value, repo, "hooks").Post(createHook))
        with
        | _ -> ()
   
        