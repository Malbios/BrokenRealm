type ActiveGoalUpdate = {
  status: ActiveGoalStatus;
  frame?: ActiveGoalFrame;
  effects?: ScriptEffect[];
};

type WeightedActiveChoice<T> = {
  value: T;
  weight: number;
};

type ActiveInterruptResult = {
  consumed: boolean;
  effects?: ScriptEffect[];
  skipGoalUpdate?: boolean;
};

function activeInteger(value: GameValue | undefined, fallback: number): number {
  return typeof value === "number" && Number.isFinite(value) ? Math.trunc(value) : fallback;
}

function activeState(context: TickContext, defaultRootGoal: string): ActiveEntityState | null {
  const raw = context.this.properties.ai;
  if (!raw || Array.isArray(raw) || typeof raw !== "object") {
    return null;
  }
  const value = raw as Record<string, GameValue>;
  const stack = Array.isArray(value.stack) ? value.stack as unknown as ActiveGoalFrame[] : [];
  const memory = value.memory && !Array.isArray(value.memory) && typeof value.memory === "object"
    ? value.memory as Record<string, GameValue>
    : {};
  return {
    rootGoal: typeof value.rootGoal === "string" ? value.rootGoal : defaultRootGoal,
    stack,
    memory,
    rngState: activeInteger(value.rngState, 1) >>> 0,
    nextGoalId: Math.max(1, activeInteger(value.nextGoalId, 1))
  };
}

function nextActiveRandom(state: ActiveEntityState): number {
  state.rngState = (Math.imul(state.rngState, 1664525) + 1013904223) >>> 0;
  return state.rngState / 4294967296;
}

function chooseActiveWeighted<T>(state: ActiveEntityState, choices: WeightedActiveChoice<T>[]): T | null {
  const available = choices.filter(choice => Number.isFinite(choice.weight) && choice.weight > 0);
  const total = available.reduce((sum, choice) => sum + choice.weight, 0);
  if (total <= 0) {
    return null;
  }
  let roll = nextActiveRandom(state) * total;
  for (const choice of available) {
    if (roll < choice.weight) {
      return choice.value;
    }
    roll -= choice.weight;
  }
  return available[available.length - 1].value;
}

function createActiveGoal(
  state: ActiveEntityState,
  context: TickContext,
  kind: string,
  lifetimeTicks: number,
  parameters: Record<string, GameValue> = {},
  parentId: string | null = null
): ActiveGoalFrame {
  const id = `goal-${state.nextGoalId}`;
  state.nextGoalId += 1;
  return {
    id,
    kind,
    parentId,
    enteredTick: context.tick.index,
    deadlineTick: context.tick.index + Math.max(1, Math.trunc(lifetimeTicks)),
    parameters
  };
}

function createWaitGoal(
  state: ActiveEntityState,
  context: TickContext,
  lifetimeTicks: number,
  parameters: Record<string, GameValue> = {}
): ActiveGoalFrame {
  return createActiveGoal(state, context, "wait", lifetimeTicks, parameters);
}

function createWanderGoal(
  state: ActiveEntityState,
  context: TickContext,
  lifetimeTicks = 1,
  parameters: Record<string, GameValue> = {}
): ActiveGoalFrame {
  return createActiveGoal(state, context, "wander", lifetimeTicks, parameters);
}

function createFleeGoal(
  state: ActiveEntityState,
  context: TickContext,
  destinationId: string,
  lifetimeTicks = 1,
  parameters: Record<string, GameValue> = {}
): ActiveGoalFrame {
  return createActiveGoal(state, context, "flee", lifetimeTicks, { ...parameters, destinationId });
}

function pushActiveSequence(state: ActiveEntityState, goalsInExecutionOrder: ActiveGoalFrame[]): void {
  for (let index = goalsInExecutionOrder.length - 1; index >= 0; index -= 1) {
    state.stack.push(goalsInExecutionOrder[index]);
  }
}

function clearActiveStack(state: ActiveEntityState): void {
  state.stack = [];
}

function finishActiveGoal(state: ActiveEntityState, frame: ActiveGoalFrame, status: ActiveGoalStatus): void {
  state.stack.pop();
  if (status !== "failure") {
    return;
  }
  if (frame.parentId === null) {
    state.stack = [];
    return;
  }
  while (state.stack.length > 0 && state.stack[state.stack.length - 1].id !== frame.parentId) {
    state.stack.pop();
  }
}

function updateStandardGoal(context: TickContext, _state: ActiveEntityState, frame: ActiveGoalFrame): ActiveGoalUpdate {
  if (frame.kind === "wait") {
    return context.tick.index >= frame.deadlineTick
      ? { status: "success" }
      : { status: "continue", frame };
  }
  if (frame.kind === "wander") {
    const directions = Object.keys(context.room.references).sort();
    const direction = directions[0];
    const destinationId = direction ? context.room.references[direction] : undefined;
    return destinationId
      ? { status: "success", effects: [{ type: "moveObject", objectId: context.this.id, destinationId }] }
      : { status: "failure" };
  }
  if (frame.kind === "flee") {
    const destinationId = String(frame.parameters.destinationId ?? "");
    return destinationId
      ? { status: "success", effects: [{ type: "moveObject", objectId: context.this.id, destinationId }] }
      : { status: "failure" };
  }
  return { status: "failure" };
}

