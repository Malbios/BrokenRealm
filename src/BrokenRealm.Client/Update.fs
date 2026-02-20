namespace BrokenRealm.Client

open Elmish
open Blazored.LocalStorage

module Update =
    
    let updateMain (localStorage: ILocalStorageService) message model =
        
        let key = "test_key"
        
        match message with
        | Message.Init ->
            model, Cmd.ofMsg Message.Retrieve
            
        | Message.Error ex ->
            failwith ex.Message
        
        | Message.SetPage page ->
            { model with Page = page }, Cmd.none
        
        | Message.Retrieve ->
            let cmd =
                Cmd.OfAsync.either
                    (Persistence.tryGet localStorage) key
                    Message.RetrieveDone
                    Message.Error

            model, cmd
        
        | Message.RetrieveDone value ->
            let value = value |> Option.defaultValue "[not found]"
            
            { model with TestStorage = $"TestStorage: {value}" }, Cmd.none
            
        | Message.Store value ->
            let cmd =
                Cmd.OfAsync.either
                    (fun () -> Persistence.set localStorage key value) ()
                    (fun () -> Message.StoreDone)
                    Message.Error
                    
            model, cmd
            
        | Message.StoreDone ->
            model, Cmd.ofMsg Message.Retrieve

        | Message.ClearStore ->
            let cmd =
                Cmd.OfAsync.either
                    (fun () -> Persistence.clear localStorage key) ()
                    (fun () -> Message.Retrieve)
                    Message.Error
            
            model, cmd
