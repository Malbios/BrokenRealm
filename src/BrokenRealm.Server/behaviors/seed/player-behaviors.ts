function findLocationContent(context: VerbContext, objectId: string) {
  return context.actor.locationContents.find(object => object.id === objectId) ?? null;
}

function parseRequestedAmount(rawAmount: string | undefined): number | null {
  if (!rawAmount) {
    return 1;
  }
  const requested = Number.parseInt(rawAmount, 10);
  if (!Number.isFinite(requested) || requested < 1 || requested > 100) {
    return null;
  }
  return requested;
}

function findContainer(context: VerbContext, objectId: string) {
  const object = findLocationContent(context, objectId);
  if (!object || !object.tags.includes("container")) {
    return null;
  }
  return object;
}

function isContainerLocked(container: { properties: Record<string, GameValue> }): boolean {
  const locked = container.properties.locked;
  return locked === true || Number(locked ?? 0) > 0;
}

function actorCanAccessLockedContainer(context: VerbContext, container: { properties: Record<string, GameValue> }): boolean {
  const keyItemId = String(container.properties.keyItemId ?? "");
  if (!keyItemId) {
    return false;
  }
  return (context.actor.inventory[keyItemId] ?? 0) >= 1;
}

function lockedContainerMessage(context: VerbContext, container: { properties: Record<string, GameValue> }): VerbResult {
  return { effects: [{ type: "message", key: "container.locked", args: {} }] };
}

class PlayerBehavior extends GameBehavior {
  static override commands: CommandDefinition[] = [
    ...super.commands,
    {
      methodName: "inventory",
      patterns: [
        { culture: "en", pattern: "inventory" },
        { culture: "en", pattern: "inv" },
        { culture: "de", pattern: "inventar" },
        { culture: "de", pattern: "inv" }
      ]
    },
    {
      methodName: "drop",
      patterns: [
        { culture: "en", pattern: "drop {amount} {item}" },
        { culture: "en", pattern: "drop {item}" },
        { culture: "de", pattern: "lege {amount} {item} ab" },
        { culture: "de", pattern: "lege {item} ab" }
      ]
    },
    {
      methodName: "give",
      patterns: [
        { culture: "en", pattern: "give {amount} {item} to {player}" },
        { culture: "en", pattern: "give {item} to {player}" },
        { culture: "de", pattern: "gib {amount} {item} an {player}" },
        { culture: "de", pattern: "gib {item} an {player}" }
      ]
    },
    {
      methodName: "take",
      patterns: [
        { culture: "en", pattern: "take {amount} {item} from {object}" },
        { culture: "en", pattern: "take {item} from {object}" },
        { culture: "en", pattern: "take {amount} {item}" },
        { culture: "en", pattern: "pick up {amount} {item}" },
        { culture: "en", pattern: "take {item}" },
        { culture: "en", pattern: "pick up {item}" },
        { culture: "de", pattern: "nimm {amount} {item} aus {object}" },
        { culture: "de", pattern: "nimm {item} aus {object}" },
        { culture: "de", pattern: "nimm {amount} {item}" },
        { culture: "de", pattern: "hebe {amount} {item} auf" },
        { culture: "de", pattern: "nimm {item}" },
        { culture: "de", pattern: "hebe {item} auf" }
      ]
    },
    {
      methodName: "put",
      patterns: [
        { culture: "en", pattern: "put {amount} {item} in {object}" },
        { culture: "en", pattern: "put {item} in {object}" },
        { culture: "en", pattern: "place {amount} {item} in {object}" },
        { culture: "en", pattern: "place {item} in {object}" },
        { culture: "de", pattern: "lege {amount} {item} in {object}" },
        { culture: "de", pattern: "lege {item} in {object}" },
        { culture: "de", pattern: "stecke {amount} {item} in {object}" },
        { culture: "de", pattern: "stecke {item} in {object}" }
      ]
    },
    {
      methodName: "say",
      patterns: [
        { culture: "en", pattern: "say {text}" },
        { culture: "en", pattern: "say" },
        { culture: "de", pattern: "sag {text}" },
        { culture: "de", pattern: "sag" },
        { culture: "de", pattern: "sage {text}" },
        { culture: "de", pattern: "sage" }
      ]
    },
    {
      methodName: "emote",
      patterns: [
        { culture: "en", pattern: "emote {text}" },
        { culture: "en", pattern: "emote" },
        { culture: "en", pattern: ": {text}" },
        { culture: "de", pattern: "emote {text}" },
        { culture: "de", pattern: "emote" },
        { culture: "de", pattern: "* {text}" }
      ]
    },
    {
      methodName: "push",
      patterns: [
        { culture: "en", pattern: "push {object} {direction}" },
        { culture: "de", pattern: "schiebe {object} nach {direction}" }
      ]
    },
    {
      methodName: "moveObject",
      patterns: [
        { culture: "en", pattern: "move {object} to {destination}" },
        { culture: "de", pattern: "verschiebe {object} nach {destination}" }
      ]
    },
    {
      methodName: "map",
      patterns: [
        { culture: "en", pattern: "map" },
        { culture: "de", pattern: "karte" }
      ]
    },
    {
      methodName: "eat",
      patterns: [
        { culture: "en", pattern: "eat {item}" },
        { culture: "de", pattern: "iss {item}" },
        { culture: "de", pattern: "esse {item}" }
      ]
    },
    {
      methodName: "help",
      patterns: [
        { culture: "en", pattern: "help" },
        { culture: "en", pattern: "help {cmd}" },
        { culture: "en", pattern: "h" },
        { culture: "en", pattern: "?" },
        { culture: "de", pattern: "hilfe" },
        { culture: "de", pattern: "hilfe {cmd}" },
        { culture: "de", pattern: "h" },
        { culture: "de", pattern: "?" }
      ]
    }
  ];

