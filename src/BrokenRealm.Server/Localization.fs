namespace BrokenRealm.Server

module Cultures =
    let parse (value: string) =
        match value.Trim().ToLowerInvariant() with
        | "de" -> De
        | _ -> En

module Localizer =
    let itemAliases culture =
        match culture with
        | En -> Map.ofList [ "wood", "wood" ]
        | De -> Map.ofList [ "holz", "wood" ]

    let itemName culture itemId =
        match culture, itemId with
        | De, "wood" -> "Holz"
        | _, "wood" -> "wood"
        | _, unknown -> unknown

    let private template culture key =
        match culture, key with
        | En, "location.forest.description" -> "You are standing in a quiet forest."
        | De, "location.forest.description" -> "Du stehst in einem stillen Wald."
        | En, "gather.wood.success" -> "You gather {amount} {item}."
        | De, "gather.wood.success" -> "Du sammelst {amount} {item}."
        | En, "gather.no_wood_here" -> "There is no useful wood here."
        | De, "gather.no_wood_here" -> "Hier gibt es kein brauchbares Holz."
        | En, "inventory.empty" -> "Inventory: empty."
        | De, "inventory.empty" -> "Inventar: leer."
        | En, "inventory.list" -> "Inventory: {items}."
        | De, "inventory.list" -> "Inventar: {items}."
        | En, "command.unknown" -> "I do not understand that command."
        | De, "command.unknown" -> "Ich verstehe diesen Befehl nicht."
        | En, "script.error" -> "The script failed: {error}"
        | De, "script.error" -> "Das Skript ist fehlgeschlagen: {error}"
        | _, missing -> missing

    let text culture (message: Message) =
        message.Args
        |> Map.fold (fun (output: string) key value -> output.Replace("{" + key + "}", value)) (template culture message.Key)

module ResponseFormatting =
    let private formatInventoryItems culture (value: string) =
        value.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.choose (fun entry ->
            match entry.Split(':', 2) with
            | [| itemId; amount |] -> Some(amount + " " + Localizer.itemName culture itemId)
            | _ -> None)
        |> String.concat ", "

    let localizeMessage culture (message: Message) =
        let args =
            message.Args
            |> Map.map (fun key value ->
                match key with
                | "item" -> Localizer.itemName culture value
                | "items" -> formatInventoryItems culture value
                | _ -> value)

        Localizer.text culture { message with Args = args }
