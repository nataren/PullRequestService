(*
 * MindTouch.Domain - Helper data domain objects
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
module MindTouch.Domain

open log4net
open MindTouch.Dream
open System
open FSharp.Data.Json

type MergedPullRequestMetadata = {
    Repo : String;
    HtmlUri : XUri;
    LinkedYouTrackIssues : seq<string>;
    RepoSshUrl: String;
    Author : String;
    Message : String;
    Release : DateTime;
    Head: JsonValue;
    Base: JsonValue;
    MergeCommitSHA: String;
    PrNumber: int;
}

type PullRequest =
| Invalid of XUri * XUri
| AutoMergeable of XUri
| UnknownMergeability of XUri
| OpenedNotLinkedToYouTrackIssue of XUri * XUri
| ReopenedNotLinkedToYouTrackIssue of string * XUri
| Merged of MergedPullRequestMetadata
| Skip of string * XUri
| ClosedAndNotMerged of XUri
| TargetsExplicitlyFrozenBranch of (string * XUri)
| TargetsSpecificPurposeBranch of XUri