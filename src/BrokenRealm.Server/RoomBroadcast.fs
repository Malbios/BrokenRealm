namespace BrokenRealm.Server

module RoomBroadcast =
    let private roomSuffix = ".room"

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
            then
                Some gameObject.Id
            else
                None)

    let planRoomDelivery (state: GameState) culture (actingCharacterId: CharacterId) (messages: Message list) =
        let roomMessages = messages |> List.filter isRoomMessage

        if roomMessages.IsEmpty then
            []
        else
            let locationId = PlayerObjects.locationId (PlayerObjects.get state actingCharacterId)
            let recipients = otherPlayersInRoom state locationId actingCharacterId

            [ for recipientId in recipients do
                  for message in roomMessages do
                      recipientId, ResponseFormatting.localizeMessage state culture message ]

    let actorResponseLines (state: GameState) (culture: Culture) (messages: Message list) : string list =
        actorMessages messages
        |> List.map (ResponseFormatting.localizeMessage state culture)