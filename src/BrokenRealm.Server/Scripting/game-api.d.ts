declare type ScriptEffect =
  | { type: "addInventory"; itemId: string; amount: number; objectId?: string }
  | { type: "removeInventory"; itemId: string; amount: number; objectId?: string }
  | { type: "transferItem"; itemId: string; amount: number; destinationId: string; sourceId?: string }
  | { type: "createObject"; locationId: string; nameKey: string; descriptionKey?: string; behaviorModuleId: string; behaviorClassName: string; tags: string; aliasesEn?: string; aliasesDe?: string; properties?: Record<string, GameValue> }
  | { type: "growRoomExit"; direction: string; reverseDirection: string; nameKey: string; descriptionKey?: string; behaviorModuleId: string; behaviorClassName: string; tags: string; aliasesEn?: string; aliasesDe?: string; properties?: Record<string, GameValue> }
  | { type: "destroyObject"; objectId?: string }
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
  properties: Record<string, GameValue>;
}

declare interface TickContext {
  tick: {
    index: number;
    seconds: number;
  };
  this: VerbObjectSummary & {
    properties: Record<string, GameValue>;
    references: Record<string, string>;
    contents: VerbObjectSummary[];
    storedItems: Record<string, number>;
    containerStorage: Record<string, Record<string, number>>;
  };
  room: {
    id: string;
    properties: Record<string, GameValue>;
    references: Record<string, string>;
    contents: VerbObjectSummary[];
    floorItems: Record<string, number>;
    containerStorage: Record<string, Record<string, number>>;
    connectedPlayers: VerbObjectSummary[];
  };
}

declare type ActiveGoalStatus = "continue" | "success" | "failure";

declare interface ActiveGoalFrame {
  id: string;
  kind: string;
  parentId: string | null;
  enteredTick: number;
  deadlineTick: number;
  parameters: Record<string, GameValue>;
}

declare interface ActiveEntityState {
  rootGoal: string;
  stack: ActiveGoalFrame[];
  memory: Record<string, GameValue>;
  rngState: number;
  nextGoalId: number;
}

declare interface VerbContext {
  args: Record<string, string>;
  this: VerbObjectSummary & {
    properties: Record<string, GameValue>;
    references: Record<string, string>;
    contents: VerbObjectSummary[];
    storedItems: Record<string, number>;
    containerStorage: Record<string, Record<string, number>>;
  };
  actor: VerbObjectSummary & {
    properties: Record<string, GameValue>;
    references: Record<string, string>;
    locationReferences: Record<string, string>;
    inventory: Record<string, number>;
    locationId: string;
    locationContents: VerbObjectSummary[];
    floorItems: Record<string, number>;
    containerStorage: Record<string, Record<string, number>>;
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
    locationReferences: Record<string, string>;
    inventory: Record<string, number>;
    locationId: string;
    locationContents: VerbObjectSummary[];
    floorItems: Record<string, number>;
  };
}

declare interface Gatherable {
  gather(context: VerbContext): VerbResult;
}

declare interface Placeable {
  use(context: VerbContext): VerbResult;
}

declare interface Container {
  open(context: VerbContext): VerbResult;
}

declare function execute(context: VerbContext): VerbResult;
