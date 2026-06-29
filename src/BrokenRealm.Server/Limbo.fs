namespace BrokenRealm.Server

module Limbo =
    [<Literal>]
    let DefaultReentryLocation = "forest"

    let isInLimbo = PlayerObjects.isInLimbo

    let tryLocationId = PlayerObjects.tryLocationId

    let lastSafeLocationId = PlayerObjects.lastSafeLocationId

    let private withLastSafeLocation (player: GameObject) (locationId: ObjectId) =
        { player with
            Properties =
                player.Properties
                |> Map.add PlayerObjects.LastSafeLocationProperty (StringValue locationId) }

    let enterLimbo (state: GameState) (characterId: CharacterId) =
        match PlayerObjects.tryGet state characterId with
        | None -> Error $"Unknown player object id: {characterId}"
        | Some player ->
            match player.LocationId with
            | None -> Ok state
            | Some locationId ->
                let updated =
                    player
                    |> fun value -> withLastSafeLocation value locationId
                    |> PlayerObjects.withoutLocation

                Ok { state with Objects = Map.add characterId updated state.Objects }

    let resolveReentryLocation (state: GameState) (player: GameObject) =
        lastSafeLocationId player
        |> Option.filter (fun locationId -> state.Objects.ContainsKey locationId)
        |> Option.defaultValue DefaultReentryLocation

    let tryEnterPlay (characterId: CharacterId) (state: GameState) =
        match PlayerObjects.tryGet state characterId with
        | None -> Error $"Unknown player object id: {characterId}"
        | Some player ->
            match player.LocationId with
            | Some _ -> Ok(state, [ { Key = "enter.already"; Args = Map.empty } ], false)
            | None ->
                let destinationId = resolveReentryLocation state player

                if not (state.Objects.ContainsKey destinationId) then
                    Error $"Unknown re-entry location: {destinationId}"
                else
                    let updated = PlayerObjects.withLocation player destinationId

                    let newState =
                        { state with Objects = Map.add characterId updated state.Objects }
                        |> fun current -> PlayerObjects.recordRoomVisit current characterId destinationId

                    let messages =
                        [ { Key = "enter.success"; Args = Map.ofList [ "location", destinationId ] }
                          { Key = "move.arrive.room"
                            Args = Map.ofList [ "actor", characterId; "roomId", destinationId ] } ]

                    Ok(newState, messages, true)

    let limboAllPlayers (state: GameState) =
        state.Objects
        |> Map.toList
        |> List.choose (fun (_, gameObject) ->
            if PlayerObjects.isPlayer gameObject then
                Some gameObject.Id
            else
                None)
        |> List.fold
            (fun current characterId ->
                match enterLimbo current characterId with
                | Ok updated -> updated
                | Error _ -> current)
            state