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

class ThingBehavior extends GameBehavior {
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
    if (context.this.tags.includes("bench")) {
      return { effects: [{ type: "message", key: "use.bench", args: {} }] };
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
    } else if (context.this.tags.includes("bench")) {
      effects.push({ type: "addInventory", itemId: "wood", amount: 2 });
    }
    effects.push({ type: "message", key: "dismantle.success", args: {} });
    return { effects };
  }
}

class ContainerBehavior extends ThingBehavior {
  static override commands: CommandDefinition[] = [
    ...super.commands,
    {
      methodName: "open",
      patterns: [
        { culture: "en", pattern: "open {object}" },
        { culture: "de", pattern: "öffne {object}" },
        { culture: "de", pattern: "oeffne {object}" }
      ]
    }
  ];

  open(context: VerbContext): VerbResult {
    const entries = Object.entries(context.this.storedItems);
    if (entries.length === 0) {
      return { effects: [{ type: "message", key: "container.open.empty", args: {} }] };
    }
    return {
      effects: [{ type: "message", key: "container.open.items", args: { items: context.this.storedItems } }]
    };
  }
}

class WorkbenchBehavior extends ThingBehavior {
  static override commands: CommandDefinition[] = [
    ...super.commands,
    {
      methodName: "craft",
      patterns: [
        { culture: "en", pattern: "craft {recipe} at {object}" },
        { culture: "en", pattern: "make {recipe} at {object}" },
        { culture: "de", pattern: "fertige {recipe} an {object}" },
        { culture: "de", pattern: "baue {recipe} an {object}" }
      ]
    }
  ];

  craft(context: VerbContext): VerbResult {
    if (!context.this.tags.includes("workstation")) {
      return { effects: [{ type: "message", key: "craft.requires_workstation", args: {} }] };
    }
    if (context.args.object !== context.this.id) {
      return { effects: [{ type: "message", key: "craft.wrong_workstation", args: {} }] };
    }
    return craftFromRecipe(context.args.recipe, context);
  }
}

class CreatureBehavior extends GameBehavior {
  static override commands: CommandDefinition[] = [
    ...super.commands,
    {
      methodName: "examine",
      patterns: [
        { culture: "en", pattern: "examine {object}" },
        { culture: "en", pattern: "x {object}" },
        { culture: "de", pattern: "untersuche {object}" },
        { culture: "de", pattern: "betrachte {object}" }
      ]
    }
  ];

  override tick(context: TickContext): VerbResult {
    const steps = Number(context.this.properties.tickSteps ?? 0);
    const nextSteps = steps + 1;
    const effects: ScriptEffect[] = [{ type: "replaceValue", path: ["tickSteps"], value: nextSteps }];

    if (nextSteps % 2 === 0) {
      const directions = Object.keys(context.room.references).sort();
      const direction = directions[0];
      const destinationId = direction ? context.room.references[direction] : undefined;
      if (destinationId) {
        effects.push({ type: "moveObject", destinationId });
      }
    }

    return { effects };
  }

  examine(context: VerbContext): VerbResult {
    return { effects: [{ type: "message", key: context.this.descriptionKey, args: {} }] };
  }
}

class HumanoidCreatureBehavior extends CreatureBehavior {
  static override commands: CommandDefinition[] = [
    ...super.commands,
    {
      methodName: "talk",
      patterns: [
        { culture: "en", pattern: "talk to {object}" },
        { culture: "en", pattern: "speak to {object}" },
        { culture: "de", pattern: "sprich mit {object}" },
        { culture: "de", pattern: "rede mit {object}" }
      ]
    }
  ];

  override tick(_context: TickContext): VerbResult {
    return { effects: [] };
  }

  talk(context: VerbContext): VerbResult {
    const greetingKey = String(context.this.properties.greetingKey ?? "creature.talk.generic");
    return { effects: [{ type: "message", key: greetingKey, args: {} }] };
  }
}

const thingBehaviorClasses = {
  ThingBehavior,
  PlaceableBehavior,
  ContainerBehavior,
  WorkbenchBehavior,
  CreatureBehavior,
  HumanoidCreatureBehavior
};