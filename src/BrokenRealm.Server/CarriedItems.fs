namespace BrokenRealm.Server

module CarriedItems =
    [<Literal>]
    let CarriedTag = "carried"

    [<Literal>]
    let ItemIdProperty = "itemId"

    [<Literal>]
    let QuantityProperty = "quantity"

    [<Literal>]
    let StackBehaviorModuleId = "thing-behaviors"

    [<Literal>]
    let StackBehaviorClassName = "ThingBehavior"

    let isCarriedStack (gameObject: GameObject) = gameObject.Tags.Contains CarriedTag

    let private stackItemId (gameObject: GameObject) =
        match gameObject.Properties |> Map.tryFind ItemIdProperty with
        | Some(StringValue itemId) -> Some itemId
        | _ -> None

    let stackQuantity (gameObject: GameObject) =
        match gameObject.Properties |> Map.tryFind QuantityProperty with
        | Some(IntegerValue quantity) when quantity > 0L -> Some(int quantity)
        | _ -> None

    let createStack (stackId: ObjectId) (playerId: ObjectId) (itemId: ItemId) (quantity: Quantity) =
        { Id = stackId
          Name = itemId
          NameKey = $"item.{itemId}.stack"
          Aliases = Map.empty
          DescriptionKey = None
          LocationId = Some playerId
          Tags = Set.ofList [ CarriedTag; "item"; itemId ]
          Properties =
            Map.ofList
                [ ItemIdProperty, StringValue itemId
                  QuantityProperty, IntegerValue(int64 quantity) ]
          References = Map.empty
          BehaviorModuleId = StackBehaviorModuleId
          BehaviorClassName = StackBehaviorClassName }

    let stacksFor (state: GameState) (playerId: ObjectId) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter (fun gameObject -> gameObject.LocationId = Some playerId && isCarriedStack gameObject)
        |> List.sortBy _.Id

    let inventoryMap (state: GameState) (playerId: ObjectId) =
        stacksFor state playerId
        |> List.choose (fun stack ->
            match stackItemId stack, stackQuantity stack with
            | Some itemId, Some quantity -> Some(itemId, quantity)
            | _ -> None)
        |> Map.ofList

    let private withStackQuantity (stack: GameObject) quantity =
        { stack with
            Properties = Map.add QuantityProperty (IntegerValue(int64 quantity)) stack.Properties }

    let stacksIn (state: GameState) (containerId: ObjectId) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter (fun gameObject -> gameObject.LocationId = Some containerId && isCarriedStack gameObject)
        |> List.sortBy _.Id

    let itemQuantitiesInContainer (state: GameState) (containerId: ObjectId) =
        stacksIn state containerId
        |> List.choose (fun stack ->
            match stackItemId stack, stackQuantity stack with
            | Some itemId, Some quantity -> Some(itemId, quantity)
            | _ -> None)
        |> List.groupBy fst
        |> List.map (fun (itemId, entries) -> itemId, entries |> List.sumBy snd)
        |> Map.ofList

    let private addToContainer (state: GameState) (containerId: ObjectId) (itemId: ItemId) (amount: int) =
        if amount <= 0 then
            state
        else
            match stacksIn state containerId |> List.tryFind (fun stack -> stackItemId stack = Some itemId) with
            | Some existing ->
                let updatedQuantity = stackQuantity existing |> Option.defaultValue 0 |> (+) amount
                let updatedStack = withStackQuantity existing updatedQuantity

                { state with Objects = Map.add existing.Id updatedStack state.Objects }
            | None ->
                let stackId = ObjectIds.create()
                let stack = createStack stackId containerId itemId amount

                { state with Objects = Map.add stackId stack state.Objects }

    let addInventory (state: GameState) (playerId: ObjectId) (itemId: ItemId) (amount: int) =
        addToContainer state playerId itemId amount

    let removeQuantity (state: GameState) (containerId: ObjectId) (itemId: ItemId) (amount: int) =
        if amount <= 0 then
            Error "transferItem effects require an amount from 1 to 100."
        else
            match stacksIn state containerId |> List.tryFind (fun stack -> stackItemId stack = Some itemId) with
            | None -> Error $"Container does not contain any {itemId}."
            | Some stack ->
                match stackQuantity stack with
                | None -> Error $"Carried stack {stack.Id} must have a positive quantity."
                | Some quantity when quantity < amount -> Error $"You do not have enough {itemId}."
                | Some quantity when quantity = amount ->
                    Ok({ state with Objects = Map.remove stack.Id state.Objects })
                | Some quantity ->
                    let updatedStack = withStackQuantity stack (quantity - amount)
                    Ok({ state with Objects = Map.add stack.Id updatedStack state.Objects })

    let transferItem (state: GameState) (fromContainerId: ObjectId) (itemId: ItemId) (amount: int) (destinationId: ObjectId) =
        removeQuantity state fromContainerId itemId amount
        |> Result.map (fun stateAfterRemoval -> addToContainer stateAfterRemoval destinationId itemId amount)

    let private isItemContainer (gameObject: GameObject) =
        gameObject.Tags.Contains "container"
        && not (gameObject.Tags.Contains "player")
        && not (isCarriedStack gameObject)
        && gameObject.LocationId.IsSome

    let private isValidStackContainer (_state: GameState) (container: GameObject) =
        container.Tags.Contains "player"
        || (container.LocationId.IsNone && not (container.Tags.Contains "player"))
        || isItemContainer container

    let validateCarriedStack (state: GameState) (gameObject: GameObject) =
        if not (isCarriedStack gameObject) then
            Ok()
        else
            [ match gameObject.LocationId with
              | None -> Some $"Carried stack {gameObject.Id} must have a container location."
              | Some containerId ->
                  match state.Objects |> Map.tryFind containerId with
                  | None -> Some $"Carried stack {gameObject.Id} references unknown container id: {containerId}"
                  | Some container when isCarriedStack container ->
                      Some $"Carried stack {gameObject.Id} cannot be contained in another carried stack."
                  | Some container when not (isValidStackContainer state container) ->
                      Some $"Carried stack {gameObject.Id} must be in a player, room, or item container."
                  | Some _ -> None
              match stackItemId gameObject with
              | None -> Some $"Carried stack {gameObject.Id} is missing itemId."
              | Some itemId when not (state.ItemIds.Contains itemId) ->
                  Some $"Carried stack {gameObject.Id} references unknown item id: {itemId}"
              | Some _ -> None
              match stackQuantity gameObject with
              | None -> Some $"Carried stack {gameObject.Id} must have a positive quantity."
              | Some _ -> None
              if gameObject.BehaviorModuleId <> StackBehaviorModuleId then
                  Some $"Carried stack {gameObject.Id} must use behavior module {StackBehaviorModuleId}."
              else
                  None ]
            |> List.tryPick id
            |> function
                | Some error -> Error error
                | None -> Ok()