type CraftRecipe = {
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
      tags: "thing,stool,placeable,seating",
      aliasesEn: "stool,wooden stool",
      aliasesDe: "hocker,holzhocker"
    },
    successKey: "craft.stool.success",
    roomKey: "craft.stool.room"
  },
  bench: {
    costs: { wood: 3 },
    placement: {
      nameKey: "object.wooden-bench.name",
      descriptionKey: "object.wooden-bench.description",
      behaviorModuleId: "thing-behaviors",
      behaviorClassName: "PlaceableBehavior",
      tags: "thing,bench,placeable,seating",
      aliasesEn: "bench,wooden bench",
      aliasesDe: "bank,holzbank"
    },
    successKey: "craft.bench.success",
    roomKey: "craft.bench.room"
  }
};

function findLocationContent(context: VerbContext, objectId: string) {
  return context.actor.locationContents.find(object => object.id === objectId) ?? null;
}

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
    },
    {
      methodName: "push",
      patterns: [
        { culture: "en", pattern: "push {object} {direction}" },
        { culture: "de", pattern: "schiebe {object} nach {direction}" }
      ]
    },
    {
      methodName: "moveObject",
      patterns: [
        { culture: "en", pattern: "move {object} to {destination}" },
        { culture: "de", pattern: "verschiebe {object} nach {destination}" }
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

  push(context: VerbContext): VerbResult {
    const object = findLocationContent(context, context.args.object);
    if (!object || !object.tags.includes("placeable")) {
      return { effects: [{ type: "message", key: "move_object.not_here", args: {} }] };
    }
    const destinationId = context.actor.locationReferences[context.args.direction];
    if (!destinationId) {
      return { effects: [{ type: "message", key: "move.no_exit", args: {} }] };
    }
    return {
      effects: [
        { type: "moveObject", objectId: object.id, destinationId },
        { type: "message", key: "push_object.success", args: { object: object.id, direction: context.args.direction } }
      ]
    };
  }

  moveObject(context: VerbContext): VerbResult {
    const object = findLocationContent(context, context.args.object);
    if (!object || object.tags.includes("carried")) {
      return { effects: [{ type: "message", key: "move_object.not_here", args: {} }] };
    }
    return {
      effects: [
        { type: "moveObject", objectId: object.id, destinationId: context.args.destination },
        { type: "message", key: "move_object.success", args: { object: object.id, destination: context.args.destination } }
      ]
    };
  }
}

const playerBehaviorClasses = { PlayerBehavior };