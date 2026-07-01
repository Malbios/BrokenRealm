namespace BrokenRealm.Server

module Cultures =
    let parse (value: string) =
        match value.Trim().ToLowerInvariant() with
        | "de" -> De
        | _ -> En

module Localizer =
    let itemAliases culture =
        match culture with
        | En -> Map.ofList [ "wood", "wood"; "berries", "berries"; "strongbox key", "strongbox-key"; "key", "strongbox-key" ]
        | De -> Map.ofList [ "holz", "wood"; "beeren", "berries"; "truhenschlüssel", "strongbox-key"; "truhenschluessel", "strongbox-key"; "schlüssel", "strongbox-key" ]

    let itemName culture itemId =
        match culture, itemId with
        | De, "wood" -> "Holz"
        | De, "berries" -> "Beeren"
        | De, "strongbox-key" -> "Truhenschlüssel"
        | _, "wood" -> "wood"
        | _, "berries" -> "berries"
        | _, "strongbox-key" -> "strongbox key"
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
        | En, "gather.depleted" -> "The forest needs time to offer more {item}."
        | De, "gather.depleted" -> "Der Wald braucht Zeit, um mehr {item} anzubieten."
        | En, "gather.no_berries_here" -> "There are no berries to gather here."
        | De, "gather.no_berries_here" -> "Hier gibt es keine Beeren zum Sammeln."
        | En, "gather.berries.success" -> "You gather {amount} {item}."
        | De, "gather.berries.success" -> "Du sammelst {amount} {item}."
        | En, "gather.unknown_item" -> "You cannot gather {item} here."
        | De, "gather.unknown_item" -> "Du kannst hier kein {item} sammeln."
        | En, "location.forest.berries" -> "Wild berries grow along the trail."
        | De, "location.forest.berries" -> "Wilde Beeren wachsen am Pfad."
        | En, "item.berries.stack" -> "a handful of berries"
        | De, "item.berries.stack" -> "eine Handvoll Beeren"
        | En, "player.hungry" -> "You feel hungry."
        | De, "player.hungry" -> "Du hast Hunger."
        | En, "player.starving" -> "You are starving."
        | De, "player.starving" -> "Du verhungerst."
        | En, "eat.success" -> "You eat some {item} and feel less hungry."
        | De, "eat.success" -> "Du isst etwas {item} und hast weniger Hunger."
        | En, "eat.none" -> "You are not carrying any {item}."
        | De, "eat.none" -> "Du trägst kein {item} bei dir."
        | En, "eat.not_edible" -> "You cannot eat {item}."
        | De, "eat.not_edible" -> "Du kannst {item} nicht essen."
        | En, "object.village-farmer.name" -> "a village farmer"
        | De, "object.village-farmer.name" -> "einen Dorfbauern"
        | En, "object.village-farmer.description" -> "A weathered farmer watches the settlement."
        | De, "object.village-farmer.description" -> "Ein verwitterter Bauer beobachtet die Siedlung."
        | En, "creature.village-farmer.greeting" -> "The farmer nods. \"Plenty of work to do around here.\""
        | De, "creature.village-farmer.greeting" -> "Der Bauer nickt. \"Hier gibt es genug zu tun.\""
        | En, "creature.village-farmer.talk.working" -> "The farmer wipes his brow. \"Let me finish stocking this crate.\""
        | En, "creature.village-farmer.talk.interrupted" -> "The farmer pauses and wipes his brow. \"This can wait.\""
        | De, "creature.village-farmer.talk.interrupted" -> "Der Bauer hält inne und wischt sich die Stirn. \"Das kann warten.\""
        | De, "creature.village-farmer.talk.working" -> "Der Bauer wischt sich die Stirn. \"Lass mich diese Kiste erst füllen.\""
        | En, "creature.village-farmer.talk.resting" -> "The farmer stretches. \"A moment's rest never hurt.\""
        | De, "creature.village-farmer.talk.resting" -> "Der Bauer streckt sich. \"Eine kurze Pause schadet nicht.\""
        | En, "creature.village-farmer.notice.enter" -> "The farmer glances up from his work."
        | De, "creature.village-farmer.notice.enter" -> "Der Bauer blickt von seiner Arbeit auf."
        | En, "creature.village-farmer.notice.enter.room" -> "The farmer glances up from his work."
        | De, "creature.village-farmer.notice.enter.room" -> "Der Bauer blickt von seiner Arbeit auf."
        | En, "creature.forest-hare.notice.enter" -> "A forest hare startles and bounds into the undergrowth."
        | De, "creature.forest-hare.notice.enter" -> "Ein Waldhase erschrickt und schießt ins Unterholz."
        | En, "creature.forest-hare.notice.enter.room" -> "A forest hare startles and bounds into the undergrowth."
        | De, "creature.forest-hare.notice.enter.room" -> "Ein Waldhase erschrickt und schießt ins Unterholz."
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
        | En, "container.open.empty_with_capacity" -> "It is empty. ({used}/{capacity})"
        | De, "container.open.empty_with_capacity" -> "Sie ist leer. ({used}/{capacity})"
        | En, "container.open.items" -> "Inside: {items}."
        | De, "container.open.items" -> "Inhalt: {items}."
        | En, "container.open.items_with_capacity" -> "Inside: {items}. ({used}/{capacity})"
        | De, "container.open.items_with_capacity" -> "Inhalt: {items}. ({used}/{capacity})"
        | En, "craft.strongbox-key.success" -> "You carve a strongbox key from spare wood."
        | De, "craft.strongbox-key.success" -> "Du schnitzst einen Truhenschlüssel aus übrigem Holz."
        | En, "craft.strongbox-key.room" -> "{actor} crafts a strongbox key."
        | De, "craft.strongbox-key.room" -> "{actor} fertigt einen Truhenschlüssel."
        | En, "item.strongbox-key.stack" -> "a strongbox key"
        | De, "item.strongbox-key.stack" -> "ein Truhenschlüssel"
        | En, "container.not_here" -> "You do not see that container here."
        | De, "container.not_here" -> "Diese Behälter siehst du hier nicht."
        | En, "container.capacity.full" -> "That container is full."
        | De, "container.capacity.full" -> "Dieser Behälter ist voll."
        | En, "container.locked" -> "It is locked."
        | De, "container.locked" -> "Es ist verschlossen."
        | En, "object.village-strongbox.name" -> "an iron strongbox"
        | De, "object.village-strongbox.name" -> "eine eiserne Truhe"
        | En, "object.village-strongbox.description" -> "A heavy iron strongbox is bolted to the floor."
        | De, "object.village-strongbox.description" -> "Eine schwere eiserne Truhe ist am Boden verankert."
        | En, "creature.village-farmer.working" -> "The farmer works at a wooden crate."
        | De, "creature.village-farmer.working" -> "Der Bauer arbeitet an einer Holzkiste."
        | En, "creature.village-farmer.resting" -> "The farmer rests on a bench."
        | De, "creature.village-farmer.resting" -> "Der Bauer ruht auf einer Bank."
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
        | En, "help.title" -> "Available commands (angle brackets show placeholders):"
        | De, "help.title" -> "Verfügbare Befehle (spitze Klammern zeigen Platzhalter):"
        | En, "help.section.movement" ->
            "look, l — Look around the current room.\n"
            + "go <direction>, walk <direction> — Travel through an exit.\n"
            + "map — Show the explored area map."
        | De, "help.section.movement" ->
            "schau, l — Schau dich im aktuellen Raum um.\n"
            + "gehe nach <direction>, geh nach <direction> — Reise durch einen Ausgang.\n"
            + "karte — Zeige die erkundete Gebietskarte."
        | En, "help.section.places" ->
            "gather <item>, collect <item> — Gather wood or berries in the forest.\n"
            + "build clearing <direction> — Carve a new clearing from the village (costs 4 wood).\n"
            + "name trail <label> — Rename the forest trail token."
        | De, "help.section.places" ->
            "sammle <item>, <item> sammeln — Sammle Holz oder Beeren im Wald.\n"
            + "baue clearing nach <direction> — Schlage vom Dorf aus eine Lichtung (4 Holz).\n"
            + "nenne pfad <label> — Benenne das Waldwege-Token um."
        | En, "help.section.inventory" ->
            "inventory, inv — List what you are carrying.\n"
            + "take <item> [from <object>] — Pick up items from the room or a container.\n"
            + "drop <item> [amount] — Drop carried items here.\n"
            + "put <item> in <object> — Store items in a container.\n"
            + "give <item> to <player> — Give items to another player.\n"
            + "eat <item> — Eat food to reduce hunger."
        | De, "help.section.inventory" ->
            "inventar, inv — Liste deinen Besitz.\n"
            + "nimm <item> [aus <object>] — Hebe Gegenstände aus dem Raum oder einem Behälter auf.\n"
            + "lege <item> ab [amount] — Lege getragene Gegenstände hier ab.\n"
            + "lege <item> in <object> — Lege Gegenstände in einen Behälter.\n"
            + "gib <item> an <player> — Gib Gegenstände an einen anderen Spieler.\n"
            + "iss <item>, esse <item> — Iss Essbares, um Hunger zu senken."
        | En, "help.section.objects" ->
            "examine <object>, x <object> — Inspect something nearby.\n"
            + "open <object> — View a container's contents (a key may be required).\n"
            + "use <object>, sit on <object> — Use furniture.\n"
            + "craft <recipe> at <object> — Craft at a workbench.\n"
            + "dismantle <object> — Break down placeable furniture for materials.\n"
            + "push <object> <direction> — Push a movable object toward an exit.\n"
            + "move <object> to <destination> — Relocate an object to another room."
        | De, "help.section.objects" ->
            "untersuche <object>, x <object> — Untersuche etwas in der Nähe.\n"
            + "öffne <object>, oeffne <object> — Sieh in einen Behälter (Schlüssel kann nötig sein).\n"
            + "benutze <object>, setz dich auf <object> — Nutze Möbel.\n"
            + "fertige <recipe> an <object> — Fertige an einer Werkbank.\n"
            + "zerlege <object> — Baue platzierbare Möbel zu Material zurück.\n"
            + "schiebe <object> nach <direction> — Schiebe einen Gegenstand zu einem Ausgang.\n"
            + "verschiebe <object> nach <destination> — Verlege einen Gegenstand in einen anderen Raum."
        | En, "help.section.social" ->
            "say <text> — Speak to everyone in the room.\n"
            + "emote <text>, : <text> — Perform an emote.\n"
            + "talk to <object> — Speak with an NPC."
        | De, "help.section.social" ->
            "sag <text>, sage <text> — Sprich zu allen im Raum.\n"
            + "emote <text>, * <text> — Führe eine Geste aus.\n"
            + "sprich mit <object>, rede mit <object> — Sprich mit einem NPC."
        | En, "help.section.meta" -> "help [command] — Explain one command, or show this full reference."
        | De, "help.section.meta" -> "hilfe [befehl] — Erkläre einen Befehl oder zeige diese Übersicht."
        | En, "help.unknown" -> "No help topic for '{cmd}'. Type help alone for the full command list."
        | De, "help.unknown" -> "Keine Hilfe für '{cmd}'. Gib nur hilfe ein für die vollständige Liste."
        | En, "help.topic.look" ->
            "look, l\n"
            + "Look around the current room. Lists creatures, items, exits, and hunger hints."
        | De, "help.topic.look" ->
            "schau, l, umsehen\n"
            + "Schau dich im aktuellen Raum um. Zeigt Wesen, Gegenstände, Ausgänge und Hungerhinweise."
        | En, "help.topic.go" ->
            "go <direction>, walk <direction>\n"
            + "Travel through an exit. Directions include north, south, east, and west."
        | De, "help.topic.go" ->
            "gehe nach <direction>, geh nach <direction>\n"
            + "Reise durch einen Ausgang. Richtungen sind z. B. norden, süden, osten und westen."
        | En, "help.topic.map" ->
            "map\n"
            + "Show the explored area map. Unvisited rooms appear as fog."
        | De, "help.topic.map" ->
            "karte\n"
            + "Zeige die erkundete Gebietskarte. Unbesuchte Räume erscheinen als Nebel."
        | En, "help.topic.gather" ->
            "gather <item>, collect <item>\n"
            + "Gather renewable resources in the forest, such as wood or berries. Yields recover over time."
        | De, "help.topic.gather" ->
            "sammle <item>, <item> sammeln\n"
            + "Sammle erneuerbare Ressourcen im Wald, z. B. Holz oder Beeren. Vorräte erholen sich mit der Zeit."
        | En, "help.topic.build" ->
            "build clearing <direction>\n"
            + "From the village, spend 4 wood to carve a new clearing in that direction."
        | De, "help.topic.build" ->
            "baue clearing nach <direction>\n"
            + "Vom Dorf aus: 4 Holz ausgeben, um in diese Richtung eine Lichtung zu schlagen."
        | En, "help.topic.trail" ->
            "name trail <label>\n"
            + "Rename the forest trail token. Only works in the forest."
        | De, "help.topic.trail" ->
            "nenne pfad <label>\n"
            + "Benenne das Waldwege-Token um. Funktioniert nur im Wald."
        | En, "help.topic.inventory" ->
            "inventory, inv\n"
            + "List carried items and report hunger when you are hungry."
        | De, "help.topic.inventory" ->
            "inventar, inv\n"
            + "Liste getragene Gegenstände und melde Hunger, wenn du hungrig bist."
        | En, "help.topic.take" ->
            "take <item> [from <object>], pick up <item>\n"
            + "Pick up items from the room floor or from an open container."
        | De, "help.topic.take" ->
            "nimm <item> [aus <object>], hebe <item> auf\n"
            + "Hebe Gegenstände vom Boden oder aus einem offenen Behälter auf."
        | En, "help.topic.drop" ->
            "drop <item> [amount]\n"
            + "Drop carried items onto the ground in your current room."
        | De, "help.topic.drop" ->
            "lege <item> ab [amount]\n"
            + "Lege getragene Gegenstände im aktuellen Raum ab."
        | En, "help.topic.put" ->
            "put <item> in <object>, place <item> in <object>\n"
            + "Store carried items inside a container in the room."
        | De, "help.topic.put" ->
            "lege <item> in <object>, stecke <item> in <object>\n"
            + "Lege getragene Gegenstände in einen Behälter im Raum."
        | En, "help.topic.give" ->
            "give <item> to <player>\n"
            + "Transfer carried items to another player in the room."
        | De, "help.topic.give" ->
            "gib <item> an <player>\n"
            + "Übergebe getragene Gegenstände an einen anderen Spieler im Raum."
        | En, "help.topic.eat" ->
            "eat <item>\n"
            + "Eat edible food you carry, such as berries, to reduce hunger."
        | De, "help.topic.eat" ->
            "iss <item>, esse <item>\n"
            + "Iss mitgeführte Nahrung wie Beeren, um Hunger zu senken."
        | En, "help.topic.examine" ->
            "examine <object>, x <object>\n"
            + "Inspect a nearby thing, creature, or fixture."
        | De, "help.topic.examine" ->
            "untersuche <object>, x <object>\n"
            + "Untersuche einen nahen Gegenstand, ein Wesen oder eine Einrichtung."
        | En, "help.topic.open" ->
            "open <object>\n"
            + "Look inside a container. Locked containers need the matching key in your inventory."
        | De, "help.topic.open" ->
            "öffne <object>, oeffne <object>\n"
            + "Sieh in einen Behälter. Verschlossene Truhen brauchen den passenden Schlüssel."
        | En, "help.topic.use" ->
            "use <object>, sit on <object>\n"
            + "Use furniture or other placeable fixtures in the room."
        | De, "help.topic.use" ->
            "benutze <object>, setz dich auf <object>\n"
            + "Benutze Möbel oder andere platzierbare Einrichtungen im Raum."
        | En, "help.topic.craft" ->
            "craft <recipe> at <object>, make <recipe> at <object>\n"
            + "Craft at a workbench. Recipes include stool, bench, and strongbox-key."
        | De, "help.topic.craft" ->
            "fertige <recipe> an <object>, baue <recipe> an <object>\n"
            + "Fertige an einer Werkbank. Rezepte: Hocker, Bank, Truhenschlüssel."
        | En, "help.topic.dismantle" ->
            "dismantle <object>, take apart <object>\n"
            + "Break down placeable furniture and recover some wood."
        | De, "help.topic.dismantle" ->
            "zerlege <object>\n"
            + "Baue platzierbare Möbel zurück und gewinne etwas Holz."
        | En, "help.topic.push" ->
            "push <object> <direction>\n"
            + "Push a movable object toward an exit in that direction."
        | De, "help.topic.push" ->
            "schiebe <object> nach <direction>\n"
            + "Schiebe einen beweglichen Gegenstand zu einem Ausgang in diese Richtung."
        | En, "help.topic.relocate" ->
            "move <object> to <destination>\n"
            + "Relocate an object from the room to another room by name."
        | De, "help.topic.relocate" ->
            "verschiebe <object> nach <destination>\n"
            + "Verlege einen Gegenstand aus dem Raum in einen anderen Raum."
        | En, "help.topic.say" ->
            "say <text>\n"
            + "Speak to everyone currently in the room."
        | De, "help.topic.say" ->
            "sag <text>, sage <text>\n"
            + "Sprich zu allen, die gerade im Raum sind."
        | En, "help.topic.emote" ->
            "emote <text>, : <text>\n"
            + "Perform an emote visible to everyone in the room."
        | De, "help.topic.emote" ->
            "emote <text>, * <text>\n"
            + "Führe eine Geste aus, die alle im Raum sehen."
        | En, "help.topic.talk" ->
            "talk to <object>, speak to <object>\n"
            + "Speak with an NPC creature in the room."
        | De, "help.topic.talk" ->
            "sprich mit <object>, rede mit <object>\n"
            + "Sprich mit einem NPC im Raum."
        | En, "help.topic.help" ->
            "help [command], h, ?\n"
            + "Show the full command list, or explain one command such as help gather."
        | De, "help.topic.help" ->
            "hilfe [befehl], h, ?\n"
            + "Zeige die vollständige Befehlsliste oder erkläre einen Befehl, z. B. hilfe sammeln."
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
                | "used" | "capacity" -> value
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
