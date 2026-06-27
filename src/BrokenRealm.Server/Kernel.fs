namespace BrokenRealm.Server

module Kernel =
    let private message key args = { Key = key; Args = args }

    let private addInventory itemId amount inventory =
        let current = inventory |> Map.tryFind itemId |> Option.defaultValue 0
        inventory |> Map.add itemId (current + amount)

    let private validateEffect (state: GameState) effect =
        match effect with
        | AddInventory(itemId, amount) when not (state.ItemIds.Contains itemId) ->
            Error("Unknown item id: " + itemId)
        | AddInventory(_, amount) when amount <= 0 || amount > 100 ->
            Error "addInventory effects require an amount from 1 to 100."
        | _ -> Ok()

    let private applyEffect (state: GameState) (messages: Message list) effect =
        match validateEffect state effect with
        | Error error -> Error error
        | Ok() ->
            match effect with
            | AddInventory(itemId, amount) ->
                let inventory = state.Player.Inventory |> addInventory itemId amount
                let player = { state.Player with Inventory = inventory }
                Ok({ state with Player = player }, messages)
            | EmitMessage message -> Ok(state, messages @ [ message ])

    let submitCommand culture text (state: GameState) =
        match CommandMatching.tryMatch culture text state with
        | None ->
            { State = state
              Messages = [ message "command.unknown" Map.empty ] }
        | Some matched ->
            let target = state.Objects[matched.ObjectId]

            match Scripting.executeVerb target matched.Args state.Player.Inventory matched.Verb.CompiledSource with
            | Error error ->
                { State = state
                  Messages = [ message "script.error" (Map.ofList [ "error", error ]) ] }
            | Ok effects ->
                match
                    effects
                    |> List.fold
                        (fun result effect ->
                            match result with
                            | Error error -> Error error
                            | Ok(state, messages) -> applyEffect state messages effect)
                        (Ok(state, []))
                with
                | Error error ->
                    { State = state
                      Messages = [ message "script.error" (Map.ofList [ "error", error ]) ] }
                | Ok(state, messages) ->
                    { State = state
                      Messages = messages }

    let tryGetVerb objectId verbName (state: GameState) =
        state.Objects
        |> Map.tryFind objectId
        |> Option.bind (fun object -> object.Verbs |> Map.tryFind verbName)

    let tryUpdateVerbSource compile objectId verbName source (state: GameState) =
        match state.Objects |> Map.tryFind objectId with
        | None -> Ok None
        | Some object ->
            match object.Verbs |> Map.tryFind verbName with
            | None -> Ok None
            | Some verb ->
                match compile source with
                | Error diagnostics -> Error diagnostics
                | Ok compiledSource ->
                    let updatedVerb =
                        { verb with
                            Source = source
                            CompiledSource = compiledSource }

                    let updatedObject = { object with Verbs = object.Verbs |> Map.add verbName updatedVerb }
                    Ok(Some { state with Objects = state.Objects |> Map.add objectId updatedObject })