  private static readonly helpTopics: Record<string, string> = {
    look: "help.topic.look",
    l: "help.topic.look",
    schau: "help.topic.look",
    umsehen: "help.topic.look",
    go: "help.topic.go",
    walk: "help.topic.go",
    move: "help.topic.go",
    gehe: "help.topic.go",
    geh: "help.topic.go",
    map: "help.topic.map",
    karte: "help.topic.map",
    gather: "help.topic.gather",
    collect: "help.topic.gather",
    sammle: "help.topic.gather",
    sammeln: "help.topic.gather",
    build: "help.topic.build",
    baue: "help.topic.build",
    trail: "help.topic.trail",
    rename: "help.topic.trail",
    nenne: "help.topic.trail",
    pfad: "help.topic.trail",
    inventory: "help.topic.inventory",
    inv: "help.topic.inventory",
    inventar: "help.topic.inventory",
    take: "help.topic.take",
    nimm: "help.topic.take",
    hebe: "help.topic.take",
    drop: "help.topic.drop",
    ab: "help.topic.drop",
    put: "help.topic.put",
    place: "help.topic.put",
    stecke: "help.topic.put",
    give: "help.topic.give",
    gib: "help.topic.give",
    eat: "help.topic.eat",
    iss: "help.topic.eat",
    esse: "help.topic.eat",
    examine: "help.topic.examine",
    x: "help.topic.examine",
    untersuche: "help.topic.examine",
    betrachte: "help.topic.examine",
    open: "help.topic.open",
    öffne: "help.topic.open",
    oeffne: "help.topic.open",
    use: "help.topic.use",
    benutze: "help.topic.use",
    sit: "help.topic.use",
    craft: "help.topic.craft",
    make: "help.topic.craft",
    fertige: "help.topic.craft",
    dismantle: "help.topic.dismantle",
    zerlege: "help.topic.dismantle",
    push: "help.topic.push",
    schiebe: "help.topic.push",
    relocate: "help.topic.relocate",
    verschiebe: "help.topic.relocate",
    say: "help.topic.say",
    sag: "help.topic.say",
    sage: "help.topic.say",
    emote: "help.topic.emote",
    talk: "help.topic.talk",
    sprich: "help.topic.talk",
    rede: "help.topic.talk",
    help: "help.topic.help",
    hilfe: "help.topic.help",
    h: "help.topic.help",
    "?": "help.topic.help"
  };

  private static readonly helpMessageKeys = [
    "help.title",
    "help.section.movement",
    "help.section.places",
    "help.section.inventory",
    "help.section.objects",
    "help.section.social",
    "help.section.meta"
  ];

  private static readonly edibleItems: Record<string, number> = {
    berries: 35
  };

  override tick(context: TickContext): VerbResult {
    const hunger = Number(context.this.properties.hunger ?? 0);
    const nextHunger = Math.min(100, hunger + 1);
    if (nextHunger === hunger) {
      return { effects: [] };
    }
    return { effects: [{ type: "replaceValue", path: ["hunger"], value: nextHunger }] };
  }

  inventory(context: VerbContext): VerbResult {
    const entries = Object.entries(context.actor.inventory);
    const effects: ScriptEffect[] = entries.length === 0
      ? [{ type: "message", key: "inventory.empty", args: {} }]
      : [{ type: "message", key: "inventory.list", args: { items: context.actor.inventory } }];
    const hunger = Number(context.actor.properties.hunger ?? 0);
    if (hunger >= 80) {
      effects.push({ type: "message", key: "player.starving", args: {} });
    } else if (hunger >= 50) {
      effects.push({ type: "message", key: "player.hungry", args: {} });
    }
    return { effects };
  }

