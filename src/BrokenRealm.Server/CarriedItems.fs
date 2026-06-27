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

    let private stackQuantity (gameObject: GameObject) =
        match gameObject.Properties |> Map.tryFind QuantityProperty with
        | Some(IntegerValue quantity) when quantity > 0L -> Some(int quantity)
        | _ -> None

    let migrationStackId (playerId: ObjectId) (itemId: ItemId) = $"carried-{playerId}-{itemId}"

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

    let addInventory (state: GameState) (playerId: ObjectId) (itemId: ItemId) (amount: int) =
        if amount <= 0 then
            state
        else
            match stacksFor state playerId |> List.tryFind (fun stack -> stackItemId stack = Some itemId) with
            | Some existing ->
                let updatedQuantity = stackQuantity existing |> Option.defaultValue 0 |> (+) amount
                let updatedStack = withStackQuantity existing updatedQuantity

                { state with Objects = Map.add existing.Id updatedStack state.Objects }
            | None ->
                let stackId = ObjectIds.create()
                let stack = createStack stackId playerId itemId amount

                { state with Objects = Map.add stackId stack state.Objects }

    let validateCarriedStack (state: GameState) (gameObject: GameObject) =
        if not (isCarriedStack gameObject) then
            Ok()
        else
            [ match gameObject.LocationId with
              | None -> Some $"Carried stack {gameObject.Id} must have a carrying player location."
              | Some playerId ->
                  match state.Objects |> Map.tryFind playerId with
                  | Some carrier when carrier.Tags.Contains "player" -> None
                  | _ -> Some $"Carried stack {gameObject.Id} references unknown player id: {playerId}"
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