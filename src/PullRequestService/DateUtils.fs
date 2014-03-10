[<RequireQualifiedAccess>]
module MindTouch.DateUtils
open System
let DATE_PATTERN = "yyyyMMdd"

let getBranchDate (branchname : string) =
    DateTime.ParseExact(branchname.Substring(branchname.Length - DATE_PATTERN.Length), DATE_PATTERN, null)
