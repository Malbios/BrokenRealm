namespace BrokenRealm.Server

module BehaviorSources =
    let inheritanceSpike =
        """class GameBehavior {
  look(context: VerbContext): VerbResult {
    return { effects: [] };
  }
}

class LocationBehavior extends GameBehavior {
  override look(context: VerbContext): VerbResult {
    return {
      effects: [
        { type: "message", key: context.this.descriptionKey, args: {} }
      ]
    };
  }
}

class ForestBehavior extends LocationBehavior {
  override look(context: VerbContext): VerbResult {
    const parent = super.look(context);

    return {
      effects: [
        ...parent.effects,
        { type: "message", key: "location.forest.atmosphere", args: {} }
      ]
    };
  }
}"""
