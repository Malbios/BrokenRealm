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
        | En ->
            Map.ofList
                [ "north", "north"
                  "south", "south"
                  "east", "east"
                  "west", "west" ]
        | De ->
            Map.ofList
                [ "norden", "north"
                  "nord", "north"
                  "süden", "south"
                  "suden", "south"
                  "süd", "south"
                  "osten", "east"
                  "ost", "east"
                  "westen", "west"
                  "west", "west" ]

    let directionName culture directionId =
        match culture, directionId with
        | De, "north" -> "Norden"
        | De, "south" -> "Süden"
        | De, "east" -> "Osten"
        | De, "west" -> "Westen"
        | _, "north" -> "north"
        | _, "south" -> "south"
        | _, "east" -> "east"
        | _, "west" -> "west"
        | _, unknown -> unknown

    let private template culture key =
        match culture, key with
        | En, "location.forest.description" -> "You are standing in a quiet forest."
        | De, "location.forest.description" -> "Du stehst in einem stillen Wald."
        | En, "location.forest.atmosphere" -> "Pine-scented air moves through the branches."
        | De, "location.forest.atmosphere" -> "Kiefernduft zieht durch die Zweige."
        | En, "location.village.description" -> "You are standing in a small village."
        | De, "location.village.description" -> "Du stehst in einem kleinen Dorf."
        | En, "location.clearing.description" -> "You are standing in a grassy clearing."
        | De, "location.clearing.description" -> "Du stehst auf einer grasbewachsenen Lichtung."
        | En, "object.clearing.name" -> "a grassy clearing"
        | De, "object.clearing.name" -> "eine grasbewachsene Lichtung"
        | En, "location.contents" -> "You see {objects}."
        | De, "location.contents" -> "Du siehst {objects}."
        | En, "location.creatures" -> "Also here: {objects}."
        | De, "location.creatures" -> "Hier sind auch: {objects}."
        | En, "location.items" -> "You see {objects}."
        | De, "location.items" -> "Du siehst {objects}."
        | En, "location.exits" -> "Exits: {exits}."
        | De, "location.exits" -> "Ausgänge: {exits}."
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
        | En, "object.forest-hare.name" -> "a forest hare"
        | De, "object.forest-hare.name" -> "einen Waldhasen"
        | En, "object.forest-hare.description" -> "A wary hare watches the undergrowth."
        | De, "object.forest-hare.description" -> "Ein scheuer Hase beobachtet das Unterholz."
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
        | En, "gather.depleted" -> "The forest needs time to offer more wood."
        | De, "gather.depleted" -> "Der Wald braucht Zeit, um mehr Holz anzubieten."
        | En, "player.hungry" -> "You feel hungry."
        | De, "player.hungry" -> "Du hast Hunger."
        | En, "object.village-farmer.name" -> "a village farmer"
        | De, "object.village-farmer.name" -> "einen Dorfbauern"
        | En, "object.village-farmer.description" -> "A weathered farmer watches the settlement."
        | De, "object.village-farmer.description" -> "Ein verwitterter Bauer beobachtet die Siedlung."
        | En, "creature.village-farmer.greeting" -> "The farmer nods. \"Plenty of work to do around here.\""
        | De, "creature.village-farmer.greeting" -> "Der Bauer nickt. \"Hier gibt es genug zu tun.\""
        | En, "creature.talk.generic" -> "They acknowledge you quietly."
        | De, "creature.talk.generic" -> "Sie nehmen dich still zur Kenntnis."
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
        | En, "object.wooden-bench.name" -> "a wooden bench"
        | De, "object.wooden-bench.name" -> "eine Holzbank"
        | En, "object.wooden-bench.description" -> "A sturdy bench assembled from forest wood."
        | De, "object.wooden-bench.description" -> "Eine stabile Bank aus Waldbauholz."
        | En, "craft.bench.success" -> "You craft a wooden bench and set it down."
        | De, "craft.bench.success" -> "Du fertigst eine Holzbank und stellst sie ab."
        | En, "craft.bench.room" -> "{actor} crafts a wooden bench."
        | De, "craft.bench.room" -> "{actor} fertigt eine Holzbank."
        | En, "use.bench" -> "You rest on the wooden bench for a moment."
        | De, "use.bench" -> "Du ruhst dich kurz auf der Holzbank aus."
        | En, "move_object.not_here" -> "You do not see that here."
        | De, "move_object.not_here" -> "Das siehst du hier nicht."
        | En, "move_object.success" -> "You move {object} to {destination}."
        | De, "move_object.success" -> "Du verschiebst {object} nach {destination}."
        | En, "push_object.success" -> "You push {object} to the {direction}."
        | De, "push_object.success" -> "Du schiebst {object} nach {direction}."
        | En, "craft.insufficient" -> "You need {amount} {item} for that."
        | De, "craft.insufficient" -> "Dafür brauchst du {amount} {item}."
        | En, "craft.unknown" -> "You do not know how to craft {recipe}."
        | De, "craft.unknown" -> "Du weißt nicht, wie man {recipe} fertigt."
        | En, "craft.requires_workstation" -> "That cannot be used for crafting."
        | De, "craft.requires_workstation" -> "Damit kann man nichts fertigen."
        | En, "craft.wrong_workstation" -> "You need to use the workbench here."
        | De, "craft.wrong_workstation" -> "Du musst die Werkbank hier benutzen."
        | En, "use.stool" -> "You sit on the wooden stool for a moment."
        | De, "use.stool" -> "Du setzt dich kurz auf den Holzhocker."
        | En, "use.placeable" -> "You use it."
        | De, "use.placeable" -> "Du benutzt es."
        | En, "dismantle.success" -> "You dismantle the object."
        | De, "dismantle.success" -> "Du zerlegst das Objekt."
        | En, "dismantle.not_placeable" -> "That cannot be dismantled."
        | De, "dismantle.not_placeable" -> "Das kannst du nicht zerlegen."
        | En, "object.village-crate.name" -> "a wooden crate"
        | De, "object.village-crate.name" -> "eine Holzkiste"
        | En, "object.village-crate.description" -> "A sturdy wooden crate with a hinged lid."
        | De, "object.village-crate.description" -> "Eine stabile Holzkiste mit aufklappbarem Deckel."
        | En, "object.village-workbench.name" -> "a wooden workbench"
        | De, "object.village-workbench.name" -> "eine Holzwerkbank"
        | En, "object.village-workbench.description" -> "A sturdy workbench for assembling furniture from wood."
        | De, "object.village-workbench.description" -> "Eine stabile Werkbank zum Zusammenbauen von Holzmöbeln."
        | En, "container.open.empty" -> "It is empty."
        | De, "container.open.empty" -> "Sie ist leer."
        | En, "container.open.items" -> "Inside: {items}."
        | De, "container.open.items" -> "Inhalt: {items}."
        | En, "container.not_here" -> "You do not see that container here."
        | De, "container.not_here" -> "Diese Behälter siehst du hier nicht."
        | En, "container.capacity.full" -> "That container is full."
        | De, "container.capacity.full" -> "Dieser Behälter ist voll."
        | En, "disambiguation.prompt" ->
            "Which one do you mean?"
            + System.Environment.NewLine
            + "{options}"
            + System.Environment.NewLine
            + "Reply with a number, or use 1.stool / all stool in your command."
        | De, "disambiguation.prompt" ->
            "Was meinst du?"
            + System.Environment.NewLine
            + "{options}"
            + System.Environment.NewLine
            + "Antworte mit einer Zahl oder nutze 1.hocker / alle hocker im Befehl."
        | En, "disambiguation.invalid" -> "That is not a valid choice."
        | De, "disambiguation.invalid" -> "Das ist keine gültige Auswahl."
        | En, "put.success" -> "You put {amount} {item} in {object}."
        | De, "put.success" -> "Du legst {amount} {item} in {object}."
        | En, "put.room" -> "{actor} puts {amount} {item} in {object}."
        | De, "put.room" -> "{actor} legt {amount} {item} in {object}."
        | En, "put.none" -> "You are not carrying any {item}."
        | De, "put.none" -> "Du trägst kein {item}."
        | En, "put.insufficient" -> "You are only carrying {amount} {item}."
        | De, "put.insufficient" -> "Du trägst nur {amount} {item}."
        | En, "container.take.success" -> "You take {amount} {item} from {object}."
        | De, "container.take.success" -> "Du nimmst {amount} {item} aus {object}."
        | En, "container.take.room" -> "{actor} takes {amount} {item} from {object}."
        | De, "container.take.room" -> "{actor} nimmt {amount} {item} aus {object}."
        | En, "container.take.none" -> "There is no {item} in that container."
        | De, "container.take.none" -> "In dem Behälter ist kein {item}."
        | En, "container.take.insufficient" -> "That container only holds {amount} {item}."
        | De, "container.take.insufficient" -> "In dem Behälter sind nur {amount} {item}."
        | En, "location.village.comfortable" -> "The village feels more welcoming."
        | De, "location.village.comfortable" -> "Das Dorf wirkt einladender."
        | En, "location.village.stocked" -> "Supplies are stored here."
        | De, "location.village.stocked" -> "Vorräte sind hier eingelagert."
        | En, "location.village.wildlife" -> "You notice wildlife moving through the settlement."
        | De, "location.village.wildlife" -> "Du bemerkst Wildtiere, die durch die Siedlung ziehen."
        | En, "build.clearing.success" -> "You stake out a grassy clearing to the {direction}."
        | De, "build.clearing.success" -> "Du markierst eine grasbewachsene Lichtung im {direction}."
        | En, "build.clearing.room" -> "{actor} stakes out a clearing to the {direction}."
        | De, "build.clearing.room" -> "{actor} markiert eine Lichtung im {direction}."
        | En, "build.unknown" -> "You do not know how to build {structure}."
        | De, "build.unknown" -> "Du weißt nicht, wie man {structure} baut."
        | En, "build.invalid_direction" -> "You cannot build in that direction."
        | De, "build.invalid_direction" -> "In diese Richtung kannst du nichts bauen."
        | En, "build.exit_exists" -> "There is already an exit to the {direction}."
        | De, "build.exit_exists" -> "Es gibt bereits einen Ausgang nach {direction}."
        | En, "build.insufficient" -> "You need {amount} {item} for that."
        | De, "build.insufficient" -> "Dafür brauchst du {amount} {item}."
        | En, "map.title" -> "Area map:"
        | De, "map.title" -> "Gebietskarte:"
        | En, "map.unavailable" -> "No map is available here."
        | De, "map.unavailable" -> "Hier ist keine Karte verfügbar."
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

    let private formatExits state culture (value: string) =
        value.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun entry ->
            match entry.Split(':', 2) with
            | [| direction; destinationId |] ->
                let directionLabel = Localizer.directionName culture direction
                let destinationLabel = Localizer.objectName state culture destinationId

                match culture with
                | De -> $"{directionLabel} zum {destinationLabel}"
                | _ -> $"{directionLabel} to {destinationLabel}"
            | _ -> entry)
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
                | "exits" -> formatExits state culture value
                | "player" -> Localizer.objectName state culture value
                | "actor" -> Localizer.emoteActorName state culture value
                | "location" -> Localizer.objectName state culture value
                | "object" -> Localizer.displayObjectName state culture value
                | "destination" -> Localizer.objectName state culture value
                | _ -> value)

        Localizer.text culture { message with Args = args }
