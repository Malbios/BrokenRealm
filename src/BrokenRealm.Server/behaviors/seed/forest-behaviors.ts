class ForestBehavior extends LocationBehavior implements Gatherable {
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
    const description = parent.effects[0];
    const hungerEffects = parent.effects.filter(
      effect => effect.type === "message" && (effect.key === "player.hungry" || effect.key === "player.starving")
    );
    const remainder = parent.effects.slice(1).filter(effect => !hungerEffects.includes(effect));
    const effects: ScriptEffect[] = [
      description,
      { type: "message", key: "location.forest.atmosphere", args: {} }
    ];

    if (Number(context.this.properties.berryYield ?? 0) > 0) {
      effects.push({ type: "message", key: "location.forest.berries", args: {} });
    }

    return {
      effects: [...effects, ...hungerEffects, ...remainder]
    };
  }

  override tick(context: TickContext): VerbResult {
    const current = Number(context.this.properties.tickCount ?? 0);
    const effects: ScriptEffect[] = [{ type: "replaceValue", path: ["tickCount"], value: current + 1 }];
    const haresHere = countTaggedNpcContents(context, "herbivore");

    if (haresHere === 0) {
      effects.push({
        type: "createObject",
        locationId: context.this.id,
        nameKey: "object.forest-hare.name",
        descriptionKey: "object.forest-hare.description",
        behaviorModuleId: "thing-behaviors",
        behaviorClassName: "CreatureBehavior",
        tags: "creature,thing,herbivore",
        aliasesEn: "hare,forest hare",
        aliasesDe: "hase,waldhase",
        properties: {
          tickSteps: 0,
          ai: {
            rootGoal: "hareLife",
            stack: [],
            memory: {},
            rngState: 1,
            nextGoalId: 1
          }
        }
      });
    }

    const woodCap = Number(context.this.properties.woodCap ?? 10);
    const woodYield = Number(context.this.properties.woodYield ?? 0);
    const regrownWood = Math.min(woodCap, woodYield + 1);
    effects.push(...syncPropertyIfChanged(context, "woodYield", regrownWood));

    const berryCap = Number(context.this.properties.berryCap ?? 8);
    const berryYield = Number(context.this.properties.berryYield ?? 0);
    const regrownBerries = Math.min(berryCap, berryYield + 1);
    effects.push(...syncPropertyIfChanged(context, "berryYield", regrownBerries));

    return { effects };
  }

  gather(context: VerbContext): VerbResult {
    const item = context.args.item;

    if (item === "wood") {
      if (!context.this.tags.includes("wood")) {
        return { effects: [{ type: "message", key: "gather.no_wood_here", args: {} }] };
      }
      const amount = 2;
      const woodYield = Number(context.this.properties.woodYield ?? 0);
      if (woodYield < amount) {
        return { effects: [{ type: "message", key: "gather.depleted", args: { item: "wood" } }] };
      }
      return {
        effects: [
          { type: "addInventory", itemId: "wood", amount },
          { type: "replaceValue", path: ["woodYield"], value: woodYield - amount },
          { type: "message", key: "gather.wood.success", args: { amount, item: "wood" } }
        ]
      };
    }

    if (item === "berries") {
      if (!context.this.tags.includes("forage")) {
        return { effects: [{ type: "message", key: "gather.no_berries_here", args: {} }] };
      }
      const amount = 3;
      const berryYield = Number(context.this.properties.berryYield ?? 0);
      if (berryYield < amount) {
        return { effects: [{ type: "message", key: "gather.depleted", args: { item: "berries" } }] };
      }
      return {
        effects: [
          { type: "addInventory", itemId: "berries", amount },
          { type: "replaceValue", path: ["berryYield"], value: berryYield - amount },
          { type: "message", key: "gather.berries.success", args: { amount, item: "berries" } }
        ]
      };
    }

    return { effects: [{ type: "message", key: "gather.unknown_item", args: { item } }] };
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

const forestBehaviorClasses = { ForestBehavior };
