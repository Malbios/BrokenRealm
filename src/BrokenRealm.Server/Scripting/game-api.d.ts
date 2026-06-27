declare type ScriptEffect =
  | { type: "addInventory"; itemId: "wood"; amount: number }
  | { type: "movePlayer"; destinationId: string }
  | { type: "replaceValue"; path: (string | number)[]; value: GameValue }
  | { type: "invokeAnonymous"; path: (string | number)[]; methodName: string; args?: Record<string, string> }
  | { type: "message"; key: string; args?: Record<string, unknown> };

declare type ObjectId = string;
declare type GameValue = null | string | number | boolean | GameValue[] | { [key: string]: GameValue };

declare interface VerbContext {
  args: Record<string, string>;
  this: {
    id: string;
    name: string;
    descriptionKey: string;
    tags: string[];
    properties: Record<string, GameValue>;
    references: Record<string, string>;
    contents: {
      id: ObjectId;
      name: string;
      descriptionKey: string;
      tags: string[];
    }[];
  };
  actor: {
    inventory: Record<string, number>;
  };
}

declare interface VerbResult {
  effects: ScriptEffect[];
}

declare interface AnonymousBehaviorContext {
  args: Record<string, string>;
  this: {
    storagePath: (string | number)[];
    properties: Record<string, GameValue>;
  };
  actor: {
    inventory: Record<string, number>;
  };
}

declare interface Gatherable {
  gather(context: VerbContext): VerbResult;
}

declare function execute(context: VerbContext): VerbResult;
