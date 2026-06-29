namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module LimboTests =
    let private ensureDefaultRoomBroadcastFilter () =
        RoomBroadcast.setConnectionFilter (fun _ -> true)

    let private scoutInForest state =
        let scout = PlayerObjects.get state GameSnapshots.PrototypeScoutCharacterId
        let scoutInForest = PlayerObjects.withLocation scout "forest"

        { state with
            Objects = state.Objects |> Map.add scout.Id scoutInForest }

    [<Fact>]
    let ``Enter limbo clears location and stores last safe location`` () =
        let state = ObjectDatabase.initialState

        match Limbo.enterLimbo state GameSnapshots.PrototypeCharacterId with
        | Ok updated ->
            let player = PlayerObjects.get updated GameSnapshots.PrototypeCharacterId
            Assert.True(PlayerObjects.isInLimbo player)
            Assert.Equal(Some "forest", PlayerObjects.lastSafeLocationId player)
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Limbo characters are excluded from room contents`` () =
        let scoutInLimbo =
            PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
            |> PlayerObjects.withoutLocation
            |> fun scout -> { scout with Properties = scout.Properties |> Map.add PlayerObjects.LastSafeLocationProperty (StringValue "forest") }

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scoutInLimbo.Id scoutInLimbo }

        let contents =
            Kernel.contentsOf state "forest"
            |> List.map _.Id

        Assert.DoesNotContain(GameSnapshots.PrototypeScoutCharacterId, contents)
        Assert.Contains(GameSnapshots.PrototypeCharacterId, contents)

    [<Fact>]
    let ``Commands are rejected while the selected character is in limbo`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        let result = Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "look" limboState

        Assert.Equal("limbo.not_in_play", result.Messages |> List.head |> fun message -> message.Key)

    [<Fact>]
    let ``Enter play restores the last safe location`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        match Kernel.tryEnterPlayForCharacter GameSnapshots.PrototypeCharacterId limboState with
        | Error error -> Assert.True(false, error)
        | Ok result ->
            let player = PlayerObjects.get result.State GameSnapshots.PrototypeCharacterId
            Assert.Equal(Some "forest", PlayerObjects.tryLocationId player)

            let line =
                RoomBroadcast.actorMessages result.Messages
                |> List.head
                |> ResponseFormatting.localizeMessage result.State En

            Assert.Equal("You enter forest.", line)

    [<Fact>]
    let ``Enter play announces arrival to other players in the room`` () =
        ensureDefaultRoomBroadcastFilter ()
        let limboState =
            match Limbo.enterLimbo (scoutInForest ObjectDatabase.initialState) GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        match Kernel.tryEnterPlayForCharacter GameSnapshots.PrototypeCharacterId limboState with
        | Error error -> Assert.True(false, error)
        | Ok result ->
            let deliveries =
                RoomBroadcast.planRoomDelivery
                    result.State
                    En
                    GameSnapshots.PrototypeCharacterId
                    result.Messages
                |> List.filter (fun (recipient, _) -> recipient = GameSnapshots.PrototypeScoutCharacterId)

            Assert.Equal(1, deliveries.Length)
            Assert.Equal("A prototype player arrives.", snd deliveries[0])

    [<Fact>]
    let ``Limbo players are not room broadcast recipients`` () =
        ensureDefaultRoomBroadcastFilter ()
        let scoutInLimbo =
            PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
            |> PlayerObjects.withoutLocation
            |> fun scout -> { scout with Properties = scout.Properties |> Map.add PlayerObjects.LastSafeLocationProperty (StringValue "forest") }

        let state =
            scoutInForest ObjectDatabase.initialState
            |> fun value -> { value with Objects = value.Objects |> Map.add scoutInLimbo.Id scoutInLimbo }

        let said =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "say hello" state

        let deliveries =
            RoomBroadcast.planRoomDelivery said.State En GameSnapshots.PrototypeCharacterId said.Messages

        Assert.Empty(deliveries)

    [<Fact>]
    let ``Disconnecting the last hub connection enters limbo`` () =
        let registry = ConnectionRegistry()

        registry.Set("conn-1", GameSnapshots.PrototypeCharacterId) |> ignore

        match registry.Remove "conn-1" with
        | Some characterId ->
            Assert.Equal(GameSnapshots.PrototypeCharacterId, characterId)
            Assert.False(registry.IsCharacterConnected characterId)

            match Limbo.enterLimbo ObjectDatabase.initialState characterId with
            | Ok updated -> Assert.True(PlayerObjects.isInLimbo (PlayerObjects.get updated characterId))
            | Error error -> Assert.True(false, error)
        | None -> Assert.True(false, "Expected removed connection to return a character id.")