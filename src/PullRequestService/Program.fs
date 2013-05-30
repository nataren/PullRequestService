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
// TODO(cesarn): Create constants inside a module for all the config key names, and reuse
// TODO(cesarn): Confirm what kind of encoding on the segments is necessary

[<DreamService(
    "MindTouch Github Pull Request Service",
    "Copyright (C) 2013 MindTouch Inc.",
    SID = [| "sid://mindtouch.com/2013/05/pullrequestservice" |])>]
[<DreamServiceConfig("github.token", "string", "A Github's personal API access token")>]
[<DreamServiceConfig("github.owner", "string", "The owner of the repos we want to watch")>]
[<DreamServiceConfig("github.repos", "string", "Comma separated list of repos to watch")>]
[<DreamServiceConfig("public.uri", "string", "The public URI where this service is found")>]
type PullRequestService() =
    inherit DreamService()
    let GITHUB_API = Plug.New(new XUri("https://api.github.com"))
    let mutable token = None
    let mutable owner = None
    let mutable publicUri = None
                
    override this.Start(config : XDoc, container : ILifetimeScope, result : Result) =
        
        // Gather
        token <- Some config.["github.token"].AsText
        owner <- Some config.["github.owner"].AsText
        let repos = Some config.["github.repos"].AsText
        publicUri <- Some config.["public.uri"].AsText
        
        // Validate
        this.ValidateConfig("github.token", token)
        this.ValidateConfig("github.owner", owner)
        this.ValidateConfig("github.repos", repos)
        this.ValidateConfig("public.uri", publicUri)
        
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
        DreamMessage.Ok(MimeType.JSON, "Everything running smoothly ...")
        
    member this.ValidateConfig(key, value) =
        match value with
        | None -> raise(MissingConfig(key))
        | _ -> ()

    member this.InvalidPullRequest(pr : JsonValue) =
        let action = pr?action.AsString()
        (action = "opened" || action = "reopened") && pr.["pull_request"].["base"].["ref"].AsString() = "master"
        
    member this.ClosePullRequest(pr : JsonValue) =
        let close =
            new DreamMessage(
                DreamStatus.Ok,
                new DreamHeaders([| new KeyValuePair<string, string>("Authorization", "token " + token.Value);  new KeyValuePair<string, string>("X-HTTP-Method-Override", "PATCH") |]),
                MimeType.JSON,
                """{ "state" : "closed"  }""")
        Plug.New(new XUri(pr?pull_request?url.AsString())).Post(close)
        
    member this.CreateWebHooks(repos) =
        repos
        |> Seq.filter (fun repo -> not(this.WebHookExist(repo)))
        |> Seq.iter (fun repo -> this.CreateWebHook(repo))
        
    member this.WebHookExist repo =
        let auth =
            new DreamMessage(
                DreamStatus.Ok,
                new DreamHeaders([| new KeyValuePair<string, string>("Authorization", "token " + token.Value) |]),
                MimeType.JSON,
                "")
        let hooks : JsonValue[] = JsonValue.Parse(GITHUB_API.At("repos", owner.Value, repo, "hooks").Get(auth).ToText()).AsArray()
        
        // TODO(cesarn): Use proper pattern matching
        let isPullRequestEvent (events : JsonValue[]) = Seq.exists (fun (event : JsonValue) -> event.AsString() = "pull_request") events
        Seq.exists (fun (hook : JsonValue) -> isPullRequestEvent(hook?events.AsArray()) && hook?name.AsString() = "web" && hook?config?url.AsString() = publicUri.Value) hooks
        
    member this.CreateWebHook repo =
        let createHook =
            new DreamMessage(
                DreamStatus.Ok,
                new DreamHeaders([| new KeyValuePair<string, string>("Authorization", "token " + token.Value) |]),
                MimeType.JSON,
                String.Format("""{{ "name" : "web", "events" : ["pull_request"], "config" : {{ "url" : "{0}", "content_type" : "json" }} }}""", publicUri.Value))
        try
            ignore(GITHUB_API.At("repos", owner.Value, repo, "hooks").Post(createHook))
        with
        | _ -> ()
        