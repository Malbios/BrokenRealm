namespace BrokenRealm.Server

module PlayerObjects =
    [<Literal>]
    let PlayerTag = "player"

    [<Literal>]
    let AccountIdProperty = "accountId"

    [<Literal>]
    let InventoryProperty = "inventory"

    [<Literal>]
    let LastSafeLocationProperty = "lastSafeLocationId"

    [<Literal>]
    let PlayerBehaviorModuleId = "player-behaviors"

    [<Literal>]
    let PlayerBehaviorClassName = "PlayerBehavior"

    let isPlayer (gameObject: GameObject) = gameObject.Tags.Contains PlayerTag

    let legacyInventoryFromProperties (properties: Map<string, GameValue>) =
        match properties |> Map.tryFind InventoryProperty with
        | Some(MapValue values) ->
            values
            |> Map.toList
            |> List.choose (fun (itemId, value) ->
                match value with
                | IntegerValue quantity when quantity >= 0L -> Some(itemId, int quantity)
                | _ -> None)
            |> Map.ofList
        | _ -> Map.empty

    let accountIdFromProperties (properties: Map<string, GameValue>) =
        match properties |> Map.tryFind AccountIdProperty with
        | Some(StringValue accountId) -> Some accountId
        | _ -> None

    let create
        (id: CharacterId)
        (name: string)
        (nameKey: string)
        (accountId: AccountId)
        (locationId: ObjectId)
        =
        { Id = id
          Name = name
          NameKey = nameKey
          Aliases = Map.empty
          DescriptionKey = None
          LocationId = Some locationId
          Tags = Set.ofList [ PlayerTag ]
          Properties = Map.ofList [ AccountIdProperty, StringValue accountId ]
          References = Map.empty
          BehaviorModuleId = PlayerBehaviorModuleId
          BehaviorClassName = PlayerBehaviorClassName }

    let createWithLegacyInventory
        (id: CharacterId)
        (name: string)
        (nameKey: string)
        (accountId: AccountId)
        (locationId: ObjectId)
        (inventory: Map<ItemId, Quantity>)
        =
        let player = create id name nameKey accountId locationId

        if Map.isEmpty inventory then
            player
        else
            { player with
                Properties =
                    player.Properties
                    |> Map.add
                        InventoryProperty
                        (inventory
                         |> Map.map (fun _ quantity -> IntegerValue(int64 quantity))
                         |> MapValue) }

    let tryGet (state: GameState) (characterId: CharacterId) =
        state.Objects
        |> Map.tryFind characterId
        |> Option.bind (fun gameObject -> if isPlayer gameObject then Some gameObject else None)

    let get (state: GameState) (characterId: CharacterId) =
        match tryGet state characterId with
        | Some gameObject -> gameObject
        | None -> failwith $"Unknown player object id: {characterId}"

    let inventory (state: GameState) (playerId: CharacterId) = CarriedItems.inventoryMap state playerId

    let accountId (gameObject: GameObject) =
        accountIdFromProperties gameObject.Properties
        |> Option.defaultWith (fun () -> failwith $"Player object {gameObject.Id} is missing accountId.")

    let tryLocationId (gameObject: GameObject) = gameObject.LocationId

    let lastSafeLocationId (gameObject: GameObject) =
        match gameObject.Properties |> Map.tryFind LastSafeLocationProperty with
        | Some(StringValue locationId) -> Some locationId
        | _ -> None

    let isInLimbo (gameObject: GameObject) = gameObject.LocationId.IsNone

    let locationId (gameObject: GameObject) =
        gameObject.LocationId
        |> Option.defaultWith (fun () -> failwith $"Player object {gameObject.Id} is missing a location.")

    let withLocation (gameObject: GameObject) (locationId: ObjectId) =
        { gameObject with LocationId = Some locationId }

    let withoutLocation (gameObject: GameObject) = { gameObject with LocationId = None }

    let withoutLegacyInventoryProperty (gameObject: GameObject) =
        { gameObject with
            Properties = gameObject.Properties |> Map.remove InventoryProperty }

    let playersByAccount (state: GameState) (accountId: AccountId) =
        state.Objects
        |> Map.toList
        |> List.choose (fun (_, gameObject) ->
            if isPlayer gameObject then
                accountIdFromProperties gameObject.Properties
                |> Option.bind (fun ownerAccountId ->
                    if ownerAccountId = accountId then
                        Some gameObject
                    else
                        None)
            else
                None)
        |> List.sortBy _.Id

    let validatePlayerObject (state: GameState) (gameObject: GameObject) =
        if not (isPlayer gameObject) then
            Ok()
        else
            [ if gameObject.BehaviorModuleId <> PlayerBehaviorModuleId then
                  Some $"Player object {gameObject.Id} must use behavior module {PlayerBehaviorModuleId}."
              else
                  None
              if gameObject.BehaviorClassName <> PlayerBehaviorClassName then
                  Some $"Player object {gameObject.Id} must use behavior class {PlayerBehaviorClassName}."
              else
                  None
              match gameObject.LocationId with
              | None -> None
              | Some locationId when not (state.Objects.ContainsKey locationId) ->
                  Some $"Player object {gameObject.Id} references unknown location id: {locationId}"
              | Some _ -> None
              match lastSafeLocationId gameObject with
              | Some locationId when not (state.Objects.ContainsKey locationId) ->
                  Some $"Player object {gameObject.Id} references unknown last safe location id: {locationId}"
              | _ -> None
              match accountIdFromProperties gameObject.Properties with
              | None -> Some $"Player object {gameObject.Id} is missing accountId."
              | Some ownerAccountId when not (state.Accounts.ContainsKey ownerAccountId) ->
                  Some $"Player object {gameObject.Id} references unknown account id: {ownerAccountId}"
              | Some _ -> None
              if gameObject.Properties.ContainsKey InventoryProperty then
                  Some $"Player object {gameObject.Id} must not store legacy properties.inventory."
              else
                  None
              let inventory = CarriedItems.inventoryMap state gameObject.Id

              if
                  inventory
                  |> Map.toList
                  |> List.exists (fun (itemId, quantity) ->
                      not (state.ItemIds.Contains itemId) || quantity < 0)
              then
                  Some $"Player object {gameObject.Id} has invalid carried inventory entries."
              else
                  None ]
            |> List.tryPick id
            |> function
                | Some error -> Error error
                | None -> Ok()