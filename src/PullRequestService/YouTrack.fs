namespace MindTouch

open log4net
open MindTouch.Dream
open System.Collections.Generic
open System

module YouTrack =
    type MergedPullRequestMetadata = {
        HtmlUri : XUri;
        LinkedYouTrackIssues : seq<string>;
        Author : String;
        Message : String;
        Release : DateTime
    }

    (* github to youtrack usernames mapping *)
    let github2youtrack = [
        ("nataren", "cesarn");
        ("modethirteen", "andyv");
        ("bjorg", "SteveB");
        ("Petee", "PeteE");
        ("programzeta", "ryanc");
        ("kazad", "kalida");
        ("sdether", "arnec");
        ("yurigorokhov", "yurig");
        ("aaronmars", "aaronm");
        ("theresam", "theresam");
        ("trypton", "karena");
        ("jedapostol", "jeda");
        ("hurgleburgler", "dianaw")] |> Map.ofList

    type t(hostname : string, username, password) =
        let logger = LogManager.GetLogger typedefof<t>
        let api =  Plug.New(new XUri(hostname)).WithCredentials(username, password)

        member this.IssuesValidator (issues : seq<string>) =
            logger.DebugFormat("IssuesValidator with '{0}'", String.Join(",", issues))
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

        member this.ProcessMergedPullRequest (mergedPrMetadata : MergedPullRequestMetadata) =
            mergedPrMetadata.LinkedYouTrackIssues
            |> Seq.iter (fun issue -> 
                try
                    this.VerifyIssue issue mergedPrMetadata.HtmlUri (mergedPrMetadata.Release.ToString("yyyyMMdd")) mergedPrMetadata.Message mergedPrMetadata.Author |> ignore
                with
                | ex -> logger.DebugExceptionMethodCall(ex, "Error happened processing issue '{0}', '{1}'", issue, ex.Message))

        member this.VerifyIssue (issue : string) (prUri : XUri) (release : string) (comment : string) (author : string) =
            let command = String.Format("In Release {0} State Verified{1}", release, match github2youtrack.TryFind(author.ToLowerInvariant()) with | Some username -> " Assignee " + username | None -> "")
            let fullComment = String.Format("{0}\nPull Request: {1}", comment, prUri.ToString())
            logger.DebugFormat("command = '{0}'", command)
            logger.DebugFormat("fullComment = '{0}'", fullComment)
            api.At("rest", "issue", issue, "execute")
                .With("command", command)
                .With("comment", fullComment)
                .With("disableNotifications", true)
                .WithHeader("Accept", MimeType.JSON.ToString())
                .Post()

