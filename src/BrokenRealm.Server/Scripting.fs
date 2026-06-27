namespace BrokenRealm.Server

open System
open System.Text.Json
open Jint

module Scripting =
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

    let private readArgs (element: JsonElement) =
        match element.TryGetProperty("args") with
        | true, args -> jsonToStringMap args
        | _ -> Map.empty

    let private decodeEffect (effect: JsonElement) =
        match readString "type" effect with
        | Some "addInventory" ->
            match readString "itemId" effect, readInt "amount" effect with
            | Some itemId, Some amount when amount > 0 && amount <= 100 -> Ok(AddInventory(itemId, amount))
            | _ -> Error "addInventory effects require itemId and an amount from 1 to 100."
        | Some "message" ->
            match readString "key" effect with
            | Some key -> Ok(EmitMessage(message key (readArgs effect)))
            | None -> Error "message effects require a key."
        | Some effectType -> Error("Unknown script effect type: " + effectType)
        | None -> Error "Script effects require a type."

    let executeVerb (target: GameObject) (args: Map<string, string>) (actorInventory: Map<ItemId, Quantity>) (source: string) =
        try
            let context =
                {| args = args
                   this =
                    {| id = target.Id
                       name = target.Name
                       descriptionKey = target.DescriptionKey |> Option.defaultValue ""
                       tags = target.Tags |> Set.toArray
                       properties = target.Properties |}
                   actor = {| inventory = actorInventory |} |}

            let contextJson = JsonSerializer.Serialize(context, jsonOptions)
            let script = source + "\nJSON.stringify(execute(" + contextJson + "));"
            let json =
                (new Engine(fun options ->
                    options.LimitMemory(4_000_000L).TimeoutInterval(TimeSpan.FromMilliseconds(250.0)) |> ignore))
                    .Evaluate(script)
                    .AsString()

            use document = JsonDocument.Parse(json)
            let root = document.RootElement

            match root.TryGetProperty("effects") with
            | false, _ -> Error "Script must return an object with an effects array."
            | true, effects when effects.ValueKind <> JsonValueKind.Array -> Error "Script effects must be an array."
            | true, effects ->
                effects.EnumerateArray()
                |> Seq.map decodeEffect
                |> Seq.fold
                    (fun result effect ->
                        match result, effect with
                        | Error error, _ -> Error error
                        | _, Error error -> Error error
                        | Ok values, Ok value -> Ok(values @ [ value ]))
                    (Ok [])
        with ex ->
            Error ex.Message
