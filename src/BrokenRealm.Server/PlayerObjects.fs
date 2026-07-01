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
    let VisitedRoomIdsProperty = "visitedRoomIds"

    [<Literal>]
    let PlayerBehaviorModuleId = "player-behaviors"

    [<Literal>]
    let PlayerBehaviorClassName = "PlayerBehavior"

    let isPlayer (gameObject: GameObject) = gameObject.Tags.Contains PlayerTag

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
          Tags = Set.ofList [ PlayerTag; "creature" ]
          Properties =
              Map.ofList [ AccountIdProperty, StringValue accountId
                           "hunger", IntegerValue 0L ]
          References = Map.empty
          BehaviorModuleId = PlayerBehaviorModuleId
          BehaviorClassName = PlayerBehaviorClassName }

    let tryGet (state: GameState) (characterId: CharacterId) =
        state.Objects
        |> Map.tryFind characterId
        |> Option.bind (fun gameObject -> if isPlayer gameObject then Some gameObject else None)

    let get (state: GameState) (characterId: CharacterId) =
        match tryGet state characterId with
        | Some gameObject -> gameObject
        | None -> failwith $"Unknown player object id: {characterId}"

    let inventory (state: GameState) (playerId: CharacterId) = CarriedItems.inventoryMap state playerId

    let hunger (gameObject: GameObject) =
        match gameObject.Properties |> Map.tryFind "hunger" with
        | Some(IntegerValue value) -> int value
        | _ -> 0

    let accountId (gameObject: GameObject) =
        accountIdFromProperties gameObject.Properties
        |> Option.defaultWith (fun () -> failwith $"Player object {gameObject.Id} is missing accountId.")

    let tryLocationId (gameObject: GameObject) = gameObject.LocationId

    let lastSafeLocationId (gameObject: GameObject) =
        match gameObject.Properties |> Map.tryFind LastSafeLocationProperty with
        | Some(StringValue locationId) -> Some locationId
        | _ -> None

    let visitedRoomIds (gameObject: GameObject) =
        match gameObject.Properties |> Map.tryFind VisitedRoomIdsProperty with
        | Some(ListValue values) ->
            values
            |> List.choose (function
                | StringValue roomId -> Some roomId
                | _ -> None)
        | _ -> []

    let private isRoomObject (gameObject: GameObject) =
        gameObject.LocationId.IsNone
        && not (isPlayer gameObject)
        && not (CarriedItems.isCarriedStack gameObject)

    let recordRoomVisit (state: GameState) (playerId: CharacterId) (roomId: ObjectId) =
        match tryGet state playerId, state.Objects |> Map.tryFind roomId with
        | Some player, Some room when isRoomObject room ->
            let visited = visitedRoomIds player |> Set.ofList

            if visited.Contains roomId then
                state
            else
                let updatedIds =
                    visited
                    |> Set.add roomId
                    |> Set.toList
                    |> List.sort

                let listValue = ListValue(updatedIds |> List.map StringValue)

                let updatedPlayer =
                    { player with
                        Properties = Map.add VisitedRoomIdsProperty listValue player.Properties }

                { state with Objects = Map.add playerId updatedPlayer state.Objects }
        | _ -> state

    let isInLimbo (gameObject: GameObject) = gameObject.LocationId.IsNone

    let locationId (gameObject: GameObject) =
        gameObject.LocationId
        |> Option.defaultWith (fun () -> failwith $"Player object {gameObject.Id} is missing a location.")

    let withLocation (gameObject: GameObject) (locationId: ObjectId) =
        { gameObject with LocationId = Some locationId }

    let withoutLocation (gameObject: GameObject) = { gameObject with LocationId = None }

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