class VillageBehavior extends LocationBehavior {
  override look(context: VerbContext): VerbResult {
    const parent = super.look(context);
    const seatingCount = context.this.contents.filter(object => object.tags.includes("seating")).length;
    const effects = [...parent.effects];
    if (seatingCount > 0) {
      effects.push({ type: "message", key: "location.village.has_seating", args: { count: String(seatingCount) } });
    }
    if (Number(context.this.properties.comfort ?? 0) > 0) {
      effects.push({ type: "message", key: "location.village.comfortable", args: {} });
    }
    return { effects };
  }

  override tick(context: VerbContext): VerbResult {
    const seatingCount = context.this.contents.filter(object => object.tags.includes("seating")).length;
    const comfort = seatingCount > 0 ? 1 : 0;
    const current = Number(context.this.properties.comfort ?? 0);
    if (current === comfort) {
      return { effects: [] };
    }
    return { effects: [{ type: "replaceValue", path: ["comfort"], value: comfort }] };
  }
}

const villageBehaviorClasses = { VillageBehavior };