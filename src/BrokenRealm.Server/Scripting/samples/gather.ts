function execute(context: VerbContext): VerbResult {
  const item = context.args.item;

  if (item !== "wood" || !context.this.tags.includes("wood")) {
    return {
      effects: [
        { type: "message", key: "gather.no_wood_here", args: {} },
      ],
    };
  }

  const amount = 2;
  return {
    effects: [
      { type: "addInventory", itemId: "wood", amount },
      {
        type: "message",
        key: "gather.wood.success",
        args: { amount, item: "wood" },
      },
    ],
  };
}
