namespace BrokenRealm.Client

type Model = {
    Page: Page
    TestStorage: string
}

[<RequireQualifiedAccess>]
module Model =
    
    let defaults = {
        Page = Page.Home
        TestStorage = ""
    }
    
[<RequireQualifiedAccess>]
type Message =
    | Init
    | Error of exn
    | SetPage of Page
    | Retrieve
    | RetrieveDone of string option
    | Store of string
    | StoreDone
    | ClearStore
