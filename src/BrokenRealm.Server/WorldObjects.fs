namespace BrokenRealm.Server

module WorldObjects =
    [<Literal>]
    let CreatureTag = "creature"

    let private allowedDirections = Set.ofList [ "north"; "south"; "east"; "west" ]

    let isCreature (gameObject: GameObject) = gameObject.Tags.Contains CreatureTag

    let isInWorld (gameObject: GameObject) = gameObject.LocationId.IsSome

    let shouldTickCreature (gameObject: GameObject) =
        isCreature gameObject
        && isInWorld gameObject
        && not (CarriedItems.isCarriedStack gameObject)

    let isRoom (gameObject: GameObject) =
        gameObject.LocationId.IsNone
        && not (PlayerObjects.isPlayer gameObject)
        && not (CarriedItems.isCarriedStack gameObject)

    let isValidDirection direction =
        not (System.String.IsNullOrWhiteSpace direction) && allowedDirections.Contains direction

    let private parseCommaSeparated (value: string) =
        value.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun entry -> entry.Trim())
        |> Array.filter (fun entry -> not (System.String.IsNullOrWhiteSpace entry))
        |> Array.toList

    let parseTags (value: string) = parseCommaSeparated value

    let parseAliases (enValue: string) (deValue: string) =
        Map.ofList
            [ En, parseCommaSeparated enValue
              De, parseCommaSeparated deValue ]

    let createPermanent
        (state: GameState)
        (locationId: ObjectId)
        (nameKey: string)
        (descriptionKey: string option)
        (behaviorModuleId: string)
        (behaviorClassName: string)
        (tags: string list)
        (aliases: Map<Culture, string list>)
        (properties: Map<string, GameValue>)
        =
        let objectId = ObjectIds.create()

        let gameObject =
            { Id = objectId
              Name = nameKey
              NameKey = nameKey
              Aliases = aliases
              DescriptionKey = descriptionKey
              LocationId = Some locationId
              Tags = Set.ofList tags
              Properties = properties
              References = Map.empty
              BehaviorModuleId = behaviorModuleId
              BehaviorClassName = behaviorClassName }

        objectId, { state with Objects = Map.add objectId gameObject state.Objects }

    let createRoom
        (state: GameState)
        (nameKey: string)
        (descriptionKey: string option)
        (behaviorModuleId: string)
        (behaviorClassName: string)
        (tags: string list)
        (aliases: Map<Culture, string list>)
        (properties: Map<string, GameValue>)
        =
        let objectId = ObjectIds.create()

        let gameObject =
            { Id = objectId
              Name = nameKey
              NameKey = nameKey
              Aliases = aliases
              DescriptionKey = descriptionKey
              LocationId = None
              Tags = Set.ofList tags
              Properties = properties
              References = Map.empty
              BehaviorModuleId = behaviorModuleId
              BehaviorClassName = behaviorClassName }

        objectId, { state with Objects = Map.add objectId gameObject state.Objects }

    let private addExit (state: GameState) (roomId: ObjectId) (direction: string) (destinationId: ObjectId) =
        match state.Objects |> Map.tryFind roomId with
        | None -> Error $"Unknown room id: {roomId}"
        | Some room when not (isRoom room) -> Error "Only room objects support exit wiring."
        | Some room when not (isValidDirection direction) -> Error $"Invalid exit direction: {direction}"
        | Some room when Map.containsKey direction room.References ->
            Error $"Room {roomId} already has an exit to the {direction}."
        | Some room when not (state.Objects.ContainsKey destinationId) ->
            Error $"Unknown destination room id: {destinationId}"
        | Some room when not (isRoom state.Objects[destinationId]) ->
            Error "Exit destinations must be room objects."
        | Some room ->
            let updated = { room with References = Map.add direction destinationId room.References }
            Ok { state with Objects = Map.add roomId updated state.Objects }

    let growRoomExit
        (state: GameState)
        (sourceRoomId: ObjectId)
        (direction: string)
        (reverseDirection: string)
        (nameKey: string)
        (descriptionKey: string option)
        (behaviorModuleId: string)
        (behaviorClassName: string)
        (tags: string list)
        (aliases: Map<Culture, string list>)
        (properties: Map<string, GameValue>)
        =
        match state.Objects |> Map.tryFind sourceRoomId with
        | None -> Error $"Unknown source room id: {sourceRoomId}"
        | Some source when not (isRoom source) -> Error "growRoomExit requires a room object as the source."
        | Some source when not (isValidDirection direction) -> Error $"Invalid exit direction: {direction}"
        | Some source when not (isValidDirection reverseDirection) -> Error $"Invalid reverse exit direction: {reverseDirection}"
        | Some source when Map.containsKey direction source.References ->
            Error $"Room {sourceRoomId} already has an exit to the {direction}."
        | Some source ->
            let layoutProperties = RoomMap.assignMapLayout source direction nameKey properties

            let newRoomId, withRoom =
                createRoom
                    state
                    nameKey
                    descriptionKey
                    behaviorModuleId
                    behaviorClassName
                    tags
                    aliases
                    layoutProperties

            addExit withRoom sourceRoomId direction newRoomId
            |> Result.bind (fun wired -> addExit wired newRoomId reverseDirection sourceRoomId)
            |> Result.map (fun updated -> newRoomId, updated)

    let isPermanentThing (gameObject: GameObject) =
        not (PlayerObjects.isPlayer gameObject)
        && not (CarriedItems.isCarriedStack gameObject)
        && gameObject.LocationId.IsSome

    let isItemContainer (gameObject: GameObject) =
        gameObject.Tags.Contains "container" && isPermanentThing gameObject

    let tryContainerCapacity (gameObject: GameObject) =
        match gameObject.Properties |> Map.tryFind "capacity" with
        | Some(IntegerValue value) when value > 0L -> Some(int value)
        | _ -> None

    let containerUsedCapacity (state: GameState) (containerId: ObjectId) =
        CarriedItems.stacksIn state containerId
        |> List.sumBy (fun stack -> CarriedItems.stackQuantity stack |> Option.defaultValue 0)

    let validateContainerCapacity (state: GameState) (containerId: ObjectId) (incomingAmount: int) =
        match state.Objects |> Map.tryFind containerId with
        | None -> Error $"Unknown container id: {containerId}"
        | Some container when not (isItemContainer container) -> Ok()
        | Some container ->
            match tryContainerCapacity container with
            | None -> Ok()
            | Some limit when containerUsedCapacity state containerId + incomingAmount > limit ->
                Error "container_capacity_exceeded"
            | Some _ -> Ok()

    let isItemContainerId (state: GameState) (objectId: ObjectId) =
        match state.Objects |> Map.tryFind objectId with
        | Some gameObject when isItemContainer gameObject -> true
        | _ -> false

    let isAccessibleItemContainer (state: GameState) (actorId: ObjectId) (containerId: ObjectId) =
        match PlayerObjects.tryGet state actorId, state.Objects |> Map.tryFind containerId with
        | Some actor, Some container when isItemContainer container ->
            container.LocationId = Some(PlayerObjects.locationId actor)
        | _ -> false

    let isValidPlacementLocation (state: GameState) (locationId: ObjectId) =
        match state.Objects |> Map.tryFind locationId with
        | None -> Error $"Unknown placement location id: {locationId}"
        | Some container when PlayerObjects.isPlayer container ->
            Error "Permanent objects cannot be placed inside a player."
        | Some container when CarriedItems.isCarriedStack container ->
            Error "Permanent objects cannot be placed inside a carried stack."
        | Some container when isItemContainer container ->
            Error "Permanent objects cannot be placed inside a container."
        | Some _ -> Ok()

    let private hasContents (state: GameState) (objectId: ObjectId) =
        state.Objects
        |> Map.exists (fun _ gameObject -> gameObject.LocationId = Some objectId)

    let destroy (state: GameState) (objectId: ObjectId) =
        match state.Objects |> Map.tryFind objectId with
        | None -> Error $"Unknown object id: {objectId}"
        | Some gameObject when PlayerObjects.isPlayer gameObject ->
            Error "Player objects cannot be destroyed."
        | Some gameObject when CarriedItems.isCarriedStack gameObject ->
            Error "Carried stack objects cannot be destroyed."
        | Some gameObject when gameObject.LocationId.IsNone ->
            Error "Room objects cannot be destroyed."
        | Some _ when hasContents state objectId ->
            Error "Objects with contents cannot be destroyed."
        | Some _ -> Ok { state with Objects = Map.remove objectId state.Objects }

    let movePermanent (state: GameState) (objectId: ObjectId) (destinationId: ObjectId) =
        match state.Objects |> Map.tryFind objectId with
        | None -> Error $"Unknown object id: {objectId}"
        | Some gameObject when not (isPermanentThing gameObject) ->
            Error "Only permanent contained objects can be relocated this way."
        | Some gameObject ->
            match isValidPlacementLocation state destinationId with
            | Error error -> Error error
            | Ok() ->
                let updated = { gameObject with LocationId = Some destinationId }
                Ok { state with Objects = Map.add objectId updated state.Objects }