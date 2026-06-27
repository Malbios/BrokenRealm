namespace BrokenRealm.Server

module BehaviorSources =
    let moduleMarkerPrefix = "// @brokenrealm-module "

    let core =
        """type CommandDefinition = {
  methodName: string;
  patterns: { culture: "en" | "de"; pattern: string }[];
};

class GameBehavior {
  static commands: CommandDefinition[] = [];
}

const coreBehaviorClasses = { GameBehavior };"""

    let player =
        """class PlayerBehavior extends GameBehavior {
  static override commands: CommandDefinition[] = [
    ...super.commands,
    {
      methodName: "inventory",
      patterns: [
        { culture: "en", pattern: "inventory" },
        { culture: "en", pattern: "inv" },
        { culture: "de", pattern: "inventar" },
        { culture: "de", pattern: "inv" }
      ]
    },
    {
      methodName: "drop",
      patterns: [
        { culture: "en", pattern: "drop {amount} {item}" },
        { culture: "en", pattern: "drop {item}" },
        { culture: "de", pattern: "lege {amount} {item} ab" },
        { culture: "de", pattern: "lege {item} ab" }
      ]
    },
    {
      methodName: "give",
      patterns: [
        { culture: "en", pattern: "give {amount} {item} to {player}" },
        { culture: "en", pattern: "give {item} to {player}" },
        { culture: "de", pattern: "gib {amount} {item} an {player}" },
        { culture: "de", pattern: "gib {item} an {player}" }
      ]
    },
    {
      methodName: "take",
      patterns: [
        { culture: "en", pattern: "take {amount} {item}" },
        { culture: "en", pattern: "pick up {amount} {item}" },
        { culture: "en", pattern: "take {item}" },
        { culture: "en", pattern: "pick up {item}" },
        { culture: "de", pattern: "nimm {amount} {item}" },
        { culture: "de", pattern: "hebe {amount} {item} auf" },
        { culture: "de", pattern: "nimm {item}" },
        { culture: "de", pattern: "hebe {item} auf" }
      ]
    },
    {
      methodName: "say",
      patterns: [
        { culture: "en", pattern: "say {text}" },
        { culture: "en", pattern: "say" },
        { culture: "de", pattern: "sag {text}" },
        { culture: "de", pattern: "sag" },
        { culture: "de", pattern: "sage {text}" },
        { culture: "de", pattern: "sage" }
      ]
    },
    {
      methodName: "emote",
      patterns: [
        { culture: "en", pattern: "emote {text}" },
        { culture: "en", pattern: "emote" },
        { culture: "en", pattern: ": {text}" },
        { culture: "de", pattern: "emote {text}" },
        { culture: "de", pattern: "emote" },
        { culture: "de", pattern: "* {text}" }
      ]
    }
  ];

  inventory(context: VerbContext): VerbResult {
    const entries = Object.entries(context.actor.inventory);
    const effects: ScriptEffect[] = entries.length === 0
      ? [{ type: "message", key: "inventory.empty", args: {} }]
      : [{ type: "message", key: "inventory.list", args: { items: context.actor.inventory } }];
    return { effects };
  }

  drop(context: VerbContext): VerbResult {
    const itemId = context.args.item;
    const available = context.actor.inventory[itemId] ?? 0;
    const requested = context.args.amount ? Number.parseInt(context.args.amount, 10) : 1;
    if (!Number.isFinite(requested) || requested < 1 || requested > 100) {
      return { effects: [{ type: "message", key: "transfer.invalid_amount", args: {} }] };
    }
    if (available < 1) {
      return { effects: [{ type: "message", key: "drop.none", args: { item: itemId } }] };
    }
    if (available < requested) {
      return { effects: [{ type: "message", key: "drop.insufficient", args: { item: itemId, amount: String(available) } }] };
    }
    return {
      effects: [
        { type: "transferItem", itemId, amount: requested, destinationId: context.actor.locationId },
        { type: "message", key: "drop.success", args: { item: itemId, amount: String(requested) } },
        { type: "message", key: "drop.room", args: { actor: context.actor.id, item: itemId, amount: String(requested) } }
      ]
    };
  }

  give(context: VerbContext): VerbResult {
    const itemId = context.args.item;
    const playerId = context.args.player;
    if (playerId === context.actor.id) {
      return { effects: [{ type: "message", key: "give.self", args: {} }] };
    }
    const available = context.actor.inventory[itemId] ?? 0;
    const requested = context.args.amount ? Number.parseInt(context.args.amount, 10) : 1;
    if (!Number.isFinite(requested) || requested < 1 || requested > 100) {
      return { effects: [{ type: "message", key: "transfer.invalid_amount", args: {} }] };
    }
    if (available < 1) {
      return { effects: [{ type: "message", key: "give.none", args: { item: itemId } }] };
    }
    if (available < requested) {
      return { effects: [{ type: "message", key: "give.insufficient", args: { item: itemId, amount: String(available) } }] };
    }
    const recipient = context.actor.locationContents.find(object => object.id === playerId);
    if (!recipient || !recipient.tags.includes("player")) {
      return { effects: [{ type: "message", key: "give.not_here", args: {} }] };
    }
    return {
      effects: [
        { type: "transferItem", itemId, amount: requested, destinationId: playerId },
        { type: "message", key: "give.success", args: { item: itemId, player: playerId, amount: String(requested) } },
        { type: "message", key: "give.room", args: { actor: context.actor.id, item: itemId, player: playerId, amount: String(requested) } }
      ]
    };
  }

  take(context: VerbContext): VerbResult {
    const itemId = context.args.item;
    const available = context.actor.floorItems[itemId] ?? 0;
    const requested = context.args.amount ? Number.parseInt(context.args.amount, 10) : 1;
    if (!Number.isFinite(requested) || requested < 1 || requested > 100) {
      return { effects: [{ type: "message", key: "transfer.invalid_amount", args: {} }] };
    }
    if (available < 1) {
      return { effects: [{ type: "message", key: "take.none", args: { item: itemId } }] };
    }
    if (available < requested) {
      return { effects: [{ type: "message", key: "take.insufficient", args: { item: itemId, amount: String(available) } }] };
    }
    return {
      effects: [
        {
          type: "transferItem",
          itemId,
          amount: requested,
          sourceId: context.actor.locationId,
          destinationId: context.actor.id
        },
        { type: "message", key: "take.success", args: { item: itemId, amount: String(requested) } },
        { type: "message", key: "take.room", args: { actor: context.actor.id, item: itemId, amount: String(requested) } }
      ]
    };
  }

  say(context: VerbContext): VerbResult {
    const text = (context.args.text ?? "").trim();
    if (!text) {
      return { effects: [{ type: "message", key: "say.empty", args: {} }] };
    }
    return {
      effects: [
        { type: "message", key: "say.self", args: { text } },
        { type: "message", key: "say.room", args: { actor: context.actor.id, text } }
      ]
    };
  }

  emote(context: VerbContext): VerbResult {
    const text = (context.args.text ?? "").trim();
    if (!text) {
      return { effects: [{ type: "message", key: "emote.empty", args: {} }] };
    }
    return {
      effects: [
        { type: "message", key: "emote.self", args: { text } },
        { type: "message", key: "emote.room", args: { actor: context.actor.id, text } }
      ]
    };
  }
}

const playerBehaviorClasses = { PlayerBehavior };"""

    let location =
        """class LocationBehavior extends GameBehavior {
  static override commands: CommandDefinition[] = [
    ...super.commands,
    {
      methodName: "look",
      patterns: [
        { culture: "en", pattern: "look" },
        { culture: "en", pattern: "l" },
        { culture: "de", pattern: "schau" },
        { culture: "de", pattern: "umsehen" },
        { culture: "de", pattern: "sieh dich um" }
      ]
    },
    {
      methodName: "move",
      patterns: [
        { culture: "en", pattern: "go {direction}" },
        { culture: "en", pattern: "walk {direction}" },
        { culture: "de", pattern: "gehe nach {direction}" },
        { culture: "de", pattern: "geh nach {direction}" }
      ]
    }
  ];

  look(context: VerbContext): VerbResult {
    const effects: ScriptEffect[] = [
      { type: "message", key: context.this.descriptionKey, args: {} }
    ];
    if (context.this.contents.length > 0) {
      effects.push({
        type: "message",
        key: "location.contents",
        args: { objects: context.this.contents.map(object => object.id).join(",") }
      });
    }
    return { effects };
  }

  move(context: VerbContext): VerbResult {
    const direction = context.args.direction;
    const destinationId = context.this.references[direction];
    if (!destinationId) {
      return { effects: [{ type: "message", key: "move.no_exit", args: {} }] };
    }
    return {
      effects: [
        { type: "message", key: "move.leave.room", args: { actor: context.actor.id, direction, roomId: context.actor.locationId } },
        { type: "moveObject", destinationId },
        { type: "message", key: "move.success", args: { direction } },
        { type: "message", key: "move.arrive.room", args: { actor: context.actor.id, roomId: destinationId } }
      ]
    };
  }
}

const locationBehaviorClasses = { LocationBehavior };"""

    let forest =
        """class ForestBehavior extends LocationBehavior implements Gatherable {
  static override commands: CommandDefinition[] = [
    ...super.commands,
    {
      methodName: "gather",
      patterns: [
        { culture: "en", pattern: "gather {item}" },
        { culture: "en", pattern: "collect {item}" },
        { culture: "de", pattern: "sammle {item}" },
        { culture: "de", pattern: "{item} sammeln" }
      ]
    },
    {
      methodName: "renameTrail",
      patterns: [
        { culture: "en", pattern: "name trail {label}" },
        { culture: "de", pattern: "nenne pfad {label}" }
      ]
    }
  ];

  override look(context: VerbContext): VerbResult {
    const parent = super.look(context);
    return {
      effects: [
        ...parent.effects,
        { type: "message", key: "location.forest.atmosphere", args: {} }
      ]
    };
  }

  gather(context: VerbContext): VerbResult {
    const item = context.args.item;
    if (item !== "wood" || !context.this.tags.includes("wood")) {
      return { effects: [{ type: "message", key: "gather.no_wood_here", args: {} }] };
    }
    const amount = 2;
    return {
      effects: [
        { type: "addInventory", itemId: "wood", amount },
        { type: "message", key: "gather.wood.success", args: { amount, item: "wood" } }
      ]
    };
  }

  renameTrail(context: VerbContext): VerbResult {
    return {
      effects: [{
        type: "invokeAnonymous",
        path: ["trailToken"],
        methodName: "rename",
        args: { label: context.args.label }
      }]
    };
  }
}

const forestBehaviorClasses = { ForestBehavior };"""

    let thing =
        """class ThingBehavior extends GameBehavior {
  static override commands: CommandDefinition[] = [
    {
      methodName: "examine",
      patterns: [
        { culture: "en", pattern: "examine {object}" },
        { culture: "en", pattern: "x {object}" },
        { culture: "de", pattern: "untersuche {object}" }
      ]
    }
  ];

  examine(context: VerbContext): VerbResult {
    return {
      effects: [{ type: "message", key: context.this.descriptionKey, args: {} }]
    };
  }
}

const thingBehaviorClasses = { ThingBehavior };"""

    let village =
        """class VillageBehavior extends LocationBehavior {}

const villageBehaviorClasses = { VillageBehavior };"""

    let anonymous =
        """class TrailTokenBehavior {
  static commands: CommandDefinition[] = [];

  describe(context: AnonymousBehaviorContext): VerbResult {
    return {
      effects: [{ type: "message", key: "token.describe", args: { label: context.this.properties.label } }]
    };
  }

  rename(context: AnonymousBehaviorContext): VerbResult {
    return {
      effects: [
        { type: "replaceValue", path: [...context.this.storagePath, "label"], value: context.args.label },
        { type: "message", key: "trail.renamed", args: { label: context.args.label } }
      ]
    };
  }
}

const anonymousBehaviorClasses = { TrailTokenBehavior };"""

    let coreCompiled =
        """class GameBehavior {
  static commands = [];
}
const coreBehaviorClasses = { GameBehavior };"""

    let playerCompiled =
        """class PlayerBehavior extends GameBehavior {
  static commands = [
    ...super.commands,
    { methodName: "inventory", patterns: [
      { culture: "en", pattern: "inventory" }, { culture: "en", pattern: "inv" },
      { culture: "de", pattern: "inventar" }, { culture: "de", pattern: "inv" }
    ] },
    { methodName: "drop", patterns: [
      { culture: "en", pattern: "drop {amount} {item}" }, { culture: "en", pattern: "drop {item}" },
      { culture: "de", pattern: "lege {amount} {item} ab" }, { culture: "de", pattern: "lege {item} ab" }
    ] },
    { methodName: "give", patterns: [
      { culture: "en", pattern: "give {amount} {item} to {player}" }, { culture: "en", pattern: "give {item} to {player}" },
      { culture: "de", pattern: "gib {amount} {item} an {player}" }, { culture: "de", pattern: "gib {item} an {player}" }
    ] },
    { methodName: "take", patterns: [
      { culture: "en", pattern: "take {amount} {item}" }, { culture: "en", pattern: "pick up {amount} {item}" },
      { culture: "en", pattern: "take {item}" }, { culture: "en", pattern: "pick up {item}" },
      { culture: "de", pattern: "nimm {amount} {item}" }, { culture: "de", pattern: "hebe {amount} {item} auf" },
      { culture: "de", pattern: "nimm {item}" }, { culture: "de", pattern: "hebe {item} auf" }
    ] },
    { methodName: "say", patterns: [
      { culture: "en", pattern: "say {text}" }, { culture: "en", pattern: "say" },
      { culture: "de", pattern: "sag {text}" }, { culture: "de", pattern: "sag" },
      { culture: "de", pattern: "sage {text}" }, { culture: "de", pattern: "sage" }
    ] },
    { methodName: "emote", patterns: [
      { culture: "en", pattern: "emote {text}" }, { culture: "en", pattern: "emote" },
      { culture: "en", pattern: ": {text}" },
      { culture: "de", pattern: "emote {text}" }, { culture: "de", pattern: "emote" },
      { culture: "de", pattern: "* {text}" }
    ] }
  ];
  inventory(context) {
    const entries = Object.entries(context.actor.inventory);
    const effects = entries.length === 0
      ? [{ type: "message", key: "inventory.empty", args: {} }]
      : [{ type: "message", key: "inventory.list", args: { items: context.actor.inventory } }];
    return { effects };
  }
  drop(context) {
    const itemId = context.args.item;
    const available = context.actor.inventory[itemId] ?? 0;
    const requested = context.args.amount ? Number.parseInt(context.args.amount, 10) : 1;
    if (!Number.isFinite(requested) || requested < 1 || requested > 100) {
      return { effects: [{ type: "message", key: "transfer.invalid_amount", args: {} }] };
    }
    if (available < 1) return { effects: [{ type: "message", key: "drop.none", args: { item: itemId } }] };
    if (available < requested) return { effects: [{ type: "message", key: "drop.insufficient", args: { item: itemId, amount: String(available) } }] };
    return { effects: [
      { type: "transferItem", itemId, amount: requested, destinationId: context.actor.locationId },
      { type: "message", key: "drop.success", args: { item: itemId, amount: String(requested) } },
      { type: "message", key: "drop.room", args: { actor: context.actor.id, item: itemId, amount: String(requested) } }
    ] };
  }
  give(context) {
    const itemId = context.args.item;
    const playerId = context.args.player;
    if (playerId === context.actor.id) return { effects: [{ type: "message", key: "give.self", args: {} }] };
    const available = context.actor.inventory[itemId] ?? 0;
    const requested = context.args.amount ? Number.parseInt(context.args.amount, 10) : 1;
    if (!Number.isFinite(requested) || requested < 1 || requested > 100) {
      return { effects: [{ type: "message", key: "transfer.invalid_amount", args: {} }] };
    }
    if (available < 1) return { effects: [{ type: "message", key: "give.none", args: { item: itemId } }] };
    if (available < requested) return { effects: [{ type: "message", key: "give.insufficient", args: { item: itemId, amount: String(available) } }] };
    const recipient = context.actor.locationContents.find(object => object.id === playerId);
    if (!recipient || !recipient.tags.includes("player")) return { effects: [{ type: "message", key: "give.not_here", args: {} }] };
    return { effects: [
      { type: "transferItem", itemId, amount: requested, destinationId: playerId },
      { type: "message", key: "give.success", args: { item: itemId, player: playerId, amount: String(requested) } },
      { type: "message", key: "give.room", args: { actor: context.actor.id, item: itemId, player: playerId, amount: String(requested) } }
    ] };
  }
  take(context) {
    const itemId = context.args.item;
    const available = context.actor.floorItems[itemId] ?? 0;
    const requested = context.args.amount ? Number.parseInt(context.args.amount, 10) : 1;
    if (!Number.isFinite(requested) || requested < 1 || requested > 100) {
      return { effects: [{ type: "message", key: "transfer.invalid_amount", args: {} }] };
    }
    if (available < 1) return { effects: [{ type: "message", key: "take.none", args: { item: itemId } }] };
    if (available < requested) return { effects: [{ type: "message", key: "take.insufficient", args: { item: itemId, amount: String(available) } }] };
    return { effects: [
      { type: "transferItem", itemId, amount: requested, sourceId: context.actor.locationId, destinationId: context.actor.id },
      { type: "message", key: "take.success", args: { item: itemId, amount: String(requested) } },
      { type: "message", key: "take.room", args: { actor: context.actor.id, item: itemId, amount: String(requested) } }
    ] };
  }
  say(context) {
    const text = (context.args.text ?? "").trim();
    if (!text) return { effects: [{ type: "message", key: "say.empty", args: {} }] };
    return { effects: [
      { type: "message", key: "say.self", args: { text } },
      { type: "message", key: "say.room", args: { actor: context.actor.id, text } }
    ] };
  }
  emote(context) {
    const text = (context.args.text ?? "").trim();
    if (!text) return { effects: [{ type: "message", key: "emote.empty", args: {} }] };
    return { effects: [
      { type: "message", key: "emote.self", args: { text } },
      { type: "message", key: "emote.room", args: { actor: context.actor.id, text } }
    ] };
  }
}
const playerBehaviorClasses = { PlayerBehavior };"""

    let locationCompiled =
        """class LocationBehavior extends GameBehavior {
  static commands = [
    { methodName: "look", patterns: [
      { culture: "en", pattern: "look" }, { culture: "en", pattern: "l" },
      { culture: "de", pattern: "schau" }, { culture: "de", pattern: "umsehen" },
      { culture: "de", pattern: "sieh dich um" }
    ] },
    { methodName: "move", patterns: [
      { culture: "en", pattern: "go {direction}" }, { culture: "en", pattern: "walk {direction}" },
      { culture: "de", pattern: "gehe nach {direction}" }, { culture: "de", pattern: "geh nach {direction}" }
    ] }
  ];
  look(context) {
    const effects = [{ type: "message", key: context.this.descriptionKey, args: {} }];
    if (context.this.contents.length > 0) {
      effects.push({
        type: "message", key: "location.contents",
        args: { objects: context.this.contents.map(object => object.id).join(",") }
      });
    }
    return { effects };
  }
  move(context) {
    const direction = context.args.direction;
    const destinationId = context.this.references[direction];
    if (!destinationId) return { effects: [{ type: "message", key: "move.no_exit", args: {} }] };
    return { effects: [
      { type: "message", key: "move.leave.room", args: { actor: context.actor.id, direction, roomId: context.actor.locationId } },
      { type: "moveObject", destinationId },
      { type: "message", key: "move.success", args: { direction } },
      { type: "message", key: "move.arrive.room", args: { actor: context.actor.id, roomId: destinationId } }
    ] };
  }
}
const locationBehaviorClasses = { LocationBehavior };"""

    let forestCompiled =
        """class ForestBehavior extends LocationBehavior {
  static commands = [
    ...super.commands,
    { methodName: "gather", patterns: [
      { culture: "en", pattern: "gather {item}" }, { culture: "en", pattern: "collect {item}" },
      { culture: "de", pattern: "sammle {item}" }, { culture: "de", pattern: "{item} sammeln" }
    ] },
    { methodName: "renameTrail", patterns: [
      { culture: "en", pattern: "name trail {label}" },
      { culture: "de", pattern: "nenne pfad {label}" }
    ] }
  ];
  look(context) {
    const parent = super.look(context);
    return { effects: [...parent.effects, { type: "message", key: "location.forest.atmosphere", args: {} }] };
  }
  gather(context) {
    const item = context.args.item;
    if (item !== "wood" || !context.this.tags.includes("wood")) {
      return { effects: [{ type: "message", key: "gather.no_wood_here", args: {} }] };
    }
    const amount = 2;
    return { effects: [
      { type: "addInventory", itemId: "wood", amount },
      { type: "message", key: "gather.wood.success", args: { amount, item: "wood" } }
    ] };
  }
  renameTrail(context) {
    return { effects: [{
      type: "invokeAnonymous", path: ["trailToken"], methodName: "rename", args: { label: context.args.label }
    }] };
  }
}
const forestBehaviorClasses = { ForestBehavior };"""

    let thingCompiled =
        """class ThingBehavior extends GameBehavior {
  static commands = [
    { methodName: "examine", patterns: [
      { culture: "en", pattern: "examine {object}" },
      { culture: "en", pattern: "x {object}" },
      { culture: "de", pattern: "untersuche {object}" }
    ] }
  ];
  examine(context) {
    return { effects: [{ type: "message", key: context.this.descriptionKey, args: {} }] };
  }
}
const thingBehaviorClasses = { ThingBehavior };"""

    let villageCompiled =
        """class VillageBehavior extends LocationBehavior {}
const villageBehaviorClasses = { VillageBehavior };"""

    let anonymousCompiled =
        """class TrailTokenBehavior {
  static commands = [];
  describe(context) {
    return { effects: [{ type: "message", key: "token.describe", args: { label: context.this.properties.label } }] };
  }
  rename(context) {
    return { effects: [
      { type: "replaceValue", path: [...context.this.storagePath, "label"], value: context.args.label },
      { type: "message", key: "trail.renamed", args: { label: context.args.label } }
    ] };
  }
}
const anonymousBehaviorClasses = { TrailTokenBehavior };"""

    let join (sources: string list) =
        System.String.Join(System.Environment.NewLine + System.Environment.NewLine, sources)

    let joinModules (modules: (string * string) list) =
        modules
        |> List.map (fun (moduleId, source) -> moduleMarkerPrefix + moduleId + System.Environment.NewLine + source)
        |> join
