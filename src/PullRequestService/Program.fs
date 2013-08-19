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
open log4net

exception MissingConfig of string

type PullRequestType =
| Invalid of XUri
| AutoMergeable of XUri
| UnknownMergeability of XUri
| Skip

type Agent<'T> = MailboxProcessor<'T>

[<DreamService(
    "MindTouch Github Pull Request Service",
    "Copyright (C) 2013 MindTouch Inc.",
    SID = [| "sid://mindtouch.com/2013/05/pullrequestservice" |])>]
[<DreamServiceConfig("github.token", "string", "A Github's personal API access token")>]
[<DreamServiceConfig("github.owner", "string", "The owner of the repos we want to watch")>]
[<DreamServiceConfig("github.repos", "string", "Comma separated list of repos to watch")>]
[<DreamServiceConfig("public.uri", "string?", "The notify end-point's full public URI to use to communicate with this service")>]
type PullRequestService() =
    inherit DreamService()

    //--- Fields ---
    let GITHUB_API = Plug.New(new XUri("https://api.github.com"))
    let mutable token = None
    let mutable owner = None
    let mutable publicUri = None
    let DATE_PATTERN = "yyyyMMdd"
    let logger = LogManager.GetLogger typedefof<PullRequestService>

    // Supervisor agent
    let supervisor =
        Agent<Exception>.Start(fun inbox -> async {
            while true do
                let!  err = inbox.Receive()
                logger.DebugFormat("An error ocurred in an agent: %A", err)
        })

    // Polling agent
    let pollAgent =
        Agent.Start(fun inbox ->
            let rec loop (queue : Queue<XUri>) = async {
                let! msg = inbox.Receive()
                queue.Enqueue(msg)
                return! loop queue
            }
            loop (new Queue<XUri>()))

    let mergeAgent =
        Agent.Start(fun inbox ->
            let rec loop = async {
                return! loop
            }
            loop)

    //--- Functions ---
    let OpenPullRequest pr =
        let action = pr?action.AsString()
        action = "opened" || action = "reopened"
    
    let InvalidPullRequest pr =
         OpenPullRequest pr && pr?pull_request?``base``?ref.AsString() = "master"

    let AutoMergeablePullRequest pr =
        let action = pr?action.AsString()
        let targetBranch = pr?pull_request?``base``?ref.AsString()
        let targetBranchDate = DateTime.ParseExact(targetBranch.Substring(targetBranch.Length - DATE_PATTERN.Length), DATE_PATTERN, null)
        OpenPullRequest pr && pr?mergeable.AsBoolean() && (targetBranchDate - DateTime.UtcNow.Date).Days >= 6

    let UnknownMergeabilityPullRequest pr =
        let action = pr?action.AsString()
        OpenPullRequest pr && pr?mergeable_state.AsString() = "unknown"

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

    let DeterminePullRequestType pr =
        let pullRequestUrl = pr?pull_request?url.AsString()
        if InvalidPullRequest pr then
            Invalid (new XUri(pullRequestUrl))
        else if UnknownMergeabilityPullRequest pr then
            UnknownMergeability (new XUri(pullRequestUrl))
        else if AutoMergeablePullRequest pr then
            AutoMergeable (new XUri(pullRequestUrl))
        else
            Skip

    //--- Methods ---
    // Pull requests methods
    let ClosePullRequest (prUri : XUri) =
        Plug.New(prUri)
            .Post(Json("""{ "state" : "closed"  }""", [| new KeyValuePair<_, _>("Authorization", "token " + token.Value); new KeyValuePair<_, _>("X-HTTP-Method-Override", "PATCH") |]))

    let QueueMergePullRequest (prUri : XUri) =
        pollAgent.Post(prUri)
        DreamMessage.Ok()

    let MergePullRequest (prUri : XUri) =
        let mergePlug = Plug.New(prUri.At("merge"))
        mergePlug.Put(Json("{}", [| new KeyValuePair<_, _>("Authorization", "token " + token.Value) |]))

    // Webhooks methods
    let WebHookExist repo =
        let auth = Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token.Value) |])
        let hooks = JsonValue.Parse(GITHUB_API.At("repos", owner.Value, repo, "hooks").Get(auth).ToText()).AsArray()
        let isPullRequestEvent (events : JsonValue[]) = Seq.exists (fun (event : JsonValue) -> event.AsString() = "pull_request") events
        hooks |> Seq.exists (fun (hook : JsonValue) -> isPullRequestEvent(hook?events.AsArray()) && hook?name.AsString() = "web" && hook?config?url.AsString() = publicUri.Value)
        
    let CreateWebHook repo =
        let createHook = Json(String.Format("""{{ "name" : "web", "events" : ["pull_request"], "config" : {{ "url" : "{0}", "content_type" : "json" }} }}""",  publicUri.Value), [| new KeyValuePair<_, _>("Authorization", "token " + token.Value) |])
        GITHUB_API.At("repos", owner.Value, repo, "hooks").Post(createHook)

    let CreateWebHooks repos =
        repos
        |> Seq.filter (fun repo -> not(WebHookExist repo))
        |> Seq.iter (fun repo ->
            try
                CreateWebHook repo |> ignore
            with
            | ex -> raise(new Exception(String.Format("Repo '{0}' failed to initialize ({1})", repo, ex))))

    override this.Start(config : XDoc, container : ILifetimeScope, result : Result) =
        // NOTE(cesarn): Commented out for now
        // base.Start(config, container, result)

        // Gather
        let config' = GetConfigValue config
        token <- config' "github.token"
        owner <- config' "github.owner"
        let repos = config' "github.repos"
        publicUri <- config' "public.uri"
        
        // Validate
        ValidateConfig "github.token" token
        ValidateConfig "github.owner" owner
        ValidateConfig "github.repos" repos
        ValidateConfig "public.uri" publicUri
        
        // Use
        CreateWebHooks(repos.Value.Split(','))

        // Start the agents
        pollAgent.Error.Add(fun error -> supervisor.Post error)
        pollAgent.Start()
        result.Return()
        Seq.empty<IYield>.GetEnumerator()

    [<DreamFeature("POST:notify", "Receive a pull request notification")>]
    member this.HandleGithubMessage (context : DreamContext) (request : DreamMessage) =
        let requestText = request.ToText()
        logger.DebugFormat("Payload: ({0})", requestText)
        match DeterminePullRequestType(JsonValue.Parse(requestText)) with
        | Invalid i -> ClosePullRequest i
        | UnknownMergeability uri -> QueueMergePullRequest uri
        | AutoMergeable uri -> MergePullRequest uri
        | Skip -> DreamMessage.Ok(MimeType.JSON, "Pull request needs to be handled by a human since is not targeting an open branch or the master branch"B)

    [<DreamFeature("GET:status", "Check the service's status")>]
    member this.GetStatus (context : DreamContext) (request : DreamMessage) =
        DreamMessage.Ok(MimeType.JSON, "Running ...")