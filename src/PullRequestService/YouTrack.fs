namespace MindTouch

open log4net
open MindTouch.Dream
open System.Collections.Generic

module YouTrack =
    type t(hostname : string, username, password) =
        let YOUTRACK_API =  Plug.New(new XUri(hostname)).WithCredentials(username, password)

        member this.GetIssue(issue) =
            YOUTRACK_API
                .At("rest", "issue", issue)
                .WithHeader("Accept", MimeType.JSON.ToString())
                .Get()
                