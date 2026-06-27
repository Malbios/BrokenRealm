namespace BrokenRealm.Server

module BehaviorSources =
    let core =
        """type CommandDefinition = {
  methodName: string;
  patterns: { culture: "en" | "de"; pattern: string }[];
};

class GameBehavior {
  static commands: CommandDefinition[] = [
    {
      methodName: "inventory",
      patterns: [
        { culture: "en", pattern: "inventory" },
        { culture: "en", pattern: "inv" },
        { culture: "de", pattern: "inventar" },
        { culture: "de", pattern: "inv" }
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
}

class LocationBehavior extends GameBehavior {
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
    return {
      effects: [{ type: "message", key: context.this.descriptionKey, args: {} }]
    };
  }

  move(context: VerbContext): VerbResult {
    const direction = context.args.direction;
    const destinationId = context.this.references[direction];

    if (!destinationId) {
      return { effects: [{ type: "message", key: "move.no_exit", args: {} }] };
    }

    return {
      effects: [
        { type: "movePlayer", destinationId },
        { type: "message", key: "move.success", args: { direction } }
      ]
    };
  }
}

class ForestBehavior extends LocationBehavior {
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
}

class VillageBehavior extends LocationBehavior {}

const behaviorClasses = {
  GameBehavior,
  LocationBehavior,
  ForestBehavior,
  VillageBehavior
};"""

    let coreCompiled =
        """class GameBehavior {
  static commands = [
    { methodName: "inventory", patterns: [
      { culture: "en", pattern: "inventory" }, { culture: "en", pattern: "inv" },
      { culture: "de", pattern: "inventar" }, { culture: "de", pattern: "inv" }
    ] }
  ];
  inventory(context) {
    const entries = Object.entries(context.actor.inventory);
    const effects = entries.length === 0
      ? [{ type: "message", key: "inventory.empty", args: {} }]
      : [{ type: "message", key: "inventory.list", args: { items: context.actor.inventory } }];
    return { effects };
  }
}
class LocationBehavior extends GameBehavior {
  static commands = [
    ...super.commands,
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
  look(context) { return { effects: [{ type: "message", key: context.this.descriptionKey, args: {} }] }; }
  move(context) {
    const direction = context.args.direction;
    const destinationId = context.this.references[direction];
    if (!destinationId) return { effects: [{ type: "message", key: "move.no_exit", args: {} }] };
    return { effects: [
      { type: "movePlayer", destinationId },
      { type: "message", key: "move.success", args: { direction } }
    ] };
  }
}
class ForestBehavior extends LocationBehavior {
  static commands = [
    ...super.commands,
    { methodName: "gather", patterns: [
      { culture: "en", pattern: "gather {item}" }, { culture: "en", pattern: "collect {item}" },
      { culture: "de", pattern: "sammle {item}" }, { culture: "de", pattern: "{item} sammeln" }
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
}
class VillageBehavior extends LocationBehavior {}
const behaviorClasses = { GameBehavior, LocationBehavior, ForestBehavior, VillageBehavior };"""
