(*
 * MindTouch.Email - A thin wrapper for AWS SES
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
module MindTouch.Email
open log4net

open System
open Microsoft.FSharp.Collections

open Amazon.Runtime
open Amazon.SimpleEmail
open Amazon.SimpleEmail.Model

type t() =
    
    // Build AWS SES
    let awsCreds = new InstanceProfileAWSCredentials()
    let clientConfig = new AmazonSimpleEmailServiceConfig()
    let emailClient = new AmazonSimpleEmailServiceClient(awsCreds, clientConfig)

    member this.SendEmail(from : string, to_ : string , subject : string, textBody : string, htmlBody : string, bccAddresses : seq<string>) : SendEmailResponse =

        // Validate inputs
        if String.IsNullOrEmpty from then
            raise <| ArgumentNullException "from"
        if String.IsNullOrEmpty to_ then
            raise <| ArgumentNullException "to_"
        if String.IsNullOrEmpty subject then
            raise <| ArgumentNullException "subject"

        // Build the email
        let mutable html : Content = null
        let mutable text : Content = null
        if not(htmlBody = null) then
            html <- new Content(htmlBody)
        if not(textBody = null) then
            text <- new Content(textBody)
        let body = new Body()
        body.Html <- html
        body.Text <- text
        let message = new Message(new Content(subject), body)
        let destination = new Destination()
        let toAddresses = new System.Collections.Generic.List<string>()
        toAddresses.Add(to_)
        destination.ToAddresses <- toAddresses
        let l = Collections.Generic.List<string>()
        l.AddRange <| (if bccAddresses = null then Seq.empty<string> else bccAddresses)
        destination.BccAddresses <- l
        let request = new SendEmailRequest()
        request.Source <- from
        request.Destination <- destination
        request.Message <- message
        emailClient.SendEmail(request)