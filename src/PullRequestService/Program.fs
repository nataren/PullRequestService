namespace mindtouch
open MindTouch.Dream
open System.Collections.Generic
open MindTouch.Tasking
open MindTouch.Xml
open Autofac
open System
open FSharp.Data.Json

[<DreamService("MindTouch Github Pull Request Guard Service", "Copyright (C) 2013 MindTouch Inc.", SID = [| "sid://mindtouch.com/2013/05/pullrequestservice" |])>]
[<DreamServiceConfig("github.token", "string", "The personal authorization token issued by GitHub")>]
type PullRequestService() =
    inherit DreamService()
    let mutable token = ""

    override this.Start(config : XDoc, container : ILifetimeScope, result : Result) =
        token <- config.["github.token"].AsText
        result.Return()
        Seq.empty<IYield>.GetEnumerator()
        
    [<DreamFeature("POST:notify", "Receive a pull request notification")>]
    member this.HandleGithubMessage(context : DreamContext, request : DreamMessage) =
        let payload = JsonValue.Parse(request.ToText())
        DreamMessage.Ok(MimeType.TEXT, payload.ToString())

    [<DreamFeature("GET:status", "Check the service's status")>]
    member this.GetStatus(context : DreamContext, request : DreamMessage) =
        DreamMessage.Ok(MimeType.TEXT, "Everything running smoothly ...")