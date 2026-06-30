type CraftRecipe = {
  costs: Record<string, number>;
  placement?: {
    nameKey: string;
    descriptionKey: string;
    behaviorModuleId: string;
    behaviorClassName: string;
    tags: string;
    aliasesEn: string;
    aliasesDe: string;
  };
  output?: {
    itemId: string;
    amount: number;
  };
  successKey: string;
  roomKey: string;
  sourceContainerId?: string;
};

const CRAFT_RECIPES: Record<string, CraftRecipe> = {
  "strongbox-key": {
    costs: { wood: 2 },
    output: { itemId: "strongbox-key", amount: 1 },
    successKey: "craft.strongbox-key.success",
    roomKey: "craft.strongbox-key.room",
    sourceContainerId: "village-crate"
  },
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

function availableCraftMaterials(
  context: VerbContext,
  itemId: string,
  sourceContainerId?: string
): { inventory: number; container: number } {
  const container = sourceContainerId ? (context.actor.containerStorage[sourceContainerId]?.[itemId] ?? 0) : 0;
  return {
    inventory: context.actor.inventory[itemId] ?? 0,
    container
  };
}

function consumeCraftMaterials(
  context: VerbContext,
  itemId: string,
  amount: number,
  sourceContainerId?: string
): ScriptEffect[] {
  const available = availableCraftMaterials(context, itemId, sourceContainerId);
  const fromInventory = Math.min(available.inventory, amount);
  const fromContainer = amount - fromInventory;
  const effects: ScriptEffect[] = [];

  if (fromInventory > 0) {
    effects.push({ type: "removeInventory", itemId, amount: fromInventory });
  }

  if (fromContainer > 0 && sourceContainerId) {
    effects.push({
      type: "transferItem",
      itemId,
      amount: fromContainer,
      sourceId: sourceContainerId,
      destinationId: context.actor.id
    });
    effects.push({ type: "removeInventory", itemId, amount: fromContainer });
  }

  return effects;
}

function craftFromRecipe(recipeId: string, context: VerbContext): VerbResult {
  const recipe = CRAFT_RECIPES[recipeId];
  if (!recipe) {
    return { effects: [{ type: "message", key: "craft.unknown", args: { recipe: recipeId } }] };
  }

  for (const [itemId, amount] of Object.entries(recipe.costs)) {
    const available = availableCraftMaterials(context, itemId, recipe.sourceContainerId);
    if (available.inventory + available.container < amount) {
      return {
        effects: [{ type: "message", key: "craft.insufficient", args: { item: itemId, amount: String(amount) } }]
      };
    }
  }

  const effects: ScriptEffect[] = [];
  for (const [itemId, amount] of Object.entries(recipe.costs)) {
    effects.push(...consumeCraftMaterials(context, itemId, amount, recipe.sourceContainerId));
  }

  if (recipe.output) {
    effects.push({ type: "addInventory", itemId: recipe.output.itemId, amount: recipe.output.amount });
  } else if (recipe.placement) {
    effects.push({
      type: "createObject",
      locationId: context.actor.locationId,
      ...recipe.placement
    });
  }

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

function isContainerLocked(container: { properties: Record<string, GameValue> }): boolean {
  const locked = container.properties.locked;
  return locked === true || Number(locked ?? 0) > 0;
}

function actorCanAccessLockedContainer(context: VerbContext, container: { properties: Record<string, GameValue> }): boolean {
  const keyItemId = String(container.properties.keyItemId ?? "");
  if (!keyItemId) {
    return false;
  }
  return (context.actor.inventory[keyItemId] ?? 0) >= 1;
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
    if (isContainerLocked(context.this) && !actorCanAccessLockedContainer(context, context.this)) {
      return { effects: [{ type: "message", key: "container.locked", args: {} }] };
    }

    const capacity = Number(context.this.properties.capacity ?? 0);
    const used = Object.values(context.this.storedItems).reduce((sum, quantity) => sum + quantity, 0);
    const capacityArgs =
      capacity > 0 ? { used: String(used), capacity: String(capacity) } : {};

    const entries = Object.entries(context.this.storedItems);
    if (entries.length === 0) {
      return {
        effects: [{
          type: "message",
          key: capacity > 0 ? "container.open.empty_with_capacity" : "container.open.empty",
          args: capacityArgs
        }]
      };
    }
    return {
      effects: [{
        type: "message",
        key: capacity > 0 ? "container.open.items_with_capacity" : "container.open.items",
        args: { items: context.this.storedItems, ...capacityArgs }
      }]
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

class CreatureBehavior extends ActiveEntityBehavior {
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

  protected override defaultRootGoal(): string {
    return "creatureLife";
  }

  protected override activateRoot(context: TickContext, state: ActiveEntityState): ActiveGoalFrame[] {
    const selected = chooseActiveWeighted(state, [
      { value: "wait", weight: 25 },
      { value: "graze", weight: 10 },
      { value: "wander", weight: 65 }
    ]) ?? "wait";
    state.memory.lastChoice = selected;
    if (selected === "graze") {
      return [createActiveGoal(state, context, "wait", 2, { activity: "graze" })];
    }
    return [createActiveGoal(state, context, selected, selected === "wait" ? 1 : 2)];
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

  talk(context: VerbContext): VerbResult {
    const greetingKey = String(context.this.properties.greetingKey ?? "creature.talk.generic");
    return { effects: [{ type: "message", key: greetingKey, args: {} }] };
  }
}

const VILLAGE_CRATE_ID = "village-crate";
const VILLAGE_CRATE_CAPACITY = 4;

function containerUsedInRoom(context: TickContext, containerId: string): number {
  const items = context.room.containerStorage[containerId] ?? {};
  return Object.values(items).reduce((sum, quantity) => sum + quantity, 0);
}

class FarmerCreatureBehavior extends HumanoidCreatureBehavior {
  protected override defaultRootGoal(): string {
    return "farmerLife";
  }

  protected override activateRoot(context: TickContext, state: ActiveEntityState): ActiveGoalFrame[] {
    const selected = chooseActiveWeighted(state, [
      { value: "idle", weight: 20 },
      { value: "work", weight: 55 },
      { value: "rest", weight: 25 }
    ]) ?? "idle";

    state.memory.activity = selected;

    if (selected === "work") {
      return [createActiveGoal(state, context, "work", 2, { targetContainer: VILLAGE_CRATE_ID })];
    }

    if (selected === "rest") {
      return [createActiveGoal(state, context, "rest", 2)];
    }

    return [createActiveGoal(state, context, "wait", 1)];
  }

  protected override updateGoal(context: TickContext, state: ActiveEntityState, frame: ActiveGoalFrame): ActiveGoalUpdate {
    if (frame.kind === "work") {
      if (context.tick.index >= frame.deadlineTick) {
        if (!crateHasSpace(context)) {
          state.memory.lastWork = "full";
          return {
            status: "success",
            effects: [{ type: "replaceValue", path: ["activity"], value: "idle" }]
          };
        }

        const stockedItem =
          chooseActiveWeighted(state, [
            { value: "wood", weight: 60 },
            { value: "berries", weight: 40 }
          ]) ?? "wood";

        return {
          status: "success",
          effects: [
            { type: "addInventory", itemId: stockedItem, amount: 1, objectId: VILLAGE_CRATE_ID },
            { type: "replaceValue", path: ["activity"], value: "idle" }
          ]
        };
      }

      return {
        status: "continue",
        frame,
        effects: [{ type: "replaceValue", path: ["activity"], value: "working" }]
      };
    }

    if (frame.kind === "rest") {
      if (context.tick.index >= frame.deadlineTick) {
        return {
          status: "success",
          effects: [{ type: "replaceValue", path: ["activity"], value: "idle" }]
        };
      }

      return {
        status: "continue",
        frame,
        effects: [{ type: "replaceValue", path: ["activity"], value: "resting" }]
      };
    }

    return super.updateGoal(context, state, frame);
  }

  override talk(context: VerbContext): VerbResult {
    const activity = String(context.this.properties.activity ?? "idle");

    if (activity === "working") {
      const rawAi = context.this.properties.ai;
      const ai =
        rawAi && !Array.isArray(rawAi) && typeof rawAi === "object"
          ? rawAi as Record<string, GameValue>
          : {};
      const resetAi = {
        rootGoal: typeof ai.rootGoal === "string" ? ai.rootGoal : "farmerLife",
        stack: [],
        memory: { ...(typeof ai.memory === "object" && !Array.isArray(ai.memory) ? ai.memory as Record<string, GameValue> : {}), interruptedWork: true },
        rngState: typeof ai.rngState === "number" ? ai.rngState : 1,
        nextGoalId: typeof ai.nextGoalId === "number" ? ai.nextGoalId : 1
      };

      return {
        effects: [
          { type: "replaceValue", path: ["activity"], value: "idle" },
          { type: "replaceValue", path: ["ai"], value: resetAi as unknown as GameValue },
          { type: "message", key: "creature.village-farmer.talk.interrupted", args: {} }
        ]
      };
    }

    const key =
      activity === "resting"
        ? "creature.village-farmer.talk.resting"
        : "creature.village-farmer.greeting";

    return { effects: [{ type: "message", key, args: {} }] };
  }

  override examine(context: VerbContext): VerbResult {
    const activity = String(context.this.properties.activity ?? "idle");
    const key =
      activity === "working"
        ? "creature.village-farmer.working"
        : activity === "resting"
          ? "creature.village-farmer.resting"
          : context.this.descriptionKey;

    return { effects: [{ type: "message", key, args: {} }] };
  }
}

function crateHasSpace(context: TickContext): boolean {
  return containerUsedInRoom(context, VILLAGE_CRATE_ID) < VILLAGE_CRATE_CAPACITY;
}

const thingBehaviorClasses = {
  ThingBehavior,
  PlaceableBehavior,
  ContainerBehavior,
  WorkbenchBehavior,
  CreatureBehavior,
  HumanoidCreatureBehavior,
  FarmerCreatureBehavior
};
