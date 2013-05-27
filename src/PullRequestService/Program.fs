namespace mindtouch
open MindTouch.Dream
open System.Collections.Generic
open MindTouch.Tasking
open MindTouch.Xml
open Autofac
open System
open Seq

[<DreamService("MindTouch Github Pull Request Guard Service", "Copyright (C) 2013 MindTouch Inc.", SID = [| "sid://mindtouch.com/2013/05/pullrequestservice" |])>]
[<DreamServiceConfig("github.token", "string", "The personal authorization token issued by GitHub")>]
type PullRequestService() =
    inherit DreamService()
        override self.Start(config : XDoc, container : ILifetimeScope, result : Result) =
            let token = config.["github.token"].AsText()
            Seq.empty<IYield>.GetEnumerator()

        [<DreamFeature("POST:notify", "Receive a pull request notification")>]
        member self.HandleGithubMessage(context : DreamContext, request : DreamMessage) =
            DreamMessage.Ok(MimeType.TEXT, "yay, post handler!")

        [<DreamFeature("GET:status", "Check the service's status")>]
        member self.GetStatus(context : DreamContext, request : DreamMessage) =
            DreamMessage.Ok(MimeType.TEXT, "Everything running smoothly ...")