namespace MindTouch

open log4net
open MindTouch.Dream
open System.Collections.Generic

module YouTrack =
    type t(hostname : string, username, password) =
        let logger = LogManager.GetLogger typedefof<t>
        let api =  Plug.New(new XUri(hostname)).WithCredentials(username, password)

        member this.IssuesValidator (issues : seq<string>) =
            logger.DebugFormat("IssuesValidator with '{0}'", issues)
            not << Seq.isEmpty <| this.FilterOutNotExistentIssues issues

        member this.FilterOutNotExistentIssues (issues : seq<string>) =
            issues
            |> Seq.filter (fun issue ->
                logger.DebugFormat("Will try to validate '{0}'", issue)
                let validatedIssues = new List<string>()
                try
                    this.IssueExists(issue)
                with
                    | :? DreamResponseException as ex -> logger.DebugFormat("Failed to retrieve issue '{0}', the response was '{1}', with message '{2}'", issue, ex.Response.Status, ex.Message); false
                    | ex -> logger.DebugFormat("Failed to retrieve the issue '{0}', the message was '{1}'", issue, ex.Message); false)

        member this.GetIssue (issue : string) =
            api.At("rest", "issue", issue).WithHeader("Accept", MimeType.JSON.ToString()).Get()

        member this.IssueExists (issue : string) =
            api.At("rest", "issue", issue, "exists").WithHeader("Accept", MimeType.JSON.ToString()).Get().Status = DreamStatus.Ok