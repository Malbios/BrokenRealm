function countTaggedContents(context: TickContext, tag: string): number {
  return context.this.contents.filter(object => object.tags.includes(tag)).length;
}

function countNpcCreatures(context: TickContext): number {
  return context.this.contents.filter(
    object => object.tags.includes("creature") && !object.tags.includes("player")
  ).length;
}

function countTaggedNpcContents(context: TickContext, tag: string): number {
  return context.this.contents.filter(
    object => object.tags.includes(tag) && !object.tags.includes("player")
  ).length;
}

function gameValuesEqual(left: GameValue, right: GameValue): boolean {
  if (left === right) {
    return true;
  }

  if (typeof left === "number" && typeof right === "number") {
    return left === right;
  }

  return false;
}

function syncPropertyIfChanged(context: TickContext, key: string, value: GameValue): ScriptEffect[] {
  const current = context.this.properties[key] ?? null;
  if (gameValuesEqual(current, value)) {
    return [];
  }

  return [{ type: "replaceValue", path: [key], value }];
}

function anyContainerStocked(context: TickContext): boolean {
  return Object.values(context.this.containerStorage).some(items =>
    Object.values(items).some(quantity => quantity > 0)
  );
}

function propertyFlag(context: { this: { properties: Record<string, GameValue> } }, key: string): boolean {
  return Number(context.this.properties[key] ?? 0) > 0;
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

  tick(_context: TickContext): VerbResult {
    return { effects: [] };
  }
}

const locationBehaviorClasses = { LocationBehavior };