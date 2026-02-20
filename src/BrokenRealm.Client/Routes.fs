namespace BrokenRealm.Client

open Bolero

[<RequireQualifiedAccess>]
type Page =
    | [<EndPoint "/">] Home
    | [<EndPoint "/test">] TestPage
    | [<EndPoint "/not-found">] NotFound
