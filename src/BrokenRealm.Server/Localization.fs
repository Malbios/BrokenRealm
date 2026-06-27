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

    let directionAliases culture =
        match culture with
        | En -> Map.ofList [ "north", "north"; "south", "south" ]
        | De -> Map.ofList [ "norden", "north"; "nord", "north"; "süden", "south"; "süd", "south" ]

    let directionName culture directionId =
        match culture, directionId with
        | De, "north" -> "Norden"
        | De, "south" -> "Süden"
        | _, "north" -> "north"
        | _, "south" -> "south"
        | _, unknown -> unknown

    let private template culture key =
        match culture, key with
        | En, "location.forest.description" -> "You are standing in a quiet forest."
        | De, "location.forest.description" -> "Du stehst in einem stillen Wald."
        | En, "location.forest.atmosphere" -> "Pine-scented air moves through the branches."
        | De, "location.forest.atmosphere" -> "Kiefernduft zieht durch die Zweige."
        | En, "location.village.description" -> "You are standing in a small village."
        | De, "location.village.description" -> "Du stehst in einem kleinen Dorf."
        | En, "location.contents" -> "You see {objects}."
        | De, "location.contents" -> "Du siehst {objects}."
        | En, "object.forest.name" -> "forest"
        | De, "object.forest.name" -> "Wald"
        | En, "object.village.name" -> "village"
        | De, "object.village.name" -> "Dorf"
        | En, "object.prototype-player.name" -> "a prototype player"
        | De, "object.prototype-player.name" -> "einen Prototyp-Spieler"
        | En, "object.prototype-scout.name" -> "a prototype scout"
        | De, "object.prototype-scout.name" -> "einen Prototyp-Späher"
        | En, "object.traveler.name" -> "a traveler"
        | De, "object.traveler.name" -> "einen Reisenden"
        | En, "object.fallen-log.name" -> "a fallen log"
        | De, "object.fallen-log.name" -> "einen umgestürzten Baumstamm"
        | En, "object.fallen-log.description" -> "A moss-covered log lies across the forest floor."
        | De, "object.fallen-log.description" -> "Ein moosbedeckter Baumstamm liegt auf dem Waldboden."
        | En, "move.success" -> "You travel {direction}."
        | De, "move.success" -> "Du gehst nach {direction}."
        | En, "move.no_exit" -> "You cannot go that way."
        | De, "move.no_exit" -> "Dorthin kannst du nicht gehen."
        | En, "gather.wood.success" -> "You gather {amount} {item}."
        | De, "gather.wood.success" -> "Du sammelst {amount} {item}."
        | En, "gather.no_wood_here" -> "There is no useful wood here."
        | De, "gather.no_wood_here" -> "Hier gibt es kein brauchbares Holz."
        | En, "trail.renamed" -> "You name the trail {label}."
        | De, "trail.renamed" -> "Du nennst den Pfad {label}."
        | En, "inventory.empty" -> "Inventory: empty."
        | De, "inventory.empty" -> "Inventar: leer."
        | En, "inventory.list" -> "Inventory: {items}."
        | De, "inventory.list" -> "Inventar: {items}."
        | En, "command.unknown" -> "I do not understand that command."
        | De, "command.unknown" -> "Ich verstehe diesen Befehl nicht."
        | En, "script.error" -> "The script failed: {error}"
        | De, "script.error" -> "Das Skript ist fehlgeschlagen: {error}"
        | _, missing -> missing

    let objectName (state: GameState) culture objectId =
        state.Objects
        |> Map.tryFind objectId
        |> Option.map (fun object -> template culture object.NameKey)
        |> Option.defaultValue objectId

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

    let private formatObjects state culture (value: string) =
        value.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (Localizer.objectName state culture)
        |> String.concat ", "

    let localizeMessage state culture (message: Message) =
        let args =
            message.Args
            |> Map.map (fun key value ->
                match key with
                | "item" -> Localizer.itemName culture value
                | "items" -> formatInventoryItems culture value
                | "direction" -> Localizer.directionName culture value
                | "objects" -> formatObjects state culture value
                | _ -> value)

        Localizer.text culture { message with Args = args }
