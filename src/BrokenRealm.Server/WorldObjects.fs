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

    let isValidPlacementLocation (state: GameState) (locationId: ObjectId) =
        match state.Objects |> Map.tryFind locationId with
        | None -> Error $"Unknown placement location id: {locationId}"
        | Some container when PlayerObjects.isPlayer container ->
            Error "Permanent objects cannot be placed inside a player."
        | Some container when CarriedItems.isCarriedStack container ->
            Error "Permanent objects cannot be placed inside a carried stack."
        | Some _ -> Ok()