class ActiveEntityBehavior extends GameBehavior {
  protected defaultRootGoal(): string {
    return "idle";
  }

  protected activateRoot(context: TickContext, state: ActiveEntityState): ActiveGoalFrame[] {
    return [createWaitGoal(state, context, 1)];
  }

  protected handleInterrupt(
    _context: TickContext,
    _state: ActiveEntityState,
    _interrupt: ActiveInterrupt
  ): ActiveInterruptResult {
    return { consumed: false };
  }

  protected processInterrupts(
    context: TickContext,
    state: ActiveEntityState,
    interrupts: ActiveInterrupt[]
  ): ActiveInterruptResult {
    for (const interrupt of interrupts) {
      for (let index = state.stack.length - 1; index >= 0; index -= 1) {
        const frame = state.stack[index];
        const frameResult = this.handleGoalInterrupt(context, state, frame, interrupt);
        if (frameResult.consumed) {
          return frameResult;
        }
      }

      const result = this.handleInterrupt(context, state, interrupt);
      if (result.consumed) {
        return result;
      }
    }

    return { consumed: false };
  }

  protected handleGoalInterrupt(
    _context: TickContext,
    _state: ActiveEntityState,
    _frame: ActiveGoalFrame,
    _interrupt: ActiveInterrupt
  ): ActiveInterruptResult {
    return { consumed: false };
  }

  protected updateGoal(context: TickContext, state: ActiveEntityState, frame: ActiveGoalFrame): ActiveGoalUpdate {
    return updateStandardGoal(context, state, frame);
  }

  override handleInterrupts(context: TickContext): VerbResult {
    const state =
      activeState(context, this.defaultRootGoal()) ?? {
        rootGoal: this.defaultRootGoal(),
        stack: [],
        memory: {},
        rngState: 1,
        nextGoalId: 1
      };

    const interrupts = context.interrupts ?? [];
    if (interrupts.length === 0) {
      return { effects: [] };
    }

    const result = this.processInterrupts(context, state, interrupts);
    if (!result.consumed) {
      return { effects: [] };
    }

    return {
      effects: [
        { type: "replaceValue", path: ["ai"], value: state as unknown as GameValue },
        ...(result.effects ?? [])
      ]
    };
  }

  override tick(context: TickContext): VerbResult {
    const state = activeState(context, this.defaultRootGoal());
    if (state === null) {
      return { effects: [] };
    }

    const lastUpdatedTick = activeInteger(state.memory.lastUpdatedTick, context.tick.index);
    if (context.tick.index < lastUpdatedTick) {
      state.stack = state.stack.map(frame => {
        const remainingTicks = Math.max(1, frame.deadlineTick - lastUpdatedTick);
        return {
          ...frame,
          enteredTick: context.tick.index,
          deadlineTick: context.tick.index + remainingTicks
        };
      });
      state.memory.lastRebasedFromTick = lastUpdatedTick;
    }

    const interrupts = context.interrupts ?? [];
    const actionEffects: ScriptEffect[] = [];
    let skipGoalUpdate = false;

    if (interrupts.length > 0) {
      const interruptResult = this.processInterrupts(context, state, interrupts);
      if (interruptResult.consumed) {
        actionEffects.push(...(interruptResult.effects ?? []));
        skipGoalUpdate = interruptResult.skipGoalUpdate ?? false;
      }
    }

    if (state.stack.length === 0) {
      pushActiveSequence(state, this.activateRoot(context, state));
    }

    const frame = state.stack[state.stack.length - 1];
    if (frame && !skipGoalUpdate) {
      const update = context.tick.index > frame.deadlineTick
        ? { status: "failure" as ActiveGoalStatus }
        : this.updateGoal(context, state, frame);
      if (update.status === "continue" && update.frame) {
        state.stack[state.stack.length - 1] = update.frame;
      } else if (update.status !== "continue") {
        finishActiveGoal(state, frame, update.status);
      }
      actionEffects.push(...(update.effects ?? []));
      state.memory.lastGoal = frame.kind;
      state.memory.lastStatus = update.status;
      state.memory.lastUpdatedTick = context.tick.index;
    } else if (frame) {
      state.memory.lastUpdatedTick = context.tick.index;
    }

    const tickSteps = activeInteger(context.this.properties.tickSteps, 0) + 1;
    return {
      effects: [
        { type: "replaceValue", path: ["ai"], value: state as unknown as GameValue },
        { type: "replaceValue", path: ["tickSteps"], value: tickSteps },
        ...actionEffects
      ]
    };
  }
}

const activeEntityBehaviorClasses = { ActiveEntityBehavior };