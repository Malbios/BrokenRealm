module BrokenRealm.Client.Main

open Blazored.LocalStorage
open Elmish
open Bolero
open Bolero.Html
open Microsoft.AspNetCore.Components
open Radzen
open Radzen.Blazor

type Model =
    {
        x: string
    }

let initModel =
    {
        x = ""
    }

type Message =
    | Ping
    | Pong of bool
    | Error of exn

let update (localStorage: ILocalStorageService) message model =
    
    match message with
    | Ping ->
        let contains key =
            localStorage.ContainKeyAsync(key)
            |> _.AsTask()
            |> Async.AwaitTask
            
        let cmd =
            Cmd.OfAsync.either
                contains
                "key"
                Pong
                Error

        model, cmd
        
    | Pong doesContain ->
        { model with x = $"x: {doesContain}" }, Cmd.none
        
    | Error ex ->
        failwith ex.Message

let view model _ = concat {
    comp<RadzenComponents> {}
    
    comp<RadzenTheme> {
        "Theme" => "Dark"
    }
    
    comp<RadzenStack> {
        "Orientation" => Orientation.Horizontal
        
        comp<RadzenButton> { "a" }
        comp<RadzenButton> { "b" }
    }
    
    p { model.x }
}

type MyApp() =
    inherit ProgramComponent<Model, Message>()
    
    [<Inject>]
    member val LocalStorage : ILocalStorageService = Unchecked.defaultof<_> with get, set

    override this.Program =
    
        let update = update this.LocalStorage
    
        Program.mkProgram (fun _ -> initModel, Cmd.none) update view
