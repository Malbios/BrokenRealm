namespace BrokenRealm.Server

module Kernel =
    let private message key args = { Key = key; Args = args }

    let private addInventory itemId amount inventory =
        let current = inventory |> Map.tryFind itemId |> Option.defaultValue 0
        inventory |> Map.add itemId (current + amount)

    let rec private tryReplaceNestedValue path replacement current =
        match path, current with
        | [], _ -> Ok replacement
        | PropertySegment key :: rest, MapValue values ->
            match values |> Map.tryFind key with
            | None -> Error $"replaceValue path does not contain property: {key}"
            | Some nested ->
                tryReplaceNestedValue rest replacement nested
                |> Result.map (fun updated -> MapValue(Map.add key updated values))
        | PropertySegment key :: rest, AnonymousValue anonymous ->
            match anonymous.Properties |> Map.tryFind key with
            | None -> Error $"replaceValue path does not contain anonymous property: {key}"
            | Some nested ->
                tryReplaceNestedValue rest replacement nested
                |> Result.map (fun updated ->
                    AnonymousValue { anonymous with Properties = Map.add key updated anonymous.Properties })
        | IndexSegment index :: rest, ListValue values when index < List.length values ->
            tryReplaceNestedValue rest replacement values[index]
            |> Result.map (fun updated -> ListValue(values |> List.updateAt index updated))
        | IndexSegment index :: _, ListValue _ -> Error $"replaceValue path index is out of range: {index}"
        | PropertySegment key :: _, _ -> Error $"replaceValue cannot traverse property segment: {key}"
        | IndexSegment index :: _, _ -> Error $"replaceValue cannot traverse index segment: {index}"

    let private tryReplaceObjectValue path replacement (target: GameObject) =
        match path with
        | PropertySegment propertyName :: rest ->
            match target.Properties |> Map.tryFind propertyName with
            | None -> Error $"replaceValue path does not contain object property: {propertyName}"
            | Some current ->
                tryReplaceNestedValue rest replacement current
                |> Result.map (fun updated -> { target with Properties = Map.add propertyName updated target.Properties })
        | IndexSegment _ :: _ -> Error "replaceValue paths must start with an object property name."
        | [] -> Error "replaceValue paths must not be empty."

    let rec private validateEffect (state: GameState) targetId effect =
        match effect with
        | AddInventory(itemId, amount) when not (state.ItemIds.Contains itemId) ->
            Error("Unknown item id: " + itemId)
        | AddInventory(_, amount) when amount <= 0 || amount > 100 ->
            Error "addInventory effects require an amount from 1 to 100."
        | MovePlayer destinationId when not (state.Objects.ContainsKey destinationId) ->
            Error("Unknown destination object id: " + destinationId)
        | ReplaceValue(path, replacement) ->
            match validateValueReferences state "replacement" replacement with
            | Error error -> Error error
            | Ok() ->
                match state.Objects |> Map.tryFind targetId with
                | None -> Error $"Unknown replaceValue target object id: {targetId}"
                | Some target -> tryReplaceObjectValue path replacement target |> Result.map ignore
        | _ -> Ok()

    and private validateValueReferences (state: GameState) path value =
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
        | AnonymousValue anonymous ->
            match state.BehaviorModules |> Map.tryFind anonymous.BehaviorModuleId with
            | None -> Error $"Anonymous value {path} references unknown behavior module: {anonymous.BehaviorModuleId}"
            | Some behaviorModule when not (behaviorModule.Classes.ContainsKey anonymous.BehaviorClassName) ->
                Error $"Anonymous value {path} references unknown behavior class: {anonymous.BehaviorClassName}"
            | Some _ ->
                anonymous.Properties
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

    let rec private valueUsesBehaviorModule moduleIds value =
        match value with
        | AnonymousValue anonymous ->
            Set.contains anonymous.BehaviorModuleId moduleIds
            || (anonymous.Properties |> Map.exists (fun _ nested -> valueUsesBehaviorModule moduleIds nested))
        | ListValue values -> values |> List.exists (valueUsesBehaviorModule moduleIds)
        | MapValue values -> values |> Map.exists (fun _ nested -> valueUsesBehaviorModule moduleIds nested)
        | _ -> false

    let private objectUsesBehaviorModules moduleIds (object: GameObject) =
        Set.contains object.BehaviorModuleId moduleIds
        || (object.Properties |> Map.exists (fun _ value -> valueUsesBehaviorModule moduleIds value))

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

    let rec private tryGetNestedValue path current =
        match path, current with
        | [], value -> Ok value
        | PropertySegment key :: rest, MapValue values ->
            match values |> Map.tryFind key with
            | Some value -> tryGetNestedValue rest value
            | None -> Error $"Value path does not contain property: {key}"
        | PropertySegment key :: rest, AnonymousValue anonymous ->
            match anonymous.Properties |> Map.tryFind key with
            | Some value -> tryGetNestedValue rest value
            | None -> Error $"Value path does not contain anonymous property: {key}"
        | IndexSegment index :: rest, ListValue values when index < List.length values ->
            tryGetNestedValue rest values[index]
        | IndexSegment index :: _, ListValue _ -> Error $"Value path index is out of range: {index}"
        | _ -> Error "Value path cannot traverse the selected value."

    let private tryGetObjectValue path (target: GameObject) =
        match path with
        | PropertySegment propertyName :: rest ->
            match target.Properties |> Map.tryFind propertyName with
            | Some value -> tryGetNestedValue rest value
            | None -> Error $"Value path does not contain object property: {propertyName}"
        | _ -> Error "Value paths must start with an object property name."

    let private maxAnonymousInvocationDepth = 8
    let private maxAnonymousInvocations = 16

    let rec private applyEffect characterId targetId depth (state: GameState) (messages: Message list) remainingInvocations effect =
        match validateEffect state targetId effect with
        | Error error -> Error error
        | Ok() ->
            match effect with
            | AddInventory(itemId, amount) ->
                let character = state.Characters[characterId]
                let updated = { character with Inventory = character.Inventory |> addInventory itemId amount }
                Ok({ state with Characters = Map.add characterId updated state.Characters }, messages, remainingInvocations)
            | MovePlayer destinationId ->
                let character = state.Characters[characterId]
                let updated = { character with LocationId = destinationId }
                Ok({ state with Characters = Map.add characterId updated state.Characters }, messages, remainingInvocations)
            | ReplaceValue(path, replacement) ->
                tryReplaceObjectValue path replacement state.Objects[targetId]
                |> Result.map (fun target ->
                    { state with Objects = Map.add targetId target state.Objects }, messages, remainingInvocations)
            | InvokeAnonymous(path, methodName, args) ->
                if depth >= maxAnonymousInvocationDepth then
                    Error $"Anonymous behavior invocation depth may not exceed {maxAnonymousInvocationDepth}."
                elif remainingInvocations <= 0 then
                    Error $"Effect batches may invoke at most {maxAnonymousInvocations} anonymous behaviors."
                else
                    match tryGetObjectValue path state.Objects[targetId] with
                    | Error error -> Error error
                    | Ok(AnonymousValue value) ->
                        match validateValueReferences state "anonymous" (AnonymousValue value) with
                        | Error error -> Error error
                        | Ok() ->
                            let behaviorModule = state.BehaviorModules[value.BehaviorModuleId]
                            let storagePath =
                                path
                                |> List.map (function PropertySegment name -> name :> obj | IndexSegment index -> index :> obj)
                                |> List.toArray

                            Scripting.executeAnonymousBehaviorMethodAtPath
                                value.BehaviorClassName methodName storagePath value args state.Characters[characterId].Inventory behaviorModule.CompiledSource
                            |> Result.bind (fun effects ->
                                applyEffects characterId targetId (depth + 1) effects state messages (remainingInvocations - 1))
                    | Ok _ -> Error "invokeAnonymous path does not select an anonymous behavior value."
            | EmitMessage message -> Ok(state, messages @ [ message ], remainingInvocations)

    and private applyEffects characterId targetId depth effects state messages remainingInvocations =
        effects
        |> List.fold
            (fun result effect ->
                match result with
                | Error error -> Error error
                | Ok(state, messages, remaining) -> applyEffect characterId targetId depth state messages remaining effect)
            (Ok(state, messages, remainingInvocations))

    let private submitValidCommand characterId culture text (state: GameState) =
        match CommandMatching.tryMatchForCharacter characterId culture text state with
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
                        state.Characters[characterId].Inventory
                        matched.CompiledSource

            match execution with
            | Error error ->
                { State = state
                  Messages = [ message "script.error" (Map.ofList [ "error", error ]) ] }
            | Ok effects ->
                match applyEffects characterId target.Id 0 effects state [] maxAnonymousInvocations with
                | Error error ->
                    { State = state
                      Messages = [ message "script.error" (Map.ofList [ "error", error ]) ] }
                | Ok(state, messages, _) ->
                    { State = state
                      Messages = messages }

    let submitCommandForCharacter characterId culture text (state: GameState) =
        match state.Characters |> Map.tryFind characterId with
        | None ->
            { State = state
              Messages = [ message "script.error" (Map.ofList [ "error", $"Unknown character id: {characterId}" ]) ] }
        | Some _ ->
            match validateContainment state with
            | Error error ->
                { State = state
                  Messages = [ message "script.error" (Map.ofList [ "error", error ]) ] }
            | Ok() -> submitValidCommand characterId culture text state

    let submitCommand culture text state =
        submitCommandForCharacter GameSnapshots.PrototypeCharacterId culture text state

    let executeAnonymousValueMethodForCharacter characterId methodName args (value: AnonymousBehaviorValue) (state: GameState) =
        match validateValueReferences state "anonymous" (AnonymousValue value) with
        | Error error -> Error error
        | Ok() ->
            let behaviorModule = state.BehaviorModules[value.BehaviorModuleId]
            Scripting.executeAnonymousBehaviorMethod
                value.BehaviorClassName
                methodName
                value
                args
                state.Characters[characterId].Inventory
                behaviorModule.CompiledSource

    let executeAnonymousValueMethod methodName args value state =
        executeAnonymousValueMethodForCharacter GameSnapshots.PrototypeCharacterId methodName args value state

    let invokeStoredAnonymousValueMethodForCharacter characterId ownerId path methodName args (state: GameState) =
        match state.Objects |> Map.tryFind ownerId with
        | None -> Error $"Unknown anonymous value owner object id: {ownerId}"
        | Some owner ->
            match tryGetObjectValue path owner with
            | Error error -> Error error
            | Ok(AnonymousValue value) ->
                match validateValueReferences state "anonymous" (AnonymousValue value) with
                | Error error -> Error error
                | Ok() ->
                    let behaviorModule = state.BehaviorModules[value.BehaviorModuleId]
                    let storagePath =
                        path
                        |> List.map (function PropertySegment name -> name :> obj | IndexSegment index -> index :> obj)
                        |> List.toArray

                    Scripting.executeAnonymousBehaviorMethodAtPath
                        value.BehaviorClassName methodName storagePath value args state.Characters[characterId].Inventory behaviorModule.CompiledSource
                    |> Result.bind (fun effects -> applyEffects characterId ownerId 0 effects state [] maxAnonymousInvocations)
                    |> Result.map (fun (state, messages, _) -> { State = state; Messages = messages })
            | Ok _ -> Error "Value path does not select an anonymous behavior value."

    let invokeStoredAnonymousValueMethod ownerId path methodName args state =
        invokeStoredAnonymousValueMethodForCharacter GameSnapshots.PrototypeCharacterId ownerId path methodName args state

    let tryGetBehaviorModule moduleId (state: GameState) =
        state.BehaviorModules |> Map.tryFind moduleId

    let private graphError message =
        { message = message; file = ""; line = 0; column = 0 }

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
            |> List.filter (objectUsesBehaviorModules (Set.ofList affectedModules))
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
                                    |> List.map (fun id -> id, modules[id].Source)
                                    |> BehaviorSources.joinModules

                                match compile compilationUnit with
                                | Error diagnostics -> Error diagnostics
                                | Ok compiledSource ->
                                    match inspect affectedModule.RegistryName compiledSource with
                                    | Error diagnostic ->
                                        Error [ if diagnostic.file = "" then { diagnostic with file = affectedId } else diagnostic ]
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
                        let updatedState = { state with BehaviorModules = updatedModules }
                        let invalidProperties =
                            updatedState.Objects
                            |> Map.toList
                            |> List.map snd
                            |> List.tryPick (fun object ->
                                match validateObjectProperties updatedState object with
                                | Error error -> Some error
                                | Ok() -> None)

                        match invalidProperties with
                        | Some error -> Error [ graphError error ]
                        | None ->
                        let affectedObjects =
                            state.Objects
                            |> Map.toList
                            |> List.map snd
                            |> List.filter (objectUsesBehaviorModules affectedSet)
                            |> List.map _.Id
                            |> List.sort

                        Ok(
                            Some
                                { State = updatedState
                                  AffectedModules = affectedModules
                                  AffectedObjects = affectedObjects })

    let tryValidateBehaviorModule compile inspect moduleId source state =
        tryUpdateBehaviorModule compile inspect moduleId source state
        |> Result.map (Option.map (fun update ->
            { AffectedModules = update.AffectedModules
              AffectedObjects = update.AffectedObjects }))
