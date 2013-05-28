namespace mindtouch
open MindTouch.Dream
open System.Collections.Generic
open MindTouch.Tasking
open MindTouch.Xml
open Autofac
open System
open FSharp.Data.Json
open FSharp.Data.Json.Extensions

[<DreamService("MindTouch Github Pull Request Guard Service", "Copyright (C) 2013 MindTouch Inc.", SID = [| "sid://mindtouch.com/2013/05/pullrequestservice" |])>]
[<DreamServiceConfig("github.token", "string", "The personal authorization token issued by GitHub")>]
[<DreamServiceConfig("public.uri", "string", "The public URI where this service is found")>]
type PullRequestService() =
    inherit DreamService()
    let mutable token = ""
    let mutable publicUri = ""
    let GITHUB_API = "https://api.github.com"
                
    override this.Start(config : XDoc, container : ILifetimeScope, result : Result) =
        token <- config.["github.token"].AsText
        publicUri <- config.["public.uri"].AsText

        // TODO(cesarn): Create web hook automatically?
        result.Return()
        Seq.empty<IYield>.GetEnumerator()
        
    [<DreamFeature("POST:notify", "Receive a pull request notification")>]
    member this.HandleGithubMessage(context : DreamContext, request : DreamMessage) =
        let pr = JsonValue.Parse(request.ToText())
        if this.InvalidPullRequest(pr)
        then this.ClosePullRequest(pr)
        DreamMessage.Ok(MimeType.TEXT, pr.ToString())

    [<DreamFeature("GET:status", "Check the service's status")>]
    member this.GetStatus(context : DreamContext, request : DreamMessage) =
        DreamMessage.Ok(MimeType.TEXT, "Everything running smoothly ...")
        
    member this.InvalidPullRequest(pullRequest : JsonValue) =
        pullRequest?pull_request?base?ref = "master"
        
    member this.ClosePullRequest(pullRequest : JsonValue) =
        true
        