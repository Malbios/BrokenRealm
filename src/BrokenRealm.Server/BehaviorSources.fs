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
        """type CraftRecipe = {
  costs: Record<string, number>;
  placement: {
    nameKey: string;
    descriptionKey: string;
    behaviorModuleId: string;
    behaviorClassName: string;
    tags: string;
    aliasesEn: string;
    aliasesDe: string;
  };
  successKey: string;
  roomKey: string;
};

const CRAFT_RECIPES: Record<string, CraftRecipe> = {
  stool: {
    costs: { wood: 2 },
    placement: {
      nameKey: "object.wooden-stool.name",
      descriptionKey: "object.wooden-stool.description",
      behaviorModuleId: "thing-behaviors",
      behaviorClassName: "PlaceableBehavior",
      tags: "thing,stool,placeable",
      aliasesEn: "stool,wooden stool",
      aliasesDe: "hocker,holzhocker"
    },
    successKey: "craft.stool.success",
    roomKey: "craft.stool.room"
  }
};

function craftFromRecipe(recipeId: string, context: VerbContext): VerbResult {
  const recipe = CRAFT_RECIPES[recipeId];
  if (!recipe) {
    return { effects: [{ type: "message", key: "craft.unknown", args: { recipe: recipeId } }] };
  }
  for (const [itemId, amount] of Object.entries(recipe.costs)) {
    if ((context.actor.inventory[itemId] ?? 0) < amount) {
      return {
        effects: [{ type: "message", key: "craft.insufficient", args: { item: itemId, amount: String(amount) } }]
      };
    }
  }
  const effects: ScriptEffect[] = [];
  for (const [itemId, amount] of Object.entries(recipe.costs)) {
    effects.push({ type: "removeInventory", itemId, amount });
  }
  effects.push({
    type: "createObject",
    locationId: context.actor.locationId,
    ...recipe.placement
  });
  effects.push({ type: "message", key: recipe.successKey, args: {} });
  effects.push({ type: "message", key: recipe.roomKey, args: { actor: context.actor.id } });
  return { effects };
}

class PlayerBehavior extends GameBehavior {
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
    },
    {
      methodName: "craft",
      patterns: [
        { culture: "en", pattern: "craft {recipe}" },
        { culture: "en", pattern: "make {recipe}" },
        { culture: "de", pattern: "fertige {recipe}" },
        { culture: "de", pattern: "baue {recipe}" }
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

  craft(context: VerbContext): VerbResult {
    return craftFromRecipe(context.args.recipe, context);
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

  tick(_context: VerbContext): VerbResult {
    return { effects: [] };
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

  override tick(context: VerbContext): VerbResult {
    const current = Number(context.this.properties.tickCount ?? 0);
    return { effects: [{ type: "replaceValue", path: ["tickCount"], value: current + 1 }] };
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

class PlaceableBehavior extends ThingBehavior {
  static override commands: CommandDefinition[] = [
    ...super.commands,
    {
      methodName: "use",
      patterns: [
        { culture: "en", pattern: "use {object}" },
        { culture: "en", pattern: "sit on {object}" },
        { culture: "de", pattern: "benutze {object}" },
        { culture: "de", pattern: "setz dich auf {object}" }
      ]
    },
    {
      methodName: "dismantle",
      patterns: [
        { culture: "en", pattern: "dismantle {object}" },
        { culture: "en", pattern: "take apart {object}" },
        { culture: "de", pattern: "zerlege {object}" }
      ]
    }
  ];

  use(context: VerbContext): VerbResult {
    if (context.this.tags.includes("stool")) {
      return { effects: [{ type: "message", key: "use.stool", args: {} }] };
    }
    return { effects: [{ type: "message", key: "use.placeable", args: {} }] };
  }

  dismantle(context: VerbContext): VerbResult {
    if (!context.this.tags.includes("placeable")) {
      return { effects: [{ type: "message", key: "dismantle.not_placeable", args: {} }] };
    }
    const effects: ScriptEffect[] = [{ type: "destroyObject", objectId: context.this.id }];
    if (context.this.tags.includes("stool")) {
      effects.push({ type: "addInventory", itemId: "wood", amount: 1 });
    }
    effects.push({ type: "message", key: "dismantle.success", args: {} });
    return { effects };
  }
}

const thingBehaviorClasses = { ThingBehavior, PlaceableBehavior };"""

    let village =
        """class VillageBehavior extends LocationBehavior {
  override look(context: VerbContext): VerbResult {
    const parent = super.look(context);
    const stoolCount = context.this.contents.filter(object => object.tags.includes("stool")).length;
    const effects = [...parent.effects];
    if (stoolCount > 0) {
      effects.push({ type: "message", key: "location.village.has_seating", args: { count: String(stoolCount) } });
    }
    if (Number(context.this.properties.comfort ?? 0) > 0) {
      effects.push({ type: "message", key: "location.village.comfortable", args: {} });
    }
    return { effects };
  }

  override tick(context: VerbContext): VerbResult {
    const stoolCount = context.this.contents.filter(object => object.tags.includes("stool")).length;
    const comfort = stoolCount > 0 ? 1 : 0;
    const current = Number(context.this.properties.comfort ?? 0);
    if (current === comfort) {
      return { effects: [] };
    }
    return { effects: [{ type: "replaceValue", path: ["comfort"], value: comfort }] };
  }
}

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
        """const CRAFT_RECIPES = {
  stool: {
    costs: { wood: 2 },
    placement: {
      nameKey: "object.wooden-stool.name",
      descriptionKey: "object.wooden-stool.description",
      behaviorModuleId: "thing-behaviors",
      behaviorClassName: "PlaceableBehavior",
      tags: "thing,stool,placeable",
      aliasesEn: "stool,wooden stool",
      aliasesDe: "hocker,holzhocker"
    },
    successKey: "craft.stool.success",
    roomKey: "craft.stool.room"
  }
};
function craftFromRecipe(recipeId, context) {
  const recipe = CRAFT_RECIPES[recipeId];
  if (!recipe) return { effects: [{ type: "message", key: "craft.unknown", args: { recipe: recipeId } }] };
  for (const [itemId, amount] of Object.entries(recipe.costs)) {
    if ((context.actor.inventory[itemId] ?? 0) < amount) {
      return { effects: [{ type: "message", key: "craft.insufficient", args: { item: itemId, amount: String(amount) } }] };
    }
  }
  const effects = [];
  for (const [itemId, amount] of Object.entries(recipe.costs)) {
    effects.push({ type: "removeInventory", itemId, amount });
  }
  effects.push({ type: "createObject", locationId: context.actor.locationId, ...recipe.placement });
  effects.push({ type: "message", key: recipe.successKey, args: {} });
  effects.push({ type: "message", key: recipe.roomKey, args: { actor: context.actor.id } });
  return { effects };
}
class PlayerBehavior extends GameBehavior {
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
    ] },
    { methodName: "craft", patterns: [
      { culture: "en", pattern: "craft {recipe}" }, { culture: "en", pattern: "make {recipe}" },
      { culture: "de", pattern: "fertige {recipe}" }, { culture: "de", pattern: "baue {recipe}" }
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
  craft(context) { return craftFromRecipe(context.args.recipe, context); }
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
  tick(_context) { return { effects: [] }; }
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
  tick(context) {
    const current = Number(context.this.properties.tickCount ?? 0);
    return { effects: [{ type: "replaceValue", path: ["tickCount"], value: current + 1 }] };
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
class PlaceableBehavior extends ThingBehavior {
  static commands = [
    ...super.commands,
    { methodName: "use", patterns: [
      { culture: "en", pattern: "use {object}" }, { culture: "en", pattern: "sit on {object}" },
      { culture: "de", pattern: "benutze {object}" }, { culture: "de", pattern: "setz dich auf {object}" }
    ] },
    { methodName: "dismantle", patterns: [
      { culture: "en", pattern: "dismantle {object}" }, { culture: "en", pattern: "take apart {object}" },
      { culture: "de", pattern: "zerlege {object}" }
    ] }
  ];
  use(context) {
    if (context.this.tags.includes("stool")) return { effects: [{ type: "message", key: "use.stool", args: {} }] };
    return { effects: [{ type: "message", key: "use.placeable", args: {} }] };
  }
  dismantle(context) {
    if (!context.this.tags.includes("placeable")) return { effects: [{ type: "message", key: "dismantle.not_placeable", args: {} }] };
    const effects = [{ type: "destroyObject", objectId: context.this.id }];
    if (context.this.tags.includes("stool")) effects.push({ type: "addInventory", itemId: "wood", amount: 1 });
    effects.push({ type: "message", key: "dismantle.success", args: {} });
    return { effects };
  }
}
const thingBehaviorClasses = { ThingBehavior, PlaceableBehavior };"""

    let villageCompiled =
        """class VillageBehavior extends LocationBehavior {
  look(context) {
    const parent = super.look(context);
    const stoolCount = context.this.contents.filter(object => object.tags.includes("stool")).length;
    const effects = [...parent.effects];
    if (stoolCount > 0) effects.push({ type: "message", key: "location.village.has_seating", args: { count: String(stoolCount) } });
    if (Number(context.this.properties.comfort ?? 0) > 0) effects.push({ type: "message", key: "location.village.comfortable", args: {} });
    return { effects };
  }
  tick(context) {
    const stoolCount = context.this.contents.filter(object => object.tags.includes("stool")).length;
    const comfort = stoolCount > 0 ? 1 : 0;
    const current = Number(context.this.properties.comfort ?? 0);
    if (current === comfort) return { effects: [] };
    return { effects: [{ type: "replaceValue", path: ["comfort"], value: comfort }] };
  }
}
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
