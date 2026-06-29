namespace BrokenRealm.Server

module Kernel =
    let private message key args = { Key = key; Args = args }

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

    let private resolvePlayerTarget (state: GameState) (actingCharacterId: CharacterId) (objectId: ObjectId option) =
        match objectId with
        | Some id ->
            match PlayerObjects.tryGet state id with
            | None -> Error $"Effect target is not a player object: {id}"
            | Some _ -> Ok id
        | None -> Ok actingCharacterId

    let private resolveMobileObject
        (state: GameState)
        (actingCharacterId: CharacterId)
        (objectId: ObjectId option)
        =
        let resolvedId =
            match objectId with
            | Some id -> id
            | None -> actingCharacterId

        match state.Objects |> Map.tryFind resolvedId with
        | None -> Error $"Unknown mobile object id: {resolvedId}"
        | Some gameObject when PlayerObjects.isPlayer gameObject -> Ok(PlayerObjects.get state resolvedId)
        | Some gameObject when WorldObjects.isPermanentThing gameObject ->
            if resolvedId = actingCharacterId then
                Ok gameObject
            else
                let actingPlayer = PlayerObjects.get state actingCharacterId
                let actingLocationId = PlayerObjects.locationId actingPlayer

                match gameObject.LocationId with
                | None -> Error $"Permanent object {resolvedId} is not in a container."
                | Some locationId when locationId <> actingLocationId ->
                    Error "That object is not here."
                | Some _ -> Ok gameObject
        | Some _ -> Error $"Object {resolvedId} cannot be moved."

    let private validateTransferItemRouting (state: GameState) actingCharacterId sourceId destinationId =
        let actingPlayer = PlayerObjects.get state actingCharacterId
        let actingLocationId = PlayerObjects.locationId actingPlayer

        let sourceContainerId =
            match sourceId with
            | Some id -> id
            | None -> actingCharacterId

        if not (state.Objects.ContainsKey sourceContainerId) then
            Error($"Unknown source container id: {sourceContainerId}")
        elif CarriedItems.isCarriedStack state.Objects[sourceContainerId] then
            Error "Source container cannot be a carried stack object."
        else
            if
                WorldObjects.isItemContainerId state sourceContainerId
                && destinationId = actingCharacterId
                && WorldObjects.isAccessibleItemContainer state actingCharacterId sourceContainerId
            then
                Ok()
            elif
                sourceContainerId = actingCharacterId
                && WorldObjects.isItemContainerId state destinationId
                && WorldObjects.isAccessibleItemContainer state actingCharacterId destinationId
            then
                Ok()
            else
                match PlayerObjects.tryGet state destinationId with
                | Some destinationPlayer when sourceContainerId = actingCharacterId ->
                    if destinationPlayer.Id = actingCharacterId then
                        Error "You cannot give an item to yourself."
                    elif PlayerObjects.locationId destinationPlayer <> actingLocationId then
                        Error "That player is not here."
                    else
                        Ok()
                | Some destinationPlayer when sourceContainerId = actingLocationId && destinationPlayer.Id = actingCharacterId ->
                    Ok()
                | Some _ -> Error "Invalid item transfer."
                | None ->
                    if sourceContainerId <> actingCharacterId then
                        Error "Invalid item transfer."
                    elif destinationId <> actingLocationId then
                        Error "Invalid drop destination."
                    else
                        Ok()

    let rec private validateEffect (state: GameState) actingCharacterId targetId effect =
        match effect with
        | AddInventory(objectId, itemId, amount) when not (state.ItemIds.Contains itemId) ->
            Error("Unknown item id: " + itemId)
        | AddInventory(objectId, _, amount) when amount <= 0 || amount > 100 ->
            Error "addInventory effects require an amount from 1 to 100."
        | AddInventory(objectId, _, _) ->
            match resolvePlayerTarget state actingCharacterId objectId with
            | Error error -> Error error
            | Ok _ -> Ok()
        | RemoveInventory(objectId, itemId, amount) when not (state.ItemIds.Contains itemId) ->
            Error("Unknown item id: " + itemId)
        | RemoveInventory(objectId, _, amount) when amount <= 0 || amount > 100 ->
            Error "removeInventory effects require an amount from 1 to 100."
        | RemoveInventory(objectId, itemId, amount) ->
            match resolvePlayerTarget state actingCharacterId objectId with
            | Error error -> Error error
            | Ok playerId ->
                match CarriedItems.inventoryMap state playerId |> Map.tryFind itemId with
                | Some available when available >= amount -> Ok()
                | Some available -> Error $"You are only carrying {available} {itemId}."
                | None -> Error $"You are not carrying any {itemId}."
        | CreateObject(locationId, nameKey, descriptionKey, behaviorModuleId, behaviorClassName, tags, _, properties) ->
            if System.String.IsNullOrWhiteSpace nameKey then
                Error "createObject effects require a nameKey."
            elif List.isEmpty tags then
                Error "createObject effects require at least one tag."
            else
                match WorldObjects.isValidPlacementLocation state locationId with
                | Error error -> Error error
                | Ok() ->
                    match state.BehaviorModules |> Map.tryFind behaviorModuleId with
                    | None -> Error $"Unknown behavior module id: {behaviorModuleId}"
                    | Some behaviorModule when not (behaviorModule.Classes.ContainsKey behaviorClassName) ->
                        Error $"Unknown behavior class name: {behaviorClassName}"
                    | Some _ ->
                        match descriptionKey with
                        | Some key when System.String.IsNullOrWhiteSpace key ->
                            Error "createObject descriptionKey must not be empty."
                        | _ ->
                            properties
                            |> Map.toList
                            |> List.tryPick (fun (name, value) ->
                                match validateValueReferences state name value with
                                | Error error -> Some error
                                | Ok() -> None)
                            |> Option.map Error
                            |> Option.defaultValue (Ok())
        | GrowRoomExit(direction, reverseDirection, nameKey, descriptionKey, behaviorModuleId, behaviorClassName, tags, _, properties) ->
            if System.String.IsNullOrWhiteSpace nameKey then
                Error "growRoomExit effects require a nameKey."
            elif List.isEmpty tags then
                Error "growRoomExit effects require at least one tag."
            elif not (WorldObjects.isValidDirection direction) then
                Error $"Invalid exit direction: {direction}"
            elif not (WorldObjects.isValidDirection reverseDirection) then
                Error $"Invalid reverse exit direction: {reverseDirection}"
            else
                match state.Objects |> Map.tryFind targetId with
                | None -> Error $"Unknown source room id: {targetId}"
                | Some source when not (WorldObjects.isRoom source) ->
                    Error "growRoomExit requires a room object as the source."
                | Some source when Map.containsKey direction source.References ->
                    Error $"Room {targetId} already has an exit to the {direction}."
                | Some _ ->
                    match state.BehaviorModules |> Map.tryFind behaviorModuleId with
                    | None -> Error $"Unknown behavior module id: {behaviorModuleId}"
                    | Some behaviorModule when not (behaviorModule.Classes.ContainsKey behaviorClassName) ->
                        Error $"Unknown behavior class name: {behaviorClassName}"
                    | Some _ ->
                        match descriptionKey with
                        | Some key when System.String.IsNullOrWhiteSpace key ->
                            Error "growRoomExit descriptionKey must not be empty."
                        | _ ->
                            properties
                            |> Map.toList
                            |> List.tryPick (fun (name, value) ->
                                match validateValueReferences state name value with
                                | Error error -> Some error
                                | Ok() -> None)
                            |> Option.map Error
                            |> Option.defaultValue (Ok())
        | DestroyObject objectId ->
            let resolvedId = objectId |> Option.defaultValue targetId

            match WorldObjects.destroy state resolvedId with
            | Error error -> Error error
            | Ok _ -> Ok()
        | MoveObject(objectId, destinationId) when not (state.Objects.ContainsKey destinationId) ->
            Error("Unknown destination object id: " + destinationId)
        | MoveObject(objectId, destinationId) ->
            match resolveMobileObject state actingCharacterId objectId with
            | Error error -> Error error
            | Ok gameObject ->
                if PlayerObjects.isPlayer gameObject then
                    Ok()
                else
                    WorldObjects.isValidPlacementLocation state destinationId
        | TransferItem(sourceId, itemId, _, destinationId) when not (state.ItemIds.Contains itemId) ->
            Error("Unknown item id: " + itemId)
        | TransferItem(sourceId, _, amount, _) when amount <= 0 || amount > 100 ->
            Error "transferItem effects require an amount from 1 to 100."
        | TransferItem(sourceId, _, amount, destinationId) when not (state.Objects.ContainsKey destinationId) ->
            Error("Unknown destination object id: " + destinationId)
        | TransferItem(sourceId, _, amount, destinationId) ->
            match
                if WorldObjects.isItemContainerId state destinationId then
                    WorldObjects.validateContainerCapacity state destinationId amount
                else
                    Ok()
            with
            | Error error -> Error error
            | Ok() -> validateTransferItemRouting state actingCharacterId sourceId destinationId
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

    let private validateObjectBehaviorReference (state: GameState) (gameObject: GameObject) =
        match state.BehaviorModules |> Map.tryFind gameObject.BehaviorModuleId with
        | None -> Error $"Object {gameObject.Id} references unknown behavior module: {gameObject.BehaviorModuleId}"
        | Some behaviorModule when not (behaviorModule.Classes.ContainsKey gameObject.BehaviorClassName) ->
            Error $"Object {gameObject.Id} references unknown behavior class: {gameObject.BehaviorClassName}"
        | Some _ -> Ok()

    let validateGameState (state: GameState) =
        match validateContainment state with
        | Error error -> Error error
        | Ok() ->
            state.Objects
            |> Map.toList
            |> List.map snd
            |> List.tryPick (fun gameObject ->
                match validateObjectBehaviorReference state gameObject with
                | Error error -> Some error
                | Ok() ->
                    match validateObjectProperties state gameObject with
                    | Error error -> Some error
                    | Ok() ->
                        match PlayerObjects.validatePlayerObject state gameObject with
                        | Error error -> Some error
                        | Ok() ->
                            match CarriedItems.validateCarriedStack state gameObject with
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
        match validateEffect state characterId targetId effect with
        | Error error -> Error error
        | Ok() ->
            match effect with
            | AddInventory(objectId, itemId, amount) ->
                match resolvePlayerTarget state characterId objectId with
                | Error error -> Error error
                | Ok playerId ->
                    Ok(CarriedItems.addInventory state playerId itemId amount, messages, remainingInvocations)
            | RemoveInventory(objectId, itemId, amount) ->
                match resolvePlayerTarget state characterId objectId with
                | Error error -> Error error
                | Ok playerId ->
                    CarriedItems.removeQuantity state playerId itemId amount
                    |> Result.map (fun updatedState -> updatedState, messages, remainingInvocations)
            | CreateObject(locationId, nameKey, descriptionKey, behaviorModuleId, behaviorClassName, tags, aliases, properties) ->
                let _, updatedState =
                    WorldObjects.createPermanent
                        state
                        locationId
                        nameKey
                        descriptionKey
                        behaviorModuleId
                        behaviorClassName
                        tags
                        aliases
                        properties

                Ok(updatedState, messages, remainingInvocations)
            | GrowRoomExit(direction, reverseDirection, nameKey, descriptionKey, behaviorModuleId, behaviorClassName, tags, aliases, properties) ->
                WorldObjects.growRoomExit
                    state
                    targetId
                    direction
                    reverseDirection
                    nameKey
                    descriptionKey
                    behaviorModuleId
                    behaviorClassName
                    tags
                    aliases
                    properties
                |> Result.map (fun (_, updatedState) -> updatedState, messages, remainingInvocations)
            | DestroyObject objectId ->
                let resolvedId = objectId |> Option.defaultValue targetId

                WorldObjects.destroy state resolvedId
                |> Result.map (fun updatedState -> updatedState, messages, remainingInvocations)
            | MoveObject(objectId, destinationId) ->
                match resolveMobileObject state characterId objectId with
                | Error error -> Error error
                | Ok gameObject ->
                    if PlayerObjects.isPlayer gameObject then
                        let updated = PlayerObjects.withLocation gameObject destinationId

                        let movedState =
                            { state with
                                Objects = Map.add gameObject.Id updated state.Objects }

                        Ok(PlayerObjects.recordRoomVisit movedState gameObject.Id destinationId, messages, remainingInvocations)
                    else
                        WorldObjects.movePermanent state gameObject.Id destinationId
                        |> Result.map (fun updatedState -> updatedState, messages, remainingInvocations)
            | TransferItem(sourceId, itemId, amount, destinationId) ->
                let sourceContainerId =
                    match sourceId with
                    | Some id -> id
                    | None -> characterId

                CarriedItems.transferItem state sourceContainerId itemId amount destinationId
                |> Result.map (fun updatedState -> updatedState, messages, remainingInvocations)
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
                                value.BehaviorClassName
                                methodName
                                storagePath
                                value
                                args
                                state
                                (PlayerObjects.get state characterId)
                                behaviorModule.CompiledSource
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

    let private applyTickEffects characterId targetId effects state =
        applyEffects characterId targetId 0 effects state [] maxAnonymousInvocations
        |> Result.map (fun (state, _, _) -> state)

    let private connectedPlayersInRoom (state: GameState) (roomId: ObjectId) (isCharacterConnected: CharacterId -> bool) =
        contentsOf state roomId
        |> List.filter (fun gameObject ->
            PlayerObjects.isPlayer gameObject
            && not (PlayerObjects.isInLimbo gameObject)
            && isCharacterConnected gameObject.Id)

    let private tickTarget
        (state: GameState)
        (target: GameObject)
        (roomId: ObjectId)
        (tickIndex: int)
        (tickSeconds: int)
        (isCharacterConnected: CharacterId -> bool)
        =
        match state.BehaviorModules |> Map.tryFind target.BehaviorModuleId with
        | None -> Ok state
        | Some behaviorModule ->
            let connectedPlayers = connectedPlayersInRoom state roomId isCharacterConnected

            match
                Scripting.executeBehaviorTick
                    target.BehaviorClassName
                    state
                    target
                    roomId
                    tickIndex
                    tickSeconds
                    connectedPlayers
                    behaviorModule.CompiledSource
            with
            | Error _ -> Ok state
            | Ok effects -> applyTickEffects target.Id target.Id effects state

    let tickWorld (state: GameState) (tickIndex: int) (tickSeconds: int) (isCharacterConnected: CharacterId -> bool) =
        let rooms =
            state.Objects
            |> Map.toList
            |> List.map snd
            |> List.filter WorldObjects.isRoom
            |> List.sortBy _.Id

        rooms
        |> List.fold
            (fun result room ->
                result
                |> Result.bind (fun current ->
                    let roomId = room.Id

                    tickTarget current current.Objects[roomId] roomId tickIndex tickSeconds isCharacterConnected
                    |> Result.bind (fun afterRoom ->
                        contentsOf afterRoom roomId
                        |> List.filter WorldObjects.shouldTickCreature
                        |> List.sortBy _.Id
                        |> List.fold
                            (fun creatureResult creature ->
                                creatureResult
                                |> Result.bind (fun creatureState ->
                                    tickTarget
                                        creatureState
                                        creature
                                        roomId
                                        tickIndex
                                        tickSeconds
                                        isCharacterConnected))
                            (Ok afterRoom))))
            (Ok state)

    let private disambiguationMessages (state: GameState) culture (ambiguous: CommandMatching.AmbiguousCommandMatch) =
        let options =
            ambiguous.Candidates
            |> List.mapi (fun index objectId ->
                $"{index + 1}) {Localizer.displayObjectName state culture objectId}")
            |> String.concat "\n"

        [ message "disambiguation.prompt" (Map.ofList [ "options", options ]) ]

    let private pendingFromAmbiguous characterId (ambiguous: CommandMatching.AmbiguousCommandMatch) =
        Some
            { CharacterId = characterId
              ObjectId = ambiguous.ObjectId
              BehaviorModuleId = ambiguous.BehaviorModuleId
              BehaviorClassName = ambiguous.BehaviorClassName
              MethodName = ambiguous.MethodName
              CompiledSource = ambiguous.CompiledSource
              ResolvedArgs = ambiguous.ResolvedArgs
              Placeholder = ambiguous.Placeholder
              Candidates = ambiguous.Candidates }

    let private executeMatchedCommand characterId (matched: MatchedBehaviorMethod) (state: GameState) =
        let target = state.Objects[matched.ObjectId]

        let contents =
            contentsOf state target.Id
            |> List.filter (fun object -> object.Id <> characterId)

        let execution =
            match validateObjectProperties state target with
            | Error error -> Error error
            | Ok() ->
                Scripting.executeBehaviorMethodWithContents
                    matched.BehaviorClassName
                    matched.MethodName
                    state
                    target
                    contents
                    matched.Args
                    (PlayerObjects.get state characterId)
                    matched.CompiledSource

        match execution with
        | Error error ->
            { State = state
              Messages = [ message "script.error" (Map.ofList [ "error", error ]) ]
              PendingDisambiguation = None }
        | Ok effects ->
            match applyEffects characterId target.Id 0 effects state [] maxAnonymousInvocations with
            | Error "container_capacity_exceeded" ->
                { State = state
                  Messages = [ message "container.capacity.full" Map.empty ]
                  PendingDisambiguation = None }
            | Error error ->
                { State = state
                  Messages = [ message "script.error" (Map.ofList [ "error", error ]) ]
                  PendingDisambiguation = None }
            | Ok(state, messages, _) ->
                { State = state
                  Messages = messages
                  PendingDisambiguation = None }

    let private submitValidCommand characterId culture text (state: GameState) =
        match CommandMatching.tryMatchForCharacter characterId culture text state with
        | CommandMatching.NoMatch ->
            { State = state
              Messages = [ message "command.unknown" Map.empty ]
              PendingDisambiguation = None }
        | CommandMatching.Ambiguous ambiguous ->
            { State = state
              Messages = disambiguationMessages state culture ambiguous
              PendingDisambiguation = pendingFromAmbiguous characterId ambiguous }
        | CommandMatching.Matched matched ->
            executeMatchedCommand characterId matched state
        | CommandMatching.MatchedSequence matches ->
            matches
            |> List.fold
                (fun acc matched ->
                    let result = executeMatchedCommand characterId matched acc.State

                    { State = result.State
                      Messages = acc.Messages @ result.Messages
                      PendingDisambiguation = None })
                { State = state
                  Messages = []
                  PendingDisambiguation = None }

    let private tryExecutePendingSelection characterId culture selection (pending: PendingDisambiguation) (state: GameState) =
        if pending.CharacterId <> characterId then
            { State = state
              Messages = [ message "disambiguation.invalid" Map.empty ]
              PendingDisambiguation = None }
        elif selection < 1 || selection > pending.Candidates.Length then
            { State = state
              Messages = [ message "disambiguation.invalid" Map.empty ]
              PendingDisambiguation = Some pending }
        else
            let selected = pending.Candidates[selection - 1]

            let matched =
                { ObjectId = pending.ObjectId
                  BehaviorModuleId = pending.BehaviorModuleId
                  BehaviorClassName = pending.BehaviorClassName
                  MethodName = pending.MethodName
                  CompiledSource = pending.CompiledSource
                  Args = Map.add pending.Placeholder selected pending.ResolvedArgs }

            executeMatchedCommand characterId matched state

    let tryEnterPlayForCharacter characterId (state: GameState) =
        match Limbo.tryEnterPlay characterId state with
        | Error error ->
            Error error
        | Ok(state, messages, _) ->
            Ok { State = state; Messages = messages }

    let submitCommandForCharacterWithPending characterId culture text (state: GameState) (pending: PendingDisambiguation option) =
        match PlayerObjects.tryGet state characterId with
        | None ->
            { State = state
              Messages = [ message "script.error" (Map.ofList [ "error", $"Unknown character id: {characterId}" ]) ]
              PendingDisambiguation = None }
        | Some player when Limbo.isInLimbo player ->
            { State = state
              Messages = [ message "limbo.not_in_play" Map.empty ]
              PendingDisambiguation = None }
        | Some player ->
            let state =
                match player.LocationId with
                | Some locationId -> PlayerObjects.recordRoomVisit state characterId locationId
                | None -> state

            match validateContainment state with
            | Error error ->
                { State = state
                  Messages = [ message "script.error" (Map.ofList [ "error", error ]) ]
                  PendingDisambiguation = None }
            | Ok() ->
                match pending with
                | Some value ->
                    match CommandMatching.tryParseSelection text with
                    | Some selection -> tryExecutePendingSelection characterId culture selection value state
                    | None -> submitValidCommand characterId culture text state
                | None -> submitValidCommand characterId culture text state

    let submitCommandForCharacter characterId culture text (state: GameState) =
        submitCommandForCharacterWithPending characterId culture text state None

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
                state
                (PlayerObjects.get state characterId)
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
                        value.BehaviorClassName
                        methodName
                        storagePath
                        value
                        args
                        state
                        (PlayerObjects.get state characterId)
                        behaviorModule.CompiledSource
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

    let listAdminBehaviorModules (state: GameState) (snapshot: GameSnapshot) (serverRoot: string option) =
        let graphReferences = BehaviorGraph.collectBehaviorGraphReferences snapshot

        state.BehaviorModules
        |> Map.toList
        |> List.map (fun (_, behaviorModule) ->
            let snapshotModule =
                snapshot.World.BehaviorModules
                |> Map.find behaviorModule.Id

            let seedDrift =
                match serverRoot with
                | Some root -> BehaviorGraph.computeSeedDrift root snapshotModule
                | None ->
                    ({ SeedHashChanged = false
                       SyncedSeedHash = snapshotModule.SyncedSeedHash
                       CurrentSeedHash = "" }
                     : BehaviorGraph.SeedDriftInfo)

            let moduleWarnings =
                graphReferences
                |> Set.filter (fun (moduleId, _) -> moduleId = behaviorModule.Id)
                |> Set.toList
                |> List.choose (fun (moduleId, className) ->
                    if behaviorModule.Classes.ContainsKey className then
                        None
                    else
                        Some $"Missing class '{className}' in module '{moduleId}'.")

            { moduleId = behaviorModule.Id
              dependencies = behaviorModule.Dependencies
              classes = behaviorModule.Classes |> Map.toList |> List.map fst
              provenance =
                  match snapshotModule.Provenance with
                  | SeedSynced -> "seedSynced"
                  | AdminEdited -> "adminEdited"
              seedDrift =
                  { seedHashChanged = seedDrift.SeedHashChanged
                    syncedSeedHash = seedDrift.SyncedSeedHash
                    currentSeedHash = seedDrift.CurrentSeedHash }
              graphWarnings = moduleWarnings })

    let recompileBehaviorModules
        (compile: string -> Result<string, CompilerDiagnostic list>)
        (inspect: string -> string -> Result<Map<string, BehaviorClassDefinition>, CompilerDiagnostic>)
        (seedBehaviorModules: Map<string, BehaviorModule>)
        : Result<Map<string, BehaviorModule>, CompilerDiagnostic list> =
        match tryTopologicalOrder seedBehaviorModules with
        | Error error -> Error [ graphError error ]
        | Ok order ->
            order
            |> List.fold
                (fun (result: Result<Map<string, BehaviorModule>, CompilerDiagnostic list>) (moduleId: string) ->
                    result
                    |> Result.bind (fun (activeBehaviorModules: Map<string, BehaviorModule>) ->
                        let moduleDefinition = activeBehaviorModules.[moduleId]
                        let closure = dependencyClosure activeBehaviorModules moduleId

                        let compilationUnit =
                            order
                            |> List.filter (fun id -> Set.contains id closure)
                            |> List.map (fun id -> id, activeBehaviorModules.[id].Source)
                            |> BehaviorSources.joinModules

                        match compile compilationUnit with
                        | Error diagnostics -> Error diagnostics
                        | Ok compiledSource ->
                            match inspect moduleDefinition.RegistryName compiledSource with
                            | Error diagnostic ->
                                Error [ if diagnostic.file = "" then { diagnostic with file = moduleId } else diagnostic ]
                            | Ok classes ->
                                let updatedModule =
                                    { moduleDefinition with
                                        CompiledSource = compiledSource
                                        Classes = classes }

                                Ok(activeBehaviorModules |> Map.add moduleId updatedModule)))
                (Ok seedBehaviorModules)

    let activateBehaviorGraph = recompileBehaviorModules

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

    let tryRegisterAccount accountId password displayName (state: GameState) =
        match Auth.validateAccountId accountId with
        | Error error -> Error error
        | Ok validatedAccountId ->
            match Auth.validatePassword password with
            | Error error -> Error error
            | Ok _ ->
                if state.Accounts.ContainsKey validatedAccountId then
                    Error "An account with that id already exists."
                else
                    let account: AccountState =
                        { Id = validatedAccountId
                          DisplayName = displayName
                          PasswordHash = Some(Auth.hashPassword password) }

                    let characterId = ObjectIds.create()

                    let player =
                        PlayerObjects.create characterId "traveler" "object.traveler.name" validatedAccountId "forest"

                    let updated =
                        { state with
                            Accounts = Map.add validatedAccountId account state.Accounts
                            Objects = Map.add characterId player state.Objects }

                    match validateGameState updated with
                    | Error error -> Error error
                    | Ok() -> Ok updated
