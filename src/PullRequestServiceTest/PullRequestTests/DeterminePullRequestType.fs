module PullRequestTests.DeterminePullRequestType

open NUnit.Framework
open FsUnit
open FSharp.Data
open FSharp.Data.Json
open FSharp.Data.Json.Extensions

[<TestFixture>]
type ``Given an invalid pull request`` () =
    let invalidPR = JsonValue.Object([("base", JsonValue.Object(["ref", JsonValue.String("master")] |> Map.ofSeq) )] |> Map.ofSeq)
    
    // [<Test>] member x.
    // ``when I ask whether it is invalid it answers true`` () =
    // MindTouch.PullRequest.DeterminePullRequestType (fun x -> false) (fun y -> true) (fun z -> ["MT-12345"]) invalidPR 
    // |> should be MindTouch.PullRequest.Invalid (uri, commentsUri)