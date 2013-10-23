namespace MindTouch
open System
open System.Collections.Generic

open MindTouch.Dream

open FSharp.Data.Json
open FSharp.Data.Json.Extensions

open log4net

module DataAccess =
    open Data
    type Github(owner, token, logger : ILog) =
        let owner = owner
        let token = token
        let logger = logger
        let GITHUB_API = Plug.New(new XUri("https://api.github.com"))

        let Json(payload : string, headers : seq<KeyValuePair<string, string>>) =
            new DreamMessage(DreamStatus.Ok, new DreamHeaders(headers), MimeType.JSON, payload)

        member this.ClosePullRequest (prUri : XUri) =
            Plug.New(prUri)
                .Post(Json("""{ "state" : "closed"  }""", [| new KeyValuePair<_, _>("Authorization", "token " + token); new KeyValuePair<_, _>("X-HTTP-Method-Override", "PATCH") |]))
    
        member this.MergePullRequest (prUri : XUri) =
            let mergePlug = Plug.New(prUri.At("merge"))
            mergePlug.Put(Json("{}", [| new KeyValuePair<_, _>("Authorization", "token " + token) |]))

        member this.WebHookExist repo publicUri =
            let auth = Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token) |])
            let hooks = JsonValue.Parse(GITHUB_API.At("repos", owner, repo, "hooks").Get(auth).ToText()).AsArray()
            let isPullRequestEvent (events : JsonValue[]) = Seq.exists (fun (event : JsonValue) -> event.AsString() = "pull_request") events
            hooks |> Seq.exists (fun (hook : JsonValue) -> isPullRequestEvent(hook?events.AsArray()) && hook?name.AsString() = "web" && hook?config?url.AsString() = publicUri)
        
        member this.CreateWebHook repo (publicUri : string) =
            let createHook = Json(String.Format("""{{ "name" : "web", "events" : ["pull_request"], "config" : {{ "url" : "{0}", "content_type" : "json" }} }}""",  publicUri), [| new KeyValuePair<_, _>("Authorization", "token " + token) |])
            GITHUB_API.At("repos", owner, repo, "hooks").Post(createHook)

        member this.CreateWebHooks repos (publicUri : string) =
            repos
            |> Seq.filter (fun repo -> not(this.WebHookExist repo publicUri))
            |> Seq.iter (fun repo ->
                try
                    this.CreateWebHook repo publicUri |> ignore
                with
                | ex -> raise(new Exception(String.Format("Repo '{0}' failed to initialize ({1})", repo, ex))))
            
        member this.GetOpenPullRequests (repo : string) =
            logger.DebugFormat("Getting the open pull requests for repo '{0}'", repo)
            let auth = Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token) |])
            JsonValue.Parse(GITHUB_API.At("repos", owner, repo, "pulls").Get(auth).ToText()).AsArray()

        member this.ProcessPullRequests pollAction prs =
            prs |> Seq.iter (fun pr -> pollAction(new XUri(pr?url.AsString())))

        member this.ProcessRepos (repos : string[]) pollAction =
            repos
            |> Array.map (fun repo->
                try
                    logger.DebugFormat("Processing repo '{0}'", repo)
                    this.GetOpenPullRequests repo
                with
                | ex -> JsonValue.Parse("[]").AsArray())
            |> Array.concat
            |> Array.sortBy (fun pr -> pr?created_at.AsDateTime())
            |> this.ProcessPullRequests pollAction

        member this.QueueMergePullRequest (prUri : XUri) action =
            let msg = String.Format("Will queue '{0}' for mergeability polling", prUri)
            logger.DebugFormat(msg)
            action prUri
            DreamMessage.Ok(MimeType.JSON, "Queue for mergeability polling"B)

        member this.ProcessPullRequestType mergeQueueAction prEventType =
            match prEventType with
            | Invalid i -> this.ClosePullRequest i
            | UnknownMergeability uri -> this.QueueMergePullRequest uri mergeQueueAction
            | AutoMergeable uri -> this.MergePullRequest uri
            | Skip -> DreamMessage.Ok(MimeType.JSON, "Pull request needs to be handled by a human since is not targeting an open branch or the master branch"B)

        member this.GetPullRequestDetails (prUri : XUri) =
            Plug.New(prUri).Get(Json("", [| new KeyValuePair<_, _>("Authorization", "token " + token) |]))
      
