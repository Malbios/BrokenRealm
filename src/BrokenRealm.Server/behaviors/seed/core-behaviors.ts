type CommandDefinition = {
  methodName: string;
  patterns: { culture: "en" | "de"; pattern: string }[];
};

class GameBehavior {
  static commands: CommandDefinition[] = [];
}

const coreBehaviorClasses = { GameBehavior };