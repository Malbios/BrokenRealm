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
            match readString "itemId" effect, readInt "amount" effect with
            | Some itemId, Some amount when amount > 0 && amount <= 100 -> Ok(AddInventory(itemId, amount))
            | _ -> Error "addInventory effects require itemId and an amount from 1 to 100."
        | Some "movePlayer" ->
            match readString "destinationId" effect with
            | Some destinationId -> Ok(MovePlayer destinationId)
            | None -> Error "movePlayer effects require a destinationId."
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

    let private executeWithinLimits limits invocation (target: GameObject) (contents: GameObject list) (args: Map<string, string>) (actorInventory: Map<ItemId, Quantity>) (source: string) =
        try
            let context =
                let properties = Dictionary<string, obj>()
                target.Properties |> Map.iter (fun key value -> properties[key] <- gameValueToObject value)

                {| args = args
                   this =
                    {| id = target.Id
                       name = target.Name
                       descriptionKey = target.DescriptionKey |> Option.defaultValue ""
                       tags = target.Tags |> Set.toArray
                       properties = properties
                       references = target.References
                       contents =
                        contents
                        |> List.map (fun object ->
                            {| id = object.Id
                               name = object.Name
                               descriptionKey = object.DescriptionKey |> Option.defaultValue ""
                               tags = object.Tags |> Set.toArray |})
                        |> List.toArray |}
                   actor = {| inventory = actorInventory |} |}

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

    let executeVerbWithLimits limits target args actorInventory (source: string) =
        if source.Length > limits.MaxSourceCharacters then
            Error $"Script source may contain at most {limits.MaxSourceCharacters} characters."
        else
            executeWithinLimits limits (fun context -> "execute(" + context + ")") target [] args actorInventory source

    let executeVerb target args actorInventory source =
        executeVerbWithLimits defaultLimits target args actorInventory source

    let private identifierPattern = Regex("^[A-Za-z_$][A-Za-z0-9_$]*$", RegexOptions.CultureInvariant)

    let executeBehaviorMethodWithLimits limits (className: string) (methodName: string) target args actorInventory (source: string) =
        if not (identifierPattern.IsMatch className) || not (identifierPattern.IsMatch methodName) then
            Error "Behavior class and method names must be valid JavaScript identifiers."
        elif source.Length > limits.MaxSourceCharacters then
            Error $"Behavior source may contain at most {limits.MaxSourceCharacters} characters."
        else
            let invocation context = $"(new {className}()).{methodName}({context})"
            executeWithinLimits limits invocation target [] args actorInventory source

    let executeBehaviorMethod className methodName target args actorInventory source =
        executeBehaviorMethodWithLimits defaultLimits className methodName target args actorInventory source

    let executeBehaviorMethodWithContents (className: string) (methodName: string) target contents args actorInventory (source: string) =
        if not (identifierPattern.IsMatch className) || not (identifierPattern.IsMatch methodName) then
            Error "Behavior class and method names must be valid JavaScript identifiers."
        elif source.Length > defaultLimits.MaxSourceCharacters then
            Error $"Behavior source may contain at most {defaultLimits.MaxSourceCharacters} characters."
        else
            let invocation context = $"(new {className}()).{methodName}({context})"
            executeWithinLimits defaultLimits invocation target contents args actorInventory source

    let inspectBehaviorModule (registryName: string) (source: string) =
        let diagnostic message = { message = message; line = 0; column = 0 }

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
