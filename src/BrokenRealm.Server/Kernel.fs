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
        | MovePlayer destinationId when not (state.Objects.ContainsKey destinationId) ->
            Error("Unknown destination object id: " + destinationId)
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
            | MovePlayer destinationId ->
                let player = { state.Player with LocationId = destinationId }
                Ok({ state with Player = player }, messages)
            | EmitMessage message -> Ok(state, messages @ [ message ])

    let submitCommand culture text (state: GameState) =
        match CommandMatching.tryMatch culture text state with
        | None ->
            { State = state
              Messages = [ message "command.unknown" Map.empty ] }
        | Some matched ->
            let target = state.Objects[matched.ObjectId]

            match
                Scripting.executeBehaviorMethod
                    matched.BehaviorClassName
                    matched.MethodName
                    target
                    matched.Args
                    state.Player.Inventory
                    matched.CompiledSource
            with
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

    let tryGetBehaviorModule moduleId (state: GameState) =
        state.BehaviorModules |> Map.tryFind moduleId

    let listAdminBehaviorModules (state: GameState) =
        state.BehaviorModules
        |> Map.toList
        |> List.map (fun (_, behaviorModule) ->
            { moduleId = behaviorModule.Id
              classes = behaviorModule.Classes |> Map.toList |> List.map fst })

    let tryUpdateBehaviorModule compile (inspect: string -> Result<Map<string, BehaviorClassDefinition>, CompilerDiagnostic>) moduleId source (state: GameState) =
        match state.BehaviorModules |> Map.tryFind moduleId with
        | None -> Ok None
        | Some behaviorModule ->
            match compile source with
            | Error diagnostics -> Error diagnostics
            | Ok compiledSource ->
                match inspect compiledSource with
                | Error diagnostic -> Error [ diagnostic ]
                | Ok classes ->
                    let missingClass =
                        state.Objects
                        |> Map.toList
                        |> List.map snd
                        |> List.filter (fun object -> object.BehaviorModuleId = moduleId)
                        |> List.tryFind (fun object -> not (classes.ContainsKey object.BehaviorClassName))

                    match missingClass with
                    | Some object ->
                        Error
                            [ { message = $"Behavior module is missing class {object.BehaviorClassName}, used by object {object.Id}."
                                line = 0
                                column = 0 } ]
                    | None ->
                        let updatedModule =
                            { behaviorModule with
                                Source = source
                                CompiledSource = compiledSource
                                Classes = classes }

                        Ok(
                            Some
                                { state with
                                    BehaviorModules = state.BehaviorModules |> Map.add moduleId updatedModule })
