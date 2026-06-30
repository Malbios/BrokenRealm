namespace BrokenRealm.Server

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.RegularExpressions
open Jint

module Scripting =
    type ExecutionLimits =
        { MemoryBytes: int64
          Timeout: TimeSpan
          MaxSourceCharacters: int
          MaxEffects: int
          MaxMessages: int
          MaxMessageArguments: int
          MaxMessageArgumentCharacters: int }

    let defaultLimits =
        { MemoryBytes = 4_000_000L
          Timeout = TimeSpan.FromMilliseconds(250.0)
          MaxSourceCharacters = 64_000
          MaxEffects = 32
          MaxMessages = 16
          MaxMessageArguments = 16
          MaxMessageArgumentCharacters = 1_024 }

    let private message key args = { Key = key; Args = args }

    let private jsonOptions =
        JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let private readString (propertyName: string) (element: JsonElement) =
        match element.TryGetProperty(propertyName) with
        | true, value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
        | _ -> None

    let private readInt (propertyName: string) (element: JsonElement) =
        match element.TryGetProperty(propertyName) with
        | true, value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt32() with
            | true, amount -> Some amount
            | _ -> None
        | _ -> None

    let rec private decodeGameValue (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Null -> Ok NullValue
        | JsonValueKind.String -> Ok(StringValue(element.GetString()))
        | JsonValueKind.True -> Ok(BooleanValue true)
        | JsonValueKind.False -> Ok(BooleanValue false)
        | JsonValueKind.Number ->
            match element.TryGetInt64() with
            | true, value -> Ok(IntegerValue value)
            | _ -> Ok(FloatValue(element.GetDouble()))
        | JsonValueKind.Array ->
            element.EnumerateArray()
            |> Seq.fold
                (fun result item ->
                    result
                    |> Result.bind (fun values -> decodeGameValue item |> Result.map (fun value -> values @ [ value ])))
                (Ok [])
            |> Result.map ListValue
        | JsonValueKind.Object ->
            element.EnumerateObject()
            |> Seq.fold
                (fun result property ->
                    result
                    |> Result.bind (fun values ->
                        decodeGameValue property.Value
                        |> Result.map (fun value -> Map.add property.Name value values)))
                (Ok Map.empty)
            |> Result.map MapValue
        | _ -> Error "replaceValue values must contain only supported game values."

    let private decodeValuePath (element: JsonElement) =
        match element.TryGetProperty("path") with
        | false, _ -> Error "replaceValue effects require a path."
        | true, path when path.ValueKind <> JsonValueKind.Array -> Error "replaceValue paths must be arrays."
        | true, path when path.GetArrayLength() = 0 || path.GetArrayLength() > 16 ->
            Error "replaceValue paths must contain 1 to 16 segments."
        | true, path ->
            path.EnumerateArray()
            |> Seq.map (fun segment ->
                match segment.ValueKind with
                | JsonValueKind.String when not (String.IsNullOrWhiteSpace(segment.GetString())) ->
                    Ok(PropertySegment(segment.GetString()))
                | JsonValueKind.Number ->
                    match segment.TryGetInt32() with
                    | true, index when index >= 0 -> Ok(IndexSegment index)
                    | _ -> Error "replaceValue index segments must be non-negative integers."
                | _ -> Error "replaceValue path segments must be non-empty strings or non-negative integers.")
            |> Seq.fold
                (fun result segment ->
                    match result, segment with
                    | Ok segments, Ok value -> Ok(segments @ [ value ])
                    | Error error, _ -> Error error
                    | _, Error error -> Error error)
                (Ok [])

    let private decodeStringArguments (element: JsonElement) =
        match element.TryGetProperty("args") with
        | false, _ -> Ok Map.empty
        | true, args when args.ValueKind <> JsonValueKind.Object -> Error "invokeAnonymous args must be an object."
        | true, args ->
            args.EnumerateObject()
            |> Seq.fold
                (fun result property ->
                    match result, property.Value.ValueKind with
                    | Ok values, JsonValueKind.String -> Ok(Map.add property.Name (property.Value.GetString()) values)
                    | Error error, _ -> Error error
                    | _ -> Error "invokeAnonymous argument values must be strings.")
                (Ok Map.empty)

    let rec private jsonToStringMap (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Object ->
            element.EnumerateObject()
            |> Seq.map (fun property ->
                let value =
                    match property.Value.ValueKind with
                    | JsonValueKind.String -> property.Value.GetString()
                    | JsonValueKind.Number -> property.Value.GetRawText()
                    | JsonValueKind.True -> "true"
                    | JsonValueKind.False -> "false"
                    | JsonValueKind.Object ->
                        property.Value.EnumerateObject()
                        |> Seq.map (fun nested -> nested.Name + ":" + nested.Value.GetRawText())
                        |> String.concat ","
                    | _ -> property.Value.GetRawText()

                property.Name, value)
            |> Map.ofSeq
        | _ -> Map.empty

    let private readArgs limits (element: JsonElement) =
        match element.TryGetProperty("args") with
        | false, _ -> Ok Map.empty
        | true, args when args.ValueKind <> JsonValueKind.Object -> Error "Message arguments must be an object."
        | true, args ->
            let values = jsonToStringMap args

            if values.Count > limits.MaxMessageArguments then
                Error $"Message effects may contain at most {limits.MaxMessageArguments} arguments."
            elif values |> Map.exists (fun _ value -> value.Length > limits.MaxMessageArgumentCharacters) then
                Error $"Message argument values may contain at most {limits.MaxMessageArgumentCharacters} characters."
            else
                Ok values

    let private decodeEffect limits (effect: JsonElement) =
        match readString "type" effect with
        | Some "addInventory" ->
            let objectId = readString "objectId" effect

            match readString "itemId" effect, readInt "amount" effect with
            | Some itemId, Some amount when amount > 0 && amount <= 100 -> Ok(AddInventory(objectId, itemId, amount))
            | _ -> Error "addInventory effects require itemId and an amount from 1 to 100."
        | Some "removeInventory" ->
            let objectId = readString "objectId" effect

            match readString "itemId" effect, readInt "amount" effect with
            | Some itemId, Some amount when amount > 0 && amount <= 100 -> Ok(RemoveInventory(objectId, itemId, amount))
            | _ -> Error "removeInventory effects require itemId and an amount from 1 to 100."
        | Some "destroyObject" ->
            let objectId = readString "objectId" effect
            Ok(DestroyObject objectId)
        | Some "createObject" ->
            match
                readString "locationId" effect,
                readString "nameKey" effect,
                readString "behaviorModuleId" effect,
                readString "behaviorClassName" effect,
                readString "tags" effect
            with
            | Some locationId, Some nameKey, Some behaviorModuleId, Some behaviorClassName, Some tags ->
                let descriptionKey = readString "descriptionKey" effect
                let aliasesEn = readString "aliasesEn" effect |> Option.defaultValue ""
                let aliasesDe = readString "aliasesDe" effect |> Option.defaultValue ""

                let properties =
                    match effect.TryGetProperty("properties") with
                    | true, value when value.ValueKind = JsonValueKind.Object ->
                        value.EnumerateObject()
                        |> Seq.fold
                            (fun result property ->
                                result
                                |> Result.bind (fun values ->
                                    decodeGameValue property.Value
                                    |> Result.map (fun decoded -> Map.add property.Name decoded values)))
                            (Ok Map.empty)
                    | _ -> Ok Map.empty

                properties
                |> Result.map (fun decodedProperties ->
                    CreateObject(
                        locationId,
                        nameKey,
                        descriptionKey,
                        behaviorModuleId,
                        behaviorClassName,
                        WorldObjects.parseTags tags,
                        WorldObjects.parseAliases aliasesEn aliasesDe,
                        decodedProperties))
            | _ ->
                Error "createObject effects require locationId, nameKey, behaviorModuleId, behaviorClassName, and tags."
        | Some "growRoomExit" ->
            match
                readString "direction" effect,
                readString "reverseDirection" effect,
                readString "nameKey" effect,
                readString "behaviorModuleId" effect,
                readString "behaviorClassName" effect,
                readString "tags" effect
            with
            | Some direction, Some reverseDirection, Some nameKey, Some behaviorModuleId, Some behaviorClassName, Some tags ->
                let descriptionKey = readString "descriptionKey" effect
                let aliasesEn = readString "aliasesEn" effect |> Option.defaultValue ""
                let aliasesDe = readString "aliasesDe" effect |> Option.defaultValue ""

                let properties =
                    match effect.TryGetProperty("properties") with
                    | true, value when value.ValueKind = JsonValueKind.Object ->
                        value.EnumerateObject()
                        |> Seq.fold
                            (fun result property ->
                                result
                                |> Result.bind (fun values ->
                                    decodeGameValue property.Value
                                    |> Result.map (fun decoded -> Map.add property.Name decoded values)))
                            (Ok Map.empty)
                    | _ -> Ok Map.empty

                properties
                |> Result.map (fun decodedProperties ->
                    GrowRoomExit(
                        direction,
                        reverseDirection,
                        nameKey,
                        descriptionKey,
                        behaviorModuleId,
                        behaviorClassName,
                        WorldObjects.parseTags tags,
                        WorldObjects.parseAliases aliasesEn aliasesDe,
                        decodedProperties))
            | _ ->
                Error "growRoomExit effects require direction, reverseDirection, nameKey, behaviorModuleId, behaviorClassName, and tags."
        | Some "movePlayer" ->
            match readString "destinationId" effect with
            | Some destinationId -> Ok(MoveObject(None, destinationId))
            | None -> Error "movePlayer effects require a destinationId."
        | Some "moveObject" ->
            let objectId = readString "objectId" effect

            match readString "destinationId" effect with
            | Some destinationId -> Ok(MoveObject(objectId, destinationId))
            | None -> Error "moveObject effects require a destinationId."
        | Some "transferItem" ->
            let sourceId = readString "sourceId" effect

            match readString "itemId" effect, readInt "amount" effect, readString "destinationId" effect with
            | Some itemId, Some amount, Some destinationId when amount > 0 && amount <= 100 ->
                Ok(TransferItem(sourceId, itemId, amount, destinationId))
            | _ -> Error "transferItem effects require itemId, destinationId, and an amount from 1 to 100."
        | Some "replaceValue" ->
            match decodeValuePath effect, effect.TryGetProperty("value") with
            | Ok path, (true, value) -> decodeGameValue value |> Result.map (fun decoded -> ReplaceValue(path, decoded))
            | Error error, _ -> Error error
            | _, (false, _) -> Error "replaceValue effects require a value."
        | Some "invokeAnonymous" ->
            match decodeValuePath effect, readString "methodName" effect, decodeStringArguments effect with
            | Ok path, Some methodName, Ok args -> Ok(InvokeAnonymous(path, methodName, args))
            | Error error, _, _ -> Error error
            | _, None, _ -> Error "invokeAnonymous effects require a methodName."
            | _, _, Error error -> Error error
        | Some "message" ->
            match readString "key" effect, readArgs limits effect with
            | Some key, Ok args -> Ok(EmitMessage(message key args))
            | _, Error error -> Error error
            | None, _ -> Error "message effects require a key."
        | Some effectType -> Error("Unknown script effect type: " + effectType)
        | None -> Error "Script effects require a type."

    let private sanitizeException (ex: exn) =
        let typeName = ex.GetType().Name
        let detail = ex.Message.ToLowerInvariant()

        if typeName.Contains("Timeout", StringComparison.OrdinalIgnoreCase) || detail.Contains("timeout") then
            "Script execution timed out."
        elif typeName.Contains("Memory", StringComparison.OrdinalIgnoreCase) || detail.Contains("memory limit") then
            "Script exceeded its memory limit."
        else
            "Script execution failed."

    let rec private gameValueToObject value : obj =
        match value with
        | NullValue -> null
        | StringValue value -> value :> obj
        | IntegerValue value -> value :> obj
        | FloatValue value -> value :> obj
        | BooleanValue value -> value :> obj
        | ObjectReferenceValue objectId -> objectId :> obj
        | ListValue values -> values |> List.map gameValueToObject |> List.toArray :> obj
        | MapValue values ->
            let output = Dictionary<string, obj>()
            values |> Map.iter (fun key value -> output[key] <- gameValueToObject value)
            output :> obj
        | AnonymousValue anonymous ->
            let properties = Dictionary<string, obj>()
            anonymous.Properties |> Map.iter (fun key value -> properties[key] <- gameValueToObject value)
            properties :> obj

    let private executeContextWithinLimits limits invocation context (source: string) =
        try
            let contextJson = JsonSerializer.Serialize(context, jsonOptions)
            let script = source + "\nJSON.stringify(" + invocation contextJson + ");"
            let json =
                (new Engine(fun options ->
                    options.LimitMemory(limits.MemoryBytes).TimeoutInterval(limits.Timeout) |> ignore))
                    .Evaluate(script)
                    .AsString()

            use document = JsonDocument.Parse(json)
            let root = document.RootElement

            match root.TryGetProperty("effects") with
            | false, _ -> Error "Script must return an object with an effects array."
            | true, effects when effects.ValueKind <> JsonValueKind.Array -> Error "Script effects must be an array."
            | true, effects when effects.GetArrayLength() > limits.MaxEffects ->
                Error $"Scripts may return at most {limits.MaxEffects} effects."
            | true, effects ->
                effects.EnumerateArray()
                |> Seq.map (decodeEffect limits)
                |> Seq.fold
                    (fun result effect ->
                        match result, effect with
                        | Error error, _ -> Error error
                        | _, Error error -> Error error
                        | Ok values, Ok value -> Ok(values @ [ value ]))
                    (Ok [])
                |> Result.bind (fun values ->
                    let messageCount =
                        values
                        |> List.sumBy (function
                            | EmitMessage _ -> 1
                            | _ -> 0)

                    if messageCount > limits.MaxMessages then
                        Error $"Scripts may return at most {limits.MaxMessages} message effects."
                    else
                        Ok values)
        with ex ->
            Error(sanitizeException ex)

    let private objectProperties (gameObject: GameObject) =
        let properties = Dictionary<string, obj>()
        gameObject.Properties |> Map.iter (fun key value -> properties[key] <- gameValueToObject value)
        properties

    let private objectSummary (gameObject: GameObject) =
        {| id = gameObject.Id
           name = gameObject.Name
           descriptionKey = gameObject.DescriptionKey |> Option.defaultValue ""
           tags = gameObject.Tags |> Set.toArray
           properties = objectProperties gameObject |}

    let private containerStorageMap (state: GameState) (locationId: ObjectId) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter (fun gameObject ->
            gameObject.LocationId = Some locationId
            && WorldObjects.isItemContainer gameObject)
        |> List.map (fun container -> container.Id, CarriedItems.itemQuantitiesInContainer state container.Id)
        |> Map.ofList

    let private contentsSummariesForRoom (state: GameState) (roomId: ObjectId) (excludeId: ObjectId) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter (fun gameObject -> gameObject.LocationId = Some roomId && gameObject.Id <> excludeId)
        |> List.sortBy _.Id
        |> List.map objectSummary

    let private actorSummary (state: GameState) (actor: GameObject) (args: Map<string, string>) =
        let locationId = PlayerObjects.locationId actor
        let location = state.Objects[locationId]
        let locationContents = contentsSummariesForRoom state locationId actor.Id

        let destinationId, destinationContents =
            match args |> Map.tryFind "direction" with
            | Some direction ->
                match location.References |> Map.tryFind direction with
                | Some destId -> Some destId, contentsSummariesForRoom state destId actor.Id
                | None -> None, []
            | None -> None, []

        {| id = actor.Id
           name = actor.Name
           descriptionKey = actor.DescriptionKey |> Option.defaultValue ""
           tags = actor.Tags |> Set.toArray
           properties = objectProperties actor
           references = actor.References
           locationReferences = location.References
           inventory = PlayerObjects.inventory state actor.Id
           locationId = locationId
           locationContents = locationContents |> List.toArray
           destinationId = destinationId |> Option.defaultValue null
           destinationContents = destinationContents |> List.toArray
           floorItems = CarriedItems.itemQuantitiesInContainer state locationId
           containerStorage = containerStorageMap state locationId |}

    let private executeWithinLimits limits invocation (state: GameState) (target: GameObject) (contents: GameObject list) (args: Map<string, string>) (actor: GameObject) source =
        let context =
            {| args = args
               this =
                {| id = target.Id
                   name = target.Name
                   descriptionKey = target.DescriptionKey |> Option.defaultValue ""
                   tags = target.Tags |> Set.toArray
                   properties = objectProperties target
                   references = target.References
                   contents = contents |> List.map objectSummary |> List.toArray
                   storedItems = CarriedItems.itemQuantitiesInContainer state target.Id
                   containerStorage = containerStorageMap state target.Id |}
               actor = actorSummary state actor args |}

        executeContextWithinLimits limits invocation context source

    let executeVerbWithLimits limits state target args actor (source: string) =
        if source.Length > limits.MaxSourceCharacters then
            Error $"Script source may contain at most {limits.MaxSourceCharacters} characters."
        else
            executeWithinLimits limits (fun context -> "execute(" + context + ")") state target [] args actor source

    let executeVerb state target args actor source =
        executeVerbWithLimits defaultLimits state target args actor source

    let private identifierPattern = Regex("^[A-Za-z_$][A-Za-z0-9_$]*$", RegexOptions.CultureInvariant)

    let executeBehaviorMethodWithLimits limits (className: string) (methodName: string) state target args actor (source: string) =
        if not (identifierPattern.IsMatch className) || not (identifierPattern.IsMatch methodName) then
            Error "Behavior class and method names must be valid JavaScript identifiers."
        elif source.Length > limits.MaxSourceCharacters then
            Error $"Behavior source may contain at most {limits.MaxSourceCharacters} characters."
        else
            let invocation context = $"(new {className}()).{methodName}({context})"
            executeWithinLimits limits invocation state target [] args actor source

    let executeBehaviorMethod className methodName state target args actor source =
        executeBehaviorMethodWithLimits defaultLimits className methodName state target args actor source

    let executeBehaviorMethodWithContents (className: string) (methodName: string) (state: GameState) target contents args actor (source: string) =
        if not (identifierPattern.IsMatch className) || not (identifierPattern.IsMatch methodName) then
            Error "Behavior class and method names must be valid JavaScript identifiers."
        elif source.Length > defaultLimits.MaxSourceCharacters then
            Error $"Behavior source may contain at most {defaultLimits.MaxSourceCharacters} characters."
        else
            let invocation context = $"(new {className}()).{methodName}({context})"
            executeWithinLimits defaultLimits invocation state target contents args actor source

    let private roomContentsForTick (state: GameState) (locationId: ObjectId) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter (fun gameObject ->
            gameObject.LocationId = Some locationId && not (CarriedItems.isCarriedStack gameObject))
        |> List.sortBy _.Id

    let private buildTickRoomContext (state: GameState) (roomId: ObjectId) (connectedPlayers: GameObject list) =
        let room = state.Objects[roomId]
        let contents = roomContentsForTick state roomId

        {| id = room.Id
           properties = objectProperties room
           references = room.References
           contents = contents |> List.map objectSummary |> List.toArray
           floorItems = CarriedItems.itemQuantitiesInContainer state roomId
           containerStorage = containerStorageMap state roomId
           connectedPlayers = connectedPlayers |> List.map objectSummary |> List.toArray |}

    let executeBehaviorTick
        (className: string)
        (state: GameState)
        (target: GameObject)
        (roomId: ObjectId)
        (tickIndex: int)
        (tickSeconds: int)
        (connectedPlayers: GameObject list)
        (source: string)
        =
        if not (identifierPattern.IsMatch className) then
            Error "Behavior class names must be valid JavaScript identifiers."
        elif source.Length > defaultLimits.MaxSourceCharacters then
            Error $"Behavior source may contain at most {defaultLimits.MaxSourceCharacters} characters."
        else
            let roomContents =
                roomContentsForTick state roomId
                |> List.filter (fun gameObject -> gameObject.Id <> target.Id)

            let context =
                {| tick = {| index = tickIndex; seconds = tickSeconds |}
                   this =
                    {| id = target.Id
                       name = target.Name
                       descriptionKey = target.DescriptionKey |> Option.defaultValue ""
                       tags = target.Tags |> Set.toArray
                       properties = objectProperties target
                       references = target.References
                       contents = roomContents |> List.map objectSummary |> List.toArray
                       storedItems = CarriedItems.itemQuantitiesInContainer state target.Id
                       containerStorage = containerStorageMap state target.Id |}
                   room = buildTickRoomContext state roomId connectedPlayers |}

            let invocation contextJson = $"(new {className}()).tick({contextJson})"
            executeContextWithinLimits defaultLimits invocation context source

    let executeAnonymousBehaviorMethodAtPath (className: string) (methodName: string) storagePath (value: AnonymousBehaviorValue) args state actor (source: string) =
        if not (identifierPattern.IsMatch className) || not (identifierPattern.IsMatch methodName) then
            Error "Behavior class and method names must be valid JavaScript identifiers."
        elif source.Length > defaultLimits.MaxSourceCharacters then
            Error $"Behavior source may contain at most {defaultLimits.MaxSourceCharacters} characters."
        else
            let properties = Dictionary<string, obj>()
            value.Properties |> Map.iter (fun key property -> properties[key] <- gameValueToObject property)
            let context = {| args = args; this = {| storagePath = storagePath; properties = properties |}; actor = actorSummary state actor args |}
            let invocation contextJson = $"(new {className}()).{methodName}({contextJson})"
            executeContextWithinLimits defaultLimits invocation context source

    let executeAnonymousBehaviorMethod className methodName value args state actor source =
        executeAnonymousBehaviorMethodAtPath className methodName [||] value args state actor source

    let inspectBehaviorModule (registryName: string) (source: string) =
        let diagnostic message = { message = message; file = ""; line = 0; column = 0 }

        if not (identifierPattern.IsMatch registryName) then
            Error(diagnostic "Behavior registry names must be valid JavaScript identifiers.")
        elif source.Length > defaultLimits.MaxSourceCharacters then
            Error(diagnostic $"Behavior source may contain at most {defaultLimits.MaxSourceCharacters} characters.")
        else
            try
                let script =
                    source
                    + $"\nJSON.stringify(Object.fromEntries(Object.entries({registryName}).map(([name, behavior]) => [name, {{ commands: behavior.commands, methodsValid: Array.isArray(behavior.commands) && behavior.commands.every(command => typeof behavior.prototype[command.methodName] === 'function') }}])));"

                let json =
                    (new Engine(fun options ->
                        options.LimitMemory(defaultLimits.MemoryBytes).TimeoutInterval(defaultLimits.Timeout) |> ignore))
                        .Evaluate(script)
                        .AsString()

                use document = JsonDocument.Parse(json)

                if document.RootElement.ValueKind <> JsonValueKind.Object then
                    Error(diagnostic "Behavior modules must define a behaviorClasses registry object.")
                else
                    document.RootElement.EnumerateObject()
                    |> Seq.map (fun classProperty ->
                        if not (identifierPattern.IsMatch classProperty.Name) then
                            Error $"Invalid behavior class name: {classProperty.Name}."
                        else
                            let hasCommands, commandsElement = classProperty.Value.TryGetProperty("commands")
                            let hasMethodsValid, methodsValidElement = classProperty.Value.TryGetProperty("methodsValid")

                            if not hasCommands || commandsElement.ValueKind <> JsonValueKind.Array then
                                Error $"Behavior class {classProperty.Name} must define a commands array."
                            elif not hasMethodsValid || methodsValidElement.ValueKind <> JsonValueKind.True then
                                Error $"Behavior class {classProperty.Name} registers a command without a matching method."
                            else
                                let commands =
                                    commandsElement.EnumerateArray()
                                    |> Seq.map (fun command ->
                                        match readString "methodName" command, command.TryGetProperty("patterns") with
                                        | Some methodName, (true, patterns) when identifierPattern.IsMatch methodName && patterns.ValueKind = JsonValueKind.Array ->
                                            let decodedPatterns =
                                                patterns.EnumerateArray()
                                                |> Seq.map (fun pattern ->
                                                    match readString "culture" pattern, readString "pattern" pattern with
                                                    | Some culture, Some text
                                                        when (culture = "en" || culture = "de")
                                                             && not (String.IsNullOrWhiteSpace text) ->
                                                        Ok
                                                            { Culture = (if culture = "de" then De else En)
                                                              Pattern = text }
                                                    | _ -> Error $"Behavior command {methodName} has an invalid pattern.")
                                                |> Seq.toList

                                            match decodedPatterns |> List.tryPick (function Error error -> Some error | _ -> None) with
                                            | Some error -> Error error
                                            | None ->
                                                Ok
                                                    { MethodName = methodName
                                                      Patterns = decodedPatterns |> List.choose (function Ok value -> Some value | _ -> None) }
                                        | _ -> Error $"Behavior class {classProperty.Name} has an invalid command definition.")
                                    |> Seq.toList

                                match commands |> List.tryPick (function Error error -> Some error | _ -> None) with
                                | Some error -> Error error
                                | None ->
                                    Ok
                                        (classProperty.Name,
                                         { ClassName = classProperty.Name
                                           Commands = commands |> List.choose (function Ok value -> Some value | _ -> None) }))
                    |> Seq.toList
                    |> fun classes ->
                        match classes |> List.tryPick (function Error error -> Some error | _ -> None) with
                        | Some error -> Error(diagnostic error)
                        | None ->
                            classes
                            |> List.choose (function Ok value -> Some value | _ -> None)
                            |> Map.ofList
                            |> Ok
            with ex ->
                Error(diagnostic (sanitizeException ex))
