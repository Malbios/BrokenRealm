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

const forestBehaviorClasses = { ForestBehavior };