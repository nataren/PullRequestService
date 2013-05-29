namespace mindtouch

open System
open System.Collections.Generic
open Autofac
open MindTouch.Dream
open MindTouch.Tasking
open MindTouch.Xml
open FSharp.Data.Json
open FSharp.Data.Json.Extensions

[<DreamService(
    "MindTouch Github Pull Request Service",
    "Copyright (C) 2013 MindTouch Inc.",
    SID = [| "sid://mindtouch.com/2013/05/pullrequestservice" |])>]
[<DreamServiceConfig("github.token", "string", "The personal authorization token issued by GitHub for the watched repo")>]
type PullRequestService() =
    inherit DreamService()
    let mutable token = ""
                
    override this.Start(config : XDoc, container : ILifetimeScope, result : Result) =
        token <- config.["github.token"].AsText

        // TODO(cesarn): Create web hook automatically?
        // TODO(cesarn): Support multiple repos?
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
        
    member this.InvalidPullRequest(pr : JsonValue) =
        let action = pr?action.AsString()
        (action = "opened" || action = "reopened") && pr.["pull_request"].["base"].["ref"].AsString() = "master"
        
    member this.ClosePullRequest(pr : JsonValue) =
        let close =
            new DreamMessage(
                DreamStatus.Ok,
                new DreamHeaders([| new KeyValuePair<string, string>("Authorization", "token " + token);  new KeyValuePair<string, string>("X-HTTP-Method-Override", "PATCH") |]),
                MimeType.JSON,
                """{ "state":"closed"  }""")
        Plug.New(new XUri(pr?pull_request?url.AsString())).Post(close)
        