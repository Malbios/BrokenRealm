function derivedVillageMetrics(context: TickContext) {
  return {
    comfort: countTaggedContents(context, "seating") > 0 ? 1 : 0,
    stocked: anyContainerStocked(context) ? 1 : 0,
    wildlife: countNpcCreatures(context) > 0 ? 1 : 0
  };
}

const REVERSE_DIRECTION: Record<string, string> = {
  north: "south",
  south: "north",
  east: "west",
  west: "east"
};

const CLEARING_BUILD_COST = 4;

function buildClearing(context: VerbContext, direction: string): VerbResult {
  const reverseDirection = REVERSE_DIRECTION[direction];
  if (!reverseDirection) {
    return { effects: [{ type: "message", key: "build.invalid_direction", args: {} }] };
  }
  if (context.this.references[direction]) {
    return { effects: [{ type: "message", key: "build.exit_exists", args: { direction } }] };
  }
  const availableWood = context.actor.inventory.wood ?? 0;
  if (availableWood < CLEARING_BUILD_COST) {
    return {
      effects: [{
        type: "message",
        key: "build.insufficient",
        args: { item: "wood", amount: String(CLEARING_BUILD_COST) }
      }]
    };
  }
  return {
    effects: [
      { type: "removeInventory", itemId: "wood", amount: CLEARING_BUILD_COST },
      {
        type: "growRoomExit",
        direction,
        reverseDirection,
        nameKey: "object.clearing.name",
        descriptionKey: "location.clearing.description",
        behaviorModuleId: "location-behaviors",
        behaviorClassName: "LocationBehavior",
        tags: "clearing,settlement",
        aliasesEn: "clearing",
        aliasesDe: "lichtung"
      },
      { type: "message", key: "build.clearing.success", args: { direction } },
      { type: "message", key: "build.clearing.room", args: { actor: context.actor.id, direction } }
    ]
  };
}

class VillageBehavior extends LocationBehavior {
  static override commands: CommandDefinition[] = [
    ...super.commands,
    {
      methodName: "build",
      patterns: [
        { culture: "en", pattern: "build {structure} {direction}" },
        { culture: "de", pattern: "baue {structure} nach {direction}" }
      ]
    }
  ];

  override look(context: VerbContext): VerbResult {
    const parent = super.look(context);
    const effects = [...parent.effects];

    if (propertyFlag(context, "comfort")) {
      effects.push({ type: "message", key: "location.village.comfortable", args: {} });
    }

    if (propertyFlag(context, "stocked")) {
      effects.push({ type: "message", key: "location.village.stocked", args: {} });
    }

    if (propertyFlag(context, "wildlife")) {
      effects.push({ type: "message", key: "location.village.wildlife", args: {} });
    }

    return { effects };
  }

  override tick(context: TickContext): VerbResult {
    const metrics = derivedVillageMetrics(context);
    const effects = [
      ...syncPropertyIfChanged(context, "comfort", metrics.comfort),
      ...syncPropertyIfChanged(context, "stocked", metrics.stocked),
      ...syncPropertyIfChanged(context, "wildlife", metrics.wildlife)
    ];
    return { effects };
  }

  build(context: VerbContext): VerbResult {
    if (context.args.structure !== "clearing") {
      return { effects: [{ type: "message", key: "build.unknown", args: { structure: context.args.structure } }] };
    }
    return buildClearing(context, context.args.direction);
  }
}

const villageBehaviorClasses = { VillageBehavior };