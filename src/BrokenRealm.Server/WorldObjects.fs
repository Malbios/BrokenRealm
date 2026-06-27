namespace BrokenRealm.Server

module WorldObjects =
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

    let isPermanentThing (gameObject: GameObject) =
        not (PlayerObjects.isPlayer gameObject)
        && not (CarriedItems.isCarriedStack gameObject)
        && gameObject.LocationId.IsSome

    let isValidPlacementLocation (state: GameState) (locationId: ObjectId) =
        match state.Objects |> Map.tryFind locationId with
        | None -> Error $"Unknown placement location id: {locationId}"
        | Some container when PlayerObjects.isPlayer container ->
            Error "Permanent objects cannot be placed inside a player."
        | Some container when CarriedItems.isCarriedStack container ->
            Error "Permanent objects cannot be placed inside a carried stack."
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