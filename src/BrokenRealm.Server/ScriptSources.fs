namespace BrokenRealm.Server

module ScriptSources =
    let look =
        """function execute(context: VerbContext): VerbResult {
  return {
    effects: [
      { type: "message", key: context.this.descriptionKey, args: {} }
    ]
  };
}"""

    let lookCompiled =
        """function execute(context) {
  return {
    effects: [
      { type: "message", key: context.this.descriptionKey, args: {} }
    ]
  };
}"""

    let gather =
        """function execute(context: VerbContext): VerbResult {
  const item = context.args.item;

  if (item !== "wood" || !context.this.tags.includes("wood")) {
    return {
      effects: [
        { type: "message", key: "gather.no_wood_here", args: {} }
      ]
    };
  }

  const amount = 2;
  return {
    effects: [
      { type: "addInventory", itemId: "wood", amount },
      {
        type: "message",
        key: "gather.wood.success",
        args: { amount, item: "wood" }
      }
    ]
  };
}"""

    let gatherCompiled =
        """function execute(context) {
  const item = context.args.item;

  if (item !== "wood" || !context.this.tags.includes("wood")) {
    return {
      effects: [
        { type: "message", key: "gather.no_wood_here", args: {} }
      ]
    };
  }

  const amount = 2;
  return {
    effects: [
      { type: "addInventory", itemId: "wood", amount },
      {
        type: "message",
        key: "gather.wood.success",
        args: { amount, item: "wood" }
      }
    ]
  };
}"""

    let inventory =
        """function execute(context: VerbContext): VerbResult {
  const entries = Object.entries(context.actor.inventory);

  if (entries.length === 0) {
    return {
      effects: [
        { type: "message", key: "inventory.empty", args: {} }
      ]
    };
  }

  return {
    effects: [
      { type: "message", key: "inventory.list", args: { items: context.actor.inventory } }
    ]
  };
}"""

    let inventoryCompiled =
        """function execute(context) {
  const entries = Object.entries(context.actor.inventory);

  if (entries.length === 0) {
    return {
      effects: [
        { type: "message", key: "inventory.empty", args: {} }
      ]
    };
  }

  return {
    effects: [
      { type: "message", key: "inventory.list", args: { items: context.actor.inventory } }
    ]
  };
}"""

    let move =
        """function execute(context: VerbContext): VerbResult {
  const direction = context.args.direction;
  const destinationId = context.this.references[direction];

  if (!destinationId) {
    return {
      effects: [
        { type: "message", key: "move.no_exit", args: {} }
      ]
    };
  }

  return {
    effects: [
      { type: "movePlayer", destinationId },
      { type: "message", key: "move.success", args: { direction } }
    ]
  };
}"""

    let moveCompiled =
        """function execute(context) {
  const direction = context.args.direction;
  const destinationId = context.this.references[direction];

  if (!destinationId) {
    return {
      effects: [
        { type: "message", key: "move.no_exit", args: {} }
      ]
    };
  }

  return {
    effects: [
      { type: "movePlayer", destinationId },
      { type: "message", key: "move.success", args: { direction } }
    ]
  };
}"""
