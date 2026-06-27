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
        | En, "move.leave.room" -> "{actor} goes {direction}."
        | De, "move.leave.room" -> "{actor} geht nach {direction}."
        | En, "move.arrive.room" -> "{actor} arrives."
        | De, "move.arrive.room" -> "{actor} kommt an."
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
        | En, "item.wood.stack" -> "a pile of wood"
        | De, "item.wood.stack" -> "ein Holzhaufen"
        | En, "drop.success" -> "You drop {amount} {item}."
        | De, "drop.success" -> "Du legst {amount} {item} ab."
        | En, "drop.room" -> "{actor} drops {amount} {item}."
        | De, "drop.room" -> "{actor} legt {amount} {item} ab."
        | En, "drop.none" -> "You are not carrying any {item}."
        | De, "drop.none" -> "Du trägst kein {item}."
        | En, "drop.insufficient" -> "You are only carrying {amount} {item}."
        | De, "drop.insufficient" -> "Du trägst nur {amount} {item}."
        | En, "give.success" -> "You give {amount} {item} to {player}."
        | De, "give.success" -> "Du gibst {player} {amount} {item}."
        | En, "give.room" -> "{actor} gives {amount} {item} to {player}."
        | De, "give.room" -> "{actor} gibt {player} {amount} {item}."
        | En, "give.insufficient" -> "You are only carrying {amount} {item}."
        | De, "give.insufficient" -> "Du trägst nur {amount} {item}."
        | En, "give.none" -> "You are not carrying any {item}."
        | De, "give.none" -> "Du trägst kein {item}."
        | En, "give.not_here" -> "That player is not here."
        | De, "give.not_here" -> "Dieser Spieler ist nicht hier."
        | En, "give.self" -> "You cannot give an item to yourself."
        | De, "give.self" -> "Du kannst dir nichts selbst geben."
        | En, "take.success" -> "You pick up {amount} {item}."
        | De, "take.success" -> "Du nimmst {amount} {item}."
        | En, "take.room" -> "{actor} picks up {amount} {item}."
        | De, "take.room" -> "{actor} nimmt {amount} {item}."
        | En, "take.none" -> "There is no {item} here."
        | De, "take.none" -> "Hier liegt kein {item}."
        | En, "take.insufficient" -> "There are only {amount} {item} here."
        | De, "take.insufficient" -> "Hier liegen nur {amount} {item}."
        | En, "transfer.invalid_amount" -> "That amount must be from 1 to 100."
        | De, "transfer.invalid_amount" -> "Diese Menge muss zwischen 1 und 100 liegen."
        | En, "say.self" -> "You say, \"{text}\"."
        | De, "say.self" -> "Du sagst: \"{text}\"."
        | En, "say.room" -> "{actor} says, \"{text}\"."
        | De, "say.room" -> "{actor} sagt: \"{text}\"."
        | En, "say.empty" -> "Say what?"
        | De, "say.empty" -> "Was möchtest du sagen?"
        | En, "emote.self" -> "You {text}."
        | De, "emote.self" -> "Du {text}."
        | En, "emote.room" -> "{actor} {text}."
        | De, "emote.room" -> "{actor} {text}."
        | En, "emote.empty" -> "Emote what?"
        | De, "emote.empty" -> "Was möchtest du ausdrücken?"
        | En, "limbo.not_in_play" -> "You are not in the world. Enter play to continue."
        | De, "limbo.not_in_play" -> "Du bist nicht in der Welt. Betrete das Spiel, um fortzufahren."
        | En, "enter.success" -> "You enter {location}."
        | De, "enter.success" -> "Du betrittst {location}."
        | En, "enter.already" -> "You are already in the world."
        | De, "enter.already" -> "Du bist bereits in der Welt."
        | En, "object.wooden-stool.name" -> "a wooden stool"
        | De, "object.wooden-stool.name" -> "einen Holzhocker"
        | En, "object.wooden-stool.description" -> "A simple stool assembled from forest wood."
        | De, "object.wooden-stool.description" -> "Ein einfacher Hocker aus Waldbauholz."
        | En, "craft.stool.success" -> "You craft a wooden stool and set it down."
        | De, "craft.stool.success" -> "Du fertigst einen Holzhocker und stellst ihn ab."
        | En, "craft.stool.room" -> "{actor} crafts a wooden stool."
        | De, "craft.stool.room" -> "{actor} fertigt einen Holzhocker."
        | En, "craft.insufficient" -> "You need {amount} {item} for that."
        | De, "craft.insufficient" -> "Dafür brauchst du {amount} {item}."
        | En, "craft.unknown" -> "You do not know how to craft {recipe}."
        | De, "craft.unknown" -> "Du weißt nicht, wie man {recipe} fertigt."
        | En, "use.stool" -> "You sit on the wooden stool for a moment."
        | De, "use.stool" -> "Du setzt dich kurz auf den Holzhocker."
        | En, "use.placeable" -> "You use it."
        | De, "use.placeable" -> "Du benutzt es."
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

    let displayObjectName (state: GameState) culture objectId =
        match state.Objects |> Map.tryFind objectId with
        | Some gameObject when CarriedItems.isCarriedStack gameObject ->
            match CarriedItems.stackQuantity gameObject with
            | Some quantity -> $"{template culture gameObject.NameKey} ({quantity})"
            | None -> template culture gameObject.NameKey
        | Some gameObject -> template culture gameObject.NameKey
        | None -> objectId

    let emoteActorName (state: GameState) culture objectId =
        state.Objects
        |> Map.tryFind objectId
        |> Option.map (fun gameObject ->
            match culture, gameObject.NameKey with
            | De, "object.prototype-player.name" -> "Ein Prototyp-Spieler"
            | De, "object.prototype-scout.name" -> "Ein Prototyp-Späher"
            | De, "object.traveler.name" -> "Ein Reisender"
            | En, "object.prototype-player.name" -> "A prototype player"
            | En, "object.prototype-scout.name" -> "A prototype scout"
            | En, "object.traveler.name" -> "A traveler"
            | _, nameKey -> template culture nameKey)
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
        |> Array.map (Localizer.displayObjectName state culture)
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
                | "player" -> Localizer.objectName state culture value
                | "actor" -> Localizer.emoteActorName state culture value
                | "location" -> Localizer.objectName state culture value
                | _ -> value)

        Localizer.text culture { message with Args = args }
