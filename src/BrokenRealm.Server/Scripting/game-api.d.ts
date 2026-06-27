declare type ScriptEffect =
  | { type: "addInventory"; itemId: "wood"; amount: number; objectId?: string }
  | { type: "moveObject"; destinationId: string; objectId?: string }
  | { type: "movePlayer"; destinationId: string }
  | { type: "replaceValue"; path: (string | number)[]; value: GameValue }
  | { type: "invokeAnonymous"; path: (string | number)[]; methodName: string; args?: Record<string, string> }
  | { type: "message"; key: string; args?: Record<string, unknown> };

declare type ObjectId = string;
declare type GameValue = null | string | number | boolean | GameValue[] | { [key: string]: GameValue };

declare interface VerbObjectSummary {
  id: string;
  name: string;
  descriptionKey: string;
  tags: string[];
}

declare interface VerbContext {
  args: Record<string, string>;
  this: VerbObjectSummary & {
    properties: Record<string, GameValue>;
    references: Record<string, string>;
    contents: VerbObjectSummary[];
  };
  actor: VerbObjectSummary & {
    properties: Record<string, GameValue>;
    references: Record<string, string>;
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
  actor: VerbObjectSummary & {
    properties: Record<string, GameValue>;
    references: Record<string, string>;
    inventory: Record<string, number>;
  };
}

declare interface Gatherable {
  gather(context: VerbContext): VerbResult;
}

declare function execute(context: VerbContext): VerbResult;