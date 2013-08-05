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
open Microsoft.FSharp.Collections

exception MissingConfig of string

[<DreamService(
    "MindTouch Github Pull Request Service",
    "Copyright (C) 2013 MindTouch Inc.",
    SID = [| "sid://mindtouch.com/2013/05/pullrequestservice" |])>]
[<DreamServiceConfig("github.token", "string", "A Github's personal API access token")>]
[<DreamServiceConfig("github.owner", "string", "The owner of the repos we want to watch")>]
[<DreamServiceConfig("github.repos", "string", "Comma separated list of repos to watch")>]
[<DreamServiceConfig("public.uri", "string?", @"The end-point's public URI to use to communicate with this service.
It is optional, used mostly for testing purposes with a public proxy and the service running locally")>]
type PullRequestService() =
    inherit DreamService()
    let GITHUB_API = Plug.New(new XUri("https://api.github.com"))
    let mutable token = None
    let mutable owner = None
    let mutable publicUri = None
                
    override this.Start(config : XDoc, container : ILifetimeScope, result : Result) =
        
        // Gather
        let config' = this.GetConfigValue config
        token <- config' "github.token"
        owner <- config' "github.owner"
        let repos = config' "github.repos"
        publicUri <- config' "public.uri"
        match publicUri with
        | None -> publicUri <- Some(this.Self.Uri.At("notify").AsPublicUri().ToString())
        | _ -> ()
        
        // Validate
        this.ValidateConfig "github.token" token
        this.ValidateConfig "github.owner" owner
        this.ValidateConfig "github.repos" repos
        
        // Use
        this.CreateWebHooks(repos.Value.Split(','))
        result.Return()
        Seq.empty<IYield>.GetEnumerator()
        
    [<DreamFeature("POST:notify", "Receive a pull request notification")>]
    member this.HandleGithubMessage (context : DreamContext) (request : DreamMessage) =
        let pr = JsonValue.Parse(request.ToText())
        if this.InvalidPullRequest(pr)
        then this.ClosePullRequest(pr)
        else DreamMessage.Ok(MimeType.JSON, "Pull request does not target master branch, will ignore"B)

    [<DreamFeature("GET:status", "Check the service's status")>]
    member this.GetStatus (context : DreamContext) (request : DreamMessage) =
        DreamMessage.Ok(MimeType.JSON, "Running ...")
        
    member this.Json(payload : string, headers : seq<KeyValuePair<string, string>>) =
        new DreamMessage(DreamStatus.Ok, new DreamHeaders(headers), MimeType.JSON, payload)

    member this.ValidateConfig (key : string) value =
        match value with
        | None -> raise(MissingConfig(key))
        | _ -> ()

    member this.GetConfigValue (doc : XDoc) (key : string) =
        let configVal = doc.[key].AsText
        if configVal = null then
            None
        else
            Some configVal

    member this.InvalidPullRequest (pr : JsonValue) =
        let action = pr?action.AsString()
        (action = "opened" || action = "reopened") && pr.["pull_request"].["base"].["ref"].AsString() = "master"

    member this.ClosePullRequest (pr : JsonValue) =
        Plug.New(new XUri(pr?pull_request?url.AsString())).Post(this.Json("""{ "state" : "closed"  }""", [| new KeyValuePair<string, string>("Authorization", "token " + token.Value); new KeyValuePair<string, string>("X-HTTP-Method-Override", "PATCH") |]))
        
    member this.CreateWebHooks repos =
        repos
        |> Seq.filter (fun repo -> not(this.WebHookExist(repo)))
        |> Seq.iter (fun repo -> this.CreateWebHook(repo))
        
    member this.WebHookExist repo =
        let auth = this.Json("", [| new KeyValuePair<string, string>("Authorization", "token " + token.Value) |])
        let hooks : JsonValue[] = JsonValue.Parse(GITHUB_API.At("repos", owner.Value, repo, "hooks").Get(auth).ToText()).AsArray()
        let isPullRequestEvent (events : JsonValue[]) = Seq.exists (fun (event : JsonValue) -> event.AsString() = "pull_request") events
        Seq.exists (fun (hook : JsonValue) -> isPullRequestEvent(hook?events.AsArray()) && hook?name.AsString() = "web" && hook?config?url.AsString() = publicUri.Value) hooks
        
    member this.CreateWebHook repo =
        let createHook = this.Json(String.Format("""{{ "name" : "web", "events" : ["pull_request"], "config" : {{ "url" : "{0}", "content_type" : "json" }} }}""",  publicUri.Value), [| new KeyValuePair<string, string>("Authorization", "token " + token.Value) |])
        try
            ignore(GITHUB_API.At("repos", owner.Value, repo, "hooks").Post(createHook))
        with
        | _ -> ()
   
        