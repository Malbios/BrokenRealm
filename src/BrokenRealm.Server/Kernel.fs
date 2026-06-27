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

    let rec private validateValueReferences (state: GameState) path value =
        match value with
        | ObjectReferenceValue objectId when not (state.Objects.ContainsKey objectId) ->
            Error $"Property {path} references unknown object id: {objectId}"
        | ObjectReferenceValue _ -> Ok()
        | ListValue values ->
            values
            |> List.mapi (fun index value -> validateValueReferences state $"{path}[{index}]" value)
            |> List.tryPick (function Error error -> Some error | Ok() -> None)
            |> Option.map Error
            |> Option.defaultValue (Ok())
        | MapValue values ->
            values
            |> Map.toList
            |> List.tryPick (fun (key, value) ->
                match validateValueReferences state $"{path}.{key}" value with
                | Error error -> Some error
                | Ok() -> None)
            |> Option.map Error
            |> Option.defaultValue (Ok())
        | _ -> Ok()

    let private validateObjectProperties state (target: GameObject) =
        target.Properties
        |> Map.toList
        |> List.tryPick (fun (name, value) ->
            match validateValueReferences state name value with
            | Error error -> Some error
            | Ok() -> None)
        |> Option.map Error
        |> Option.defaultValue (Ok())

    let contentsOf (state: GameState) containerId =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter (fun object -> object.LocationId = Some containerId)
        |> List.sortBy _.Id

    let validateContainment (state: GameState) =
        let rec validateChain visiting objectId =
            if Set.contains objectId visiting then
                Error $"Containment cycle detected at object id: {objectId}"
            else
                match state.Objects |> Map.tryFind objectId with
                | None -> Error $"Unknown contained object id: {objectId}"
                | Some object ->
                    match object.LocationId with
                    | None -> Ok()
                    | Some locationId when locationId = object.Id ->
                        Error $"Object cannot contain itself: {object.Id}"
                    | Some locationId when not (state.Objects.ContainsKey locationId) ->
                        Error $"Object {object.Id} has unknown location id: {locationId}"
                    | Some locationId -> validateChain (Set.add objectId visiting) locationId

        state.Objects
        |> Map.toList
        |> List.map fst
        |> List.tryPick (fun objectId ->
            match validateChain Set.empty objectId with
            | Error error -> Some error
            | Ok() -> None)
        |> Option.map Error
        |> Option.defaultValue (Ok())

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

    let private submitValidCommand culture text (state: GameState) =
        match CommandMatching.tryMatch culture text state with
        | None ->
            { State = state
              Messages = [ message "command.unknown" Map.empty ] }
        | Some matched ->
            let target = state.Objects[matched.ObjectId]
            let contents = contentsOf state target.Id

            let execution =
                match validateObjectProperties state target with
                | Error error -> Error error
                | Ok() ->
                    Scripting.executeBehaviorMethodWithContents
                        matched.BehaviorClassName
                        matched.MethodName
                        target
                        contents
                        matched.Args
                        state.Player.Inventory
                        matched.CompiledSource

            match execution with
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

    let submitCommand culture text (state: GameState) =
        match validateContainment state with
        | Error error ->
            { State = state
              Messages = [ message "script.error" (Map.ofList [ "error", error ]) ] }
        | Ok() -> submitValidCommand culture text state

    let tryGetBehaviorModule moduleId (state: GameState) =
        state.BehaviorModules |> Map.tryFind moduleId

    let private graphError message =
        { message = message; line = 0; column = 0 }

    let private tryTopologicalOrder (modules: Map<string, BehaviorModule>) =
        let rec visit moduleId visiting visited order =
            if Set.contains moduleId visiting then
                Error $"Behavior module dependency cycle detected at {moduleId}."
            elif Set.contains moduleId visited then
                Ok(visited, order)
            else
                match modules |> Map.tryFind moduleId with
                | None -> Error $"Missing behavior module dependency: {moduleId}."
                | Some behaviorModule ->
                    let visiting = Set.add moduleId visiting

                    behaviorModule.Dependencies
                    |> List.fold
                        (fun result dependencyId ->
                            result
                            |> Result.bind (fun (visited, order) ->
                                visit dependencyId visiting visited order))
                        (Ok(visited, order))
                    |> Result.map (fun (visited, order) ->
                        Set.add moduleId visited, order @ [ moduleId ])

        modules
        |> Map.toList
        |> List.map fst
        |> List.fold
            (fun result moduleId ->
                result
                |> Result.bind (fun (visited, order) ->
                    visit moduleId Set.empty visited order))
            (Ok(Set.empty, []))
        |> Result.map snd

    let private dependencyClosure (modules: Map<string, BehaviorModule>) (moduleId: string) : Set<string> =
        let rec collect collected currentId =
            if Set.contains currentId collected then
                collected
            else
                match modules |> Map.tryFind currentId with
                | None -> Set.add currentId collected
                | Some behaviorModule ->
                    behaviorModule.Dependencies
                    |> List.fold collect (Set.add currentId collected)

        collect Set.empty moduleId

    let behaviorImpact moduleId (state: GameState) =
        let affectedModules =
            state.BehaviorModules
            |> Map.toList
            |> List.map fst
            |> List.filter (fun candidateId ->
                dependencyClosure state.BehaviorModules candidateId
                |> Set.contains moduleId)
            |> List.sort

        let affectedObjects =
            state.Objects
            |> Map.toList
            |> List.map snd
            |> List.filter (fun object -> affectedModules |> List.contains object.BehaviorModuleId)
            |> List.map _.Id
            |> List.sort

        affectedModules, affectedObjects

    let listAdminBehaviorModules (state: GameState) =
        state.BehaviorModules
        |> Map.toList
        |> List.map (fun (_, behaviorModule) ->
            { moduleId = behaviorModule.Id
              dependencies = behaviorModule.Dependencies
              classes = behaviorModule.Classes |> Map.toList |> List.map fst })

    let tryUpdateBehaviorModule
        (compile: string -> Result<string, CompilerDiagnostic list>)
        (inspect: string -> string -> Result<Map<string, BehaviorClassDefinition>, CompilerDiagnostic>)
        moduleId
        source
        (state: GameState)
        =
        match state.BehaviorModules |> Map.tryFind moduleId with
        | None -> Ok None
        | Some behaviorModule ->
            let candidateModules =
                state.BehaviorModules
                |> Map.add moduleId { behaviorModule with Source = source }

            match tryTopologicalOrder candidateModules with
            | Error error -> Error [ graphError error ]
            | Ok order ->
                let affectedSet =
                    candidateModules
                    |> Map.toList
                    |> List.map fst
                    |> List.filter (fun candidateId ->
                        dependencyClosure candidateModules candidateId
                        |> Set.contains moduleId)
                    |> Set.ofList

                let affectedModules = order |> List.filter (fun id -> Set.contains id affectedSet)

                let compilationResult: Result<Map<string, BehaviorModule>, CompilerDiagnostic list> =
                    affectedModules
                    |> List.fold
                        (fun result affectedId ->
                            result
                            |> Result.bind (fun (modules: Map<string, BehaviorModule>) ->
                                let affectedModule = modules[affectedId]
                                let closure = dependencyClosure modules affectedId

                                let compilationUnit =
                                    order
                                    |> List.filter (fun id -> Set.contains id closure)
                                    |> List.map (fun id -> modules[id].Source)
                                    |> BehaviorSources.join

                                match compile compilationUnit with
                                | Error diagnostics -> Error diagnostics
                                | Ok compiledSource ->
                                    match inspect affectedModule.RegistryName compiledSource with
                                    | Error diagnostic -> Error [ diagnostic ]
                                    | Ok classes ->
                                        Ok(
                                            modules
                                            |> Map.add
                                                affectedId
                                                { affectedModule with
                                                    CompiledSource = compiledSource
                                                    Classes = classes })))
                        (Ok candidateModules)

                match compilationResult with
                | Error diagnostics -> Error diagnostics
                | Ok updatedModules ->
                    let missingClass =
                        state.Objects
                        |> Map.toList
                        |> List.map snd
                        |> List.tryFind (fun object ->
                            match updatedModules |> Map.tryFind object.BehaviorModuleId with
                            | None -> true
                            | Some objectModule -> not (objectModule.Classes.ContainsKey object.BehaviorClassName))

                    match missingClass with
                    | Some object ->
                        Error
                            [ graphError $"Behavior module is missing class {object.BehaviorClassName}, used by object {object.Id}." ]
                    | None ->
                        let affectedObjects =
                            state.Objects
                            |> Map.toList
                            |> List.map snd
                            |> List.filter (fun object -> Set.contains object.BehaviorModuleId affectedSet)
                            |> List.map _.Id
                            |> List.sort

                        Ok(
                            Some
                                { State = { state with BehaviorModules = updatedModules }
                                  AffectedModules = affectedModules
                                  AffectedObjects = affectedObjects })
