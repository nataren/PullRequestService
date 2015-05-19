(*
 * MindTouch.YouTrack - A client to the YouTrack API
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
[<RequireQualifiedAccess>]
module MindTouch.YouTrack
open log4net
open MindTouch.Dream
open System.Collections.Generic
open System

type MergedPullRequestMetadata = {
    HtmlUri : XUri;
    LinkedYouTrackIssues : seq<string>;
    Author : String;
    Message : String;
    Release : DateTime
}

type t(hostname : string, username, password, github2youtrackMapping : Map<string, string>) =
    let github2youtrack = github2youtrackMapping
    let logger = LogManager.GetLogger typedefof<t>
    let api =  Plug.New(new XUri(hostname)).WithCredentials(username, password)

    member this.IssuesValidator (issues : seq<string>) =
        logger.DebugFormat("IssuesValidator with '{0}'", String.Join(", ", issues))
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
        let command = String.Format("In Release {0} S QA{1} State Verified", release, match github2youtrack.TryFind(author.ToLowerInvariant()) with | Some username -> " Assignee " + username | None -> "")
        let fullComment = String.Format("{0}\nPull Request: {1}\nAuthor: {2}", comment, prUri.ToString(), author)
        logger.DebugFormat("command = '{0}'", command)
        logger.DebugFormat("fullComment = '{0}'", fullComment)
        api.At("rest", "issue", issue, "execute")
            .With("command", command)
            .With("comment", fullComment)
            .With("disableNotifications", false)
            .WithHeader("Accept", MimeType.JSON.ToString())
            .Post()