  eat(context: VerbContext): VerbResult {
    const itemId = context.args.item;
    const restore = PlayerBehavior.edibleItems[itemId];
    if (!restore) {
      return { effects: [{ type: "message", key: "eat.not_edible", args: { item: itemId } }] };
    }

    const available = context.actor.inventory[itemId] ?? 0;
    if (available < 1) {
      return { effects: [{ type: "message", key: "eat.none", args: { item: itemId } }] };
    }

    const hunger = Number(context.actor.properties.hunger ?? 0);
    const nextHunger = Math.max(0, hunger - restore);

    return {
      effects: [
        { type: "removeInventory", itemId, amount: 1 },
        { type: "replaceValue", path: ["hunger"], value: nextHunger },
        { type: "message", key: "eat.success", args: { item: itemId } }
      ]
    };
  }

  drop(context: VerbContext): VerbResult {
    const itemId = context.args.item;
    const available = context.actor.inventory[itemId] ?? 0;
    const requested = parseRequestedAmount(context.args.amount);
    if (requested === null) {
      return { effects: [{ type: "message", key: "transfer.invalid_amount", args: {} }] };
    }
    if (available < 1) {
      return { effects: [{ type: "message", key: "drop.none", args: { item: itemId } }] };
    }
    if (available < requested) {
      return { effects: [{ type: "message", key: "drop.insufficient", args: { item: itemId, amount: String(available) } }] };
    }
    return {
      effects: [
        { type: "transferItem", itemId, amount: requested, destinationId: context.actor.locationId },
        { type: "message", key: "drop.success", args: { item: itemId, amount: String(requested) } },
        { type: "message", key: "drop.room", args: { actor: context.actor.id, item: itemId, amount: String(requested) } }
      ]
    };
  }

  give(context: VerbContext): VerbResult {
    const itemId = context.args.item;
    const playerId = context.args.player;
    if (playerId === context.actor.id) {
      return { effects: [{ type: "message", key: "give.self", args: {} }] };
    }
    const available = context.actor.inventory[itemId] ?? 0;
    const requested = parseRequestedAmount(context.args.amount);
    if (requested === null) {
      return { effects: [{ type: "message", key: "transfer.invalid_amount", args: {} }] };
    }
    if (available < 1) {
      return { effects: [{ type: "message", key: "give.none", args: { item: itemId } }] };
    }
    if (available < requested) {
      return { effects: [{ type: "message", key: "give.insufficient", args: { item: itemId, amount: String(available) } }] };
    }
    const recipient = context.actor.locationContents.find(object => object.id === playerId);
    if (!recipient || !recipient.tags.includes("player")) {
      return { effects: [{ type: "message", key: "give.not_here", args: {} }] };
    }
    return {
      effects: [
        { type: "transferItem", itemId, amount: requested, destinationId: playerId },
        { type: "message", key: "give.success", args: { item: itemId, player: playerId, amount: String(requested) } },
        { type: "message", key: "give.room", args: { actor: context.actor.id, item: itemId, player: playerId, amount: String(requested) } }
      ]
    };
  }

  take(context: VerbContext): VerbResult {
    const itemId = context.args.item;
    const requested = parseRequestedAmount(context.args.amount);
    if (requested === null) {
      return { effects: [{ type: "message", key: "transfer.invalid_amount", args: {} }] };
    }

    const containerId = context.args.object;
    if (containerId) {
      const container = findContainer(context, containerId);
      if (!container) {
        return { effects: [{ type: "message", key: "container.not_here", args: {} }] };
      }
      if (isContainerLocked(container) && !actorCanAccessLockedContainer(context, container)) {
        return lockedContainerMessage(context, container);
      }
      const available = context.actor.containerStorage[containerId]?.[itemId] ?? 0;
      if (available < 1) {
        return { effects: [{ type: "message", key: "container.take.none", args: { item: itemId } }] };
      }
      if (available < requested) {
        return {
          effects: [{ type: "message", key: "container.take.insufficient", args: { item: itemId, amount: String(available) } }]
        };
      }
      return {
        effects: [
          {
            type: "transferItem",
            itemId,
            amount: requested,
            sourceId: containerId,
            destinationId: context.actor.id
          },
          { type: "message", key: "container.take.success", args: { item: itemId, amount: String(requested), object: containerId } },
          { type: "message", key: "container.take.room", args: { actor: context.actor.id, item: itemId, amount: String(requested), object: containerId } }
        ]
      };
    }

    const available = context.actor.floorItems[itemId] ?? 0;
    if (available < 1) {
      return { effects: [{ type: "message", key: "take.none", args: { item: itemId } }] };
    }
    if (available < requested) {
      return { effects: [{ type: "message", key: "take.insufficient", args: { item: itemId, amount: String(available) } }] };
    }
    return {
      effects: [
        {
          type: "transferItem",
          itemId,
          amount: requested,
          sourceId: context.actor.locationId,
          destinationId: context.actor.id
        },
        { type: "message", key: "take.success", args: { item: itemId, amount: String(requested) } },
        { type: "message", key: "take.room", args: { actor: context.actor.id, item: itemId, amount: String(requested) } }
      ]
    };
  }

