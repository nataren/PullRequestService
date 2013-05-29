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
    let GITHUB_API = new XUri("https://api.github.com")
                
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

    [<DreamFeature("GET:status", "Check the service's status")>]
    member this.GetStatus(context : DreamContext, request : DreamMessage) =
        DreamMessage.Ok(MimeType.TEXT, "Everything running smoothly ...")
        
    member this.InvalidPullRequest(pr : JsonValue) =
        let action = pr?action.AsString()
        action = "open" || action = "reopen" && pr.["pull_request"].["base"].["ref"].AsString() = "master"
        
    member this.ClosePullRequest(pr : JsonValue) =
        let update =
            new DreamMessage(
                DreamStatus.Ok,
                new DreamHeaders([| new KeyValuePair<string, string>("Authorization", "token " + token);  new KeyValuePair<string, string>("X-HTTP-Method-Override", "PATCH") |]),
                MimeType.JSON,
                """{ "state":"closed"  }""")
        let pullUrl = new XUri(pr?pull_request?url.AsString())
        Plug.New(GITHUB_API).At(pullUrl.Segments).Post(update) |> ignore
        