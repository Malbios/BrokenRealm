namespace BrokenRealm.Client

open Blazored.LocalStorage
open Bolero.Html
open BrokenRealm.Client
open BrokenRealm.Client.Update
open Elmish
open Bolero
open Microsoft.AspNetCore.Components
open Radzen.Blazor

type App() =
    inherit ProgramComponent<Model, Message>()
    
    let view model dispatch = div {
        comp<RadzenComponents> { attr.empty() }
        comp<RadzenNotification> { attr.empty() }
        comp<RadzenDialog> { attr.empty() }
        
        comp<RadzenTheme> {
            "Theme" => "Dark"
        }
        
        div {
            attr.``class`` "main-content"
            
            match model.Page with
            | Page.Home ->
                Pages.Main.render model dispatch
            | Page.TestPage ->
                Pages.Test.render model dispatch
            | Page.NotFound ->
                text "404 - Not Found"
        }
    }
        
    let update localStorage message model =
        match model.Page with
        | Page.Home -> updateMain localStorage message model
        | Page.NotFound -> model, Cmd.none
        | Page.TestPage -> model, Cmd.none
    
    [<Inject>]
    member val LocalStorage : ILocalStorageService = Unchecked.defaultof<_> with get, set
    
    override this.CssScope = CssScopes.BrokenRealm

    override this.Program =
        
        let initialState _ = Model.defaults, Cmd.ofMsg Message.Init

        let update = update this.LocalStorage
        
        let router = Router.infer Message.SetPage _.Page |> Router.withNotFound Page.NotFound
        
        Program.mkProgram initialState update view
        |> Program.withRouter router
