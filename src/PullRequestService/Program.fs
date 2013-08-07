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
[<DreamServiceConfig("public.uri", "string?", @"The notify end-point's full public URI to use to communicate with this service")>]
type PullRequestService() =
    inherit DreamService()

    //--- Fields ---
    let GITHUB_API = Plug.New(new XUri("https://api.github.com"))
    let mutable token = None
    let mutable owner = None
    let mutable publicUri = None
    let DATE_PATTERN = "yyyyMMdd"

    //--- Functions ---
    let OpenPullRequest pr =
        let action = pr?action.AsString()
        (action = "opened" || action = "reopened")
    
    let InvalidPullRequest pr =
         OpenPullRequest pr && pr?pull_request?``base``?ref.AsString() = "master"

    let AutoMergeablePullRequest pr =
        let action = pr?action.AsString()
        let targetBranch = pr?pull_request?``base``?ref.AsString()
        try
            let targetBranchDate = DateTime.ParseExact(targetBranch.Substring(targetBranch.Length - DATE_PATTERN.Length), DATE_PATTERN, null)
            OpenPullRequest pr && (targetBranchDate - DateTime.Now.Date).Days >= 6
        with
            | ex -> false

    let ValidateConfig key value =
        match value with
        | None -> raise(MissingConfig key)
        | _ -> ()

    let GetConfigValue (doc : XDoc) (key : string) =
        let configVal = doc.[key].AsText
        if configVal = null then
            None
        else
            Some configVal

    let Json(payload : string, headers : seq<KeyValuePair<string, string>>) =
        new DreamMessage(DreamStatus.Ok, new DreamHeaders(headers), MimeType.JSON, payload)

    //--- Active Patterns ---
    let (|Invalid | AutoMergeable | Skip|) pr =
        if InvalidPullRequest(pr) then
            Invalid
        else if AutoMergeablePullRequest(pr) then
            AutoMergeable
        else
            Skip

    //--- Methods ---
    override this.Start(config : XDoc, container : ILifetimeScope, result : Result) =
        
        // Gather
        let config' = GetConfigValue config
        token <- config' "github.token"
        owner <- config' "github.owner"
        let repos = config' "github.repos"
        publicUri <- config' "public.uri"
        match publicUri with
        | None -> publicUri <- Some(this.Self.Uri.At("notify").AsPublicUri().ToString())
        | _ -> ()
        
        // Validate
        ValidateConfig "github.token" token
        ValidateConfig "github.owner" owner
        ValidateConfig "github.repos" repos
        
        // Use
        this.CreateWebHooks(repos.Value.Split(','))
        result.Return()
        Seq.empty<IYield>.GetEnumerator()
        
    [<DreamFeature("POST:notify", "Receive a pull request notification")>]
    member public this.HandleGithubMessage (context : DreamContext) (request : DreamMessage) =
        let pr = JsonValue.Parse(request.ToText())
        match pr with
        | Invalid -> this.ClosePullRequest pr
        | AutoMergeable ->  this.MergePullRequest pr
        | Skip -> DreamMessage.Ok(MimeType.JSON, "Pull request needs to be handled by a human since is not targeting an open branch or the master branch"B)

    [<DreamFeature("GET:status", "Check the service's status")>]
    member public this.GetStatus (context : DreamContext) (request : DreamMessage) =
        DreamMessage.Ok(MimeType.JSON, "Running ...")
    
    // Pull requests methods
    member private this.ClosePullRequest pr =
        Plug.New(new XUri(pr?pull_request?url.AsString()))
            .Post(Json("""{ "state" : "closed"  }""", [| new KeyValuePair<string, string>("Authorization", "token " + token.Value); new KeyValuePair<string, string>("X-HTTP-Method-Override", "PATCH") |]))

    member private this.MergePullRequest pr =
        let prUri = new XUri(pr?pull_request?url.AsString())
        let mergePlug = Plug.New(prUri.At("merge"))
        mergePlug.Put(Json("{}", [| new KeyValuePair<string, string>("Authorization", "token " + token.Value) |]))

    // Webhooks methods
    member private this.CreateWebHooks repos =
        repos
        |> Seq.filter (fun repo -> not(this.WebHookExist repo))
        |> Seq.iter (fun repo -> this.CreateWebHook repo)
        
    member private this.WebHookExist repo =
        let auth = Json("", [| new KeyValuePair<string, string>("Authorization", "token " + token.Value) |])
        let hooks : JsonValue[] = JsonValue.Parse(GITHUB_API.At("repos", owner.Value, repo, "hooks").Get(auth).ToText()).AsArray()
        let isPullRequestEvent (events : JsonValue[]) = Seq.exists (fun (event : JsonValue) -> event.AsString() = "pull_request") events
        hooks |> Seq.exists (fun (hook : JsonValue) -> isPullRequestEvent(hook?events.AsArray()) && hook?name.AsString() = "web" && hook?config?url.AsString() = publicUri.Value)
        
    member private this.CreateWebHook repo =
        let createHook = Json(String.Format("""{{ "name" : "web", "events" : ["pull_request"], "config" : {{ "url" : "{0}", "content_type" : "json" }} }}""",  publicUri.Value), [| new KeyValuePair<string, string>("Authorization", "token " + token.Value) |])
        try
            ignore(GITHUB_API.At("repos", owner.Value, repo, "hooks").Post(createHook))
        with
        | _ -> ()