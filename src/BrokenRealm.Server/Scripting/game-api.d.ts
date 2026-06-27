declare type ScriptEffect =
  | { type: "addInventory"; itemId: "wood"; amount: number }
  | { type: "movePlayer"; destinationId: string }
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
  };
  actor: {
    inventory: Record<string, number>;
  };
}

declare interface VerbResult {
  effects: ScriptEffect[];
}

declare interface Gatherable {
  gather(context: VerbContext): VerbResult;
}

declare function execute(context: VerbContext): VerbResult;
