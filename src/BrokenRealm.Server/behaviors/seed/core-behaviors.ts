type CommandDefinition = {
  methodName: string;
  patterns: { culture: "en" | "de"; pattern: string }[];
};

class GameBehavior {
  static commands: CommandDefinition[] = [];

  tick(_context: TickContext): VerbResult {
    return { effects: [] };
  }
}

const coreBehaviorClasses = { GameBehavior };