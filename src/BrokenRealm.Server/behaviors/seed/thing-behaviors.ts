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

const thingBehaviorClasses = { ThingBehavior, PlaceableBehavior };