  put(context: VerbContext): VerbResult {
    const itemId = context.args.item;
    const containerId = context.args.object;
    const container = findContainer(context, containerId);
    if (!container) {
      return { effects: [{ type: "message", key: "container.not_here", args: {} }] };
    }
    if (isContainerLocked(container) && !actorCanAccessLockedContainer(context, container)) {
      return lockedContainerMessage(context, container);
    }
    const available = context.actor.inventory[itemId] ?? 0;
    const requested = parseRequestedAmount(context.args.amount);
    if (requested === null) {
      return { effects: [{ type: "message", key: "transfer.invalid_amount", args: {} }] };
    }
    if (available < 1) {
      return { effects: [{ type: "message", key: "put.none", args: { item: itemId } }] };
    }
    if (available < requested) {
      return { effects: [{ type: "message", key: "put.insufficient", args: { item: itemId, amount: String(available) } }] };
    }
    return {
      effects: [
        { type: "transferItem", itemId, amount: requested, destinationId: containerId },
        { type: "message", key: "put.success", args: { item: itemId, amount: String(requested), object: containerId } },
        { type: "message", key: "put.room", args: { actor: context.actor.id, item: itemId, amount: String(requested), object: containerId } }
      ]
    };
  }

  say(context: VerbContext): VerbResult {
    const text = (context.args.text ?? "").trim();
    if (!text) {
      return { effects: [{ type: "message", key: "say.empty", args: {} }] };
    }
    return {
      effects: [
        { type: "message", key: "say.self", args: { text } },
        { type: "message", key: "say.room", args: { actor: context.actor.id, text } }
      ]
    };
  }

  emote(context: VerbContext): VerbResult {
    const text = (context.args.text ?? "").trim();
    if (!text) {
      return { effects: [{ type: "message", key: "emote.empty", args: {} }] };
    }
    return {
      effects: [
        { type: "message", key: "emote.self", args: { text } },
        { type: "message", key: "emote.room", args: { actor: context.actor.id, text } }
      ]
    };
  }

  push(context: VerbContext): VerbResult {
    const object = findLocationContent(context, context.args.object);
    if (!object || !object.tags.includes("placeable")) {
      return { effects: [{ type: "message", key: "move_object.not_here", args: {} }] };
    }
    const destinationId = context.actor.locationReferences[context.args.direction];
    if (!destinationId) {
      return { effects: [{ type: "message", key: "move.no_exit", args: {} }] };
    }
    return {
      effects: [
        { type: "moveObject", objectId: object.id, destinationId },
        { type: "message", key: "push_object.success", args: { object: object.id, direction: context.args.direction } }
      ]
    };
  }

  moveObject(context: VerbContext): VerbResult {
    const object = findLocationContent(context, context.args.object);
    if (!object || object.tags.includes("carried")) {
      return { effects: [{ type: "message", key: "move_object.not_here", args: {} }] };
    }
    return {
      effects: [
        { type: "moveObject", objectId: object.id, destinationId: context.args.destination },
        { type: "message", key: "move_object.success", args: { object: object.id, destination: context.args.destination } }
      ]
    };
  }

  map(context: VerbContext): VerbResult {
    return {
      effects: [{ type: "message", key: "map.display", args: { actor: context.actor.id } }]
    };
  }

  help(context: VerbContext): VerbResult {
    const raw = context.args.cmd?.trim();
    if (!raw) {
      return {
        effects: PlayerBehavior.helpMessageKeys.map(key => ({ type: "message", key, args: {} }))
      };
    }

    const topicKey = PlayerBehavior.helpTopics[raw.toLowerCase()];
    if (!topicKey) {
      return { effects: [{ type: "message", key: "help.unknown", args: { cmd: raw } }] };
    }

    return { effects: [{ type: "message", key: topicKey, args: {} }] };
  }
}

const playerBehaviorClasses = { PlayerBehavior };