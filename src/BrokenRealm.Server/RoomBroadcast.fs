namespace BrokenRealm.Server

module RoomBroadcast =
    let private roomSuffix = ".room"

    let mutable private isCharacterConnected = fun (_: CharacterId) -> true

    let setConnectionFilter (filter: CharacterId -> bool) = isCharacterConnected <- filter

    let characterGroup (characterId: CharacterId) = $"character:{characterId}"

    let isRoomMessage (message: Message) = message.Key.EndsWith roomSuffix

    let actorMessages messages = messages |> List.filter (fun message -> not (isRoomMessage message))

    let private otherPlayersInRoom (state: GameState) (locationId: ObjectId) (actingCharacterId: CharacterId) =
        state.Objects
        |> Map.toList
        |> List.choose (fun (_, gameObject) ->
            if
                PlayerObjects.isPlayer gameObject
                && gameObject.Id <> actingCharacterId
                && gameObject.LocationId = Some locationId
                && isCharacterConnected gameObject.Id
            then
                Some gameObject.Id
            else
                None)

    let private roomIdForMessage (state: GameState) (actingCharacterId: CharacterId) (message: Message) =
        match message.Args |> Map.tryFind "roomId" with
        | Some roomId -> roomId
        | None -> PlayerObjects.locationId (PlayerObjects.get state actingCharacterId)

    let planRoomDelivery (state: GameState) culture (actingCharacterId: CharacterId) (messages: Message list) =
        let roomMessages = messages |> List.filter isRoomMessage

        if roomMessages.IsEmpty then
            []
        else
            [ for message in roomMessages do
                  let roomId = roomIdForMessage state actingCharacterId message
                  let recipients = otherPlayersInRoom state roomId actingCharacterId

                  for recipientId in recipients do
                      recipientId, ResponseFormatting.localizeMessage state culture message ]

    let actorResponseLines (state: GameState) (culture: Culture) (messages: Message list) : string list =
        actorMessages messages
        |> List.map (fun message ->
            if message.Key = "map.display" then
                match message.Args |> Map.tryFind "actor" with
                | Some characterId -> RoomMap.formatDisplayMessage state culture characterId
                | None -> Localizer.text culture { Key = "map.unavailable"; Args = Map.empty }
            else
                ResponseFormatting.localizeMessage state culture message)