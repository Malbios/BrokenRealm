namespace BrokenRealm.Server

open System.Collections.Generic

type PendingMessageStore() =
    let gate = obj()
    let queues = Dictionary<CharacterId, string list>()

    member _.Enqueue(characterId: CharacterId, line: string) =
        lock gate (fun () ->
            match queues.TryGetValue characterId with
            | true, existing -> queues[characterId] <- existing @ [ line ]
            | false, _ -> queues[characterId] <- [ line ])

    member _.DequeueAll(characterId: CharacterId) : string list =
        lock gate (fun () ->
            match queues.TryGetValue characterId with
            | true, lines ->
                queues.Remove characterId |> ignore
                lines
            | false, _ -> [] : string list)

module RoomBroadcast =
    let private roomSuffix = ".room"

    let isRoomMessage (message: Message) = message.Key.EndsWith roomSuffix

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

    let enqueueRoomMessages (store: PendingMessageStore) (state: GameState) culture (actingCharacterId: CharacterId) (messages: Message list) =
        let roomMessages = messages |> List.filter isRoomMessage

        if not roomMessages.IsEmpty then
            let locationId = PlayerObjects.locationId (PlayerObjects.get state actingCharacterId)
            let recipients = otherPlayersInRoom state locationId actingCharacterId

            for recipientId in recipients do
                for message in roomMessages do
                    let line = ResponseFormatting.localizeMessage state culture message
                    store.Enqueue(recipientId, line)

    let actorMessages messages = messages |> List.filter (fun message -> not (isRoomMessage message))

    let buildResponseLines (store: PendingMessageStore) (state: GameState) (culture: Culture) (characterId: CharacterId) (messages: Message list) : string list =
        let pending : string list = store.DequeueAll characterId

        let commandLines =
            actorMessages messages
            |> List.map (ResponseFormatting.localizeMessage state culture)

        pending @ commandLines