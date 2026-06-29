namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module SessionTests =
    let private ensureDefaultRoomBroadcastFilter () =
        RoomBroadcast.setConnectionFilter (fun _ -> true)

    [<Fact>]
    let ``Prototype account owns both seeded characters`` () =
        let state = ObjectDatabase.initialState

        Assert.True(state.Accounts.ContainsKey GameSnapshots.PrototypeAccountId)

        PlayerObjects.playersByAccount state GameSnapshots.PrototypeAccountId
        |> List.iter (fun player ->
            Assert.Equal(GameSnapshots.PrototypeAccountId, PlayerObjects.accountId player))

    [<Fact>]
    let ``Session selection rejects characters owned by another account`` () =
        let store = SessionStore()
        let session = store.GetOrCreate()
        let otherAccountPlayer =
            PlayerObjects.create
                "other-account-character"
                "other account character"
                "object.other-account-character.name"
                "other-account"
                "forest"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add otherAccountPlayer.Id otherAccountPlayer }

        match store.SelectCharacter(session.Id, otherAccountPlayer.Id, state) with
        | Error error -> Assert.Contains("not owned by account", error)
        | Ok _ -> Assert.True(false, "Expected foreign character selection to be rejected.")

    [<Fact>]
    let ``Tab session ids isolate selected characters`` () =
        let store = SessionStore()
        let playerSession = store.GetOrCreate(sessionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
        let scoutSession = store.GetOrCreate(sessionId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")

        match store.SelectCharacter(scoutSession.Id, GameSnapshots.PrototypeScoutCharacterId, ObjectDatabase.initialState) with
        | Ok updated -> Assert.Equal(GameSnapshots.PrototypeScoutCharacterId, updated.SelectedCharacterId)
        | Error error -> Assert.True(false, error)

        let refreshedPlayer =
            match store.TryGet playerSession.Id with
            | Some session -> session
            | None -> failwith "Expected player tab session to exist."

        Assert.Equal(GameSnapshots.PrototypeCharacterId, refreshedPlayer.SelectedCharacterId)

    [<Fact>]
    let ``Session selection switches the acting character`` () =
        let store = SessionStore()
        let session = store.GetOrCreate()

        match
            store.SelectCharacter(session.Id, GameSnapshots.PrototypeScoutCharacterId, ObjectDatabase.initialState)
        with
        | Ok updated -> Assert.Equal(GameSnapshots.PrototypeScoutCharacterId, updated.SelectedCharacterId)
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Login accepts the seeded prototype account password`` () =
        let store = SessionStore()
        let session = store.GetOrCreate()

        match store.Login(session.Id, GameSnapshots.PrototypeAccountId, "prototype", ObjectDatabase.initialState) with
        | Ok updated ->
            Assert.True(updated.Authenticated)
            Assert.Equal(GameSnapshots.PrototypeAccountId, updated.AccountId)
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Login rejects invalid passwords`` () =
        let store = SessionStore()
        let session = store.GetOrCreate()

        match store.Login(session.Id, GameSnapshots.PrototypeAccountId, "wrong-password", ObjectDatabase.initialState) with
        | Error error -> Assert.Equal("Invalid password.", error)
        | Ok _ -> Assert.True(false, "Expected invalid password login to be rejected.")

    [<Fact>]
    let ``Register creates an account with a playable character`` () =
        match
            Kernel.tryRegisterAccount "new-account" "secret" (Some "New account") ObjectDatabase.initialState
        with
        | Ok state ->
            Assert.True(state.Accounts.ContainsKey "new-account")
            Assert.Equal(1, PlayerObjects.playersByAccount state "new-account" |> List.length)
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Say room delivery targets other players in the same room`` () =
        ensureDefaultRoomBroadcastFilter ()
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInForest = PlayerObjects.withLocation scout "forest"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInForest }

        let said =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "say Hello scout" state

        let actorLines = RoomBroadcast.actorResponseLines said.State En said.Messages
        Assert.Equal<string list>([ "You say, \"Hello scout\"." ], actorLines)

        ensureDefaultRoomBroadcastFilter ()
        let deliveries = RoomBroadcast.planRoomDelivery said.State En GameSnapshots.PrototypeCharacterId said.Messages

        Assert.Equal(1, deliveries.Length)

        let recipient, line = deliveries[0]
        Assert.Equal(GameSnapshots.PrototypeScoutCharacterId, recipient)
        Assert.Equal("A prototype player says, \"Hello scout\".", line)

    [<Fact>]
    let ``Drop room delivery notifies other players in the same room`` () =
        ensureDefaultRoomBroadcastFilter ()
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInForest = PlayerObjects.withLocation scout "forest"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInForest }

        let gathered =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "gather wood" state

        let dropped =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "drop wood" gathered.State

        ensureDefaultRoomBroadcastFilter ()
        let deliveries =
            RoomBroadcast.planRoomDelivery dropped.State En GameSnapshots.PrototypeCharacterId dropped.Messages
            |> List.filter (fun (recipient, _) -> recipient = GameSnapshots.PrototypeScoutCharacterId)

        Assert.Equal(1, deliveries.Length)
        Assert.Equal("A prototype player drops 1 wood.", snd deliveries[0])

    [<Fact>]
    let ``Move leave room delivery notifies players in the departed room`` () =
        ensureDefaultRoomBroadcastFilter ()
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInForest = PlayerObjects.withLocation scout "forest"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInForest }

        let moved =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" state

        ensureDefaultRoomBroadcastFilter ()
        let leaveDeliveries =
            RoomBroadcast.planRoomDelivery moved.State En GameSnapshots.PrototypeCharacterId moved.Messages
            |> List.filter (fun (recipient, line) ->
                recipient = GameSnapshots.PrototypeScoutCharacterId
                && line.Contains("goes north"))

        Assert.Equal(1, leaveDeliveries.Length)
        Assert.Equal("A prototype player goes north.", snd leaveDeliveries[0])

    [<Fact>]
    let ``Move arrive room delivery notifies players in the destination room`` () =
        ensureDefaultRoomBroadcastFilter ()
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInVillage = PlayerObjects.withLocation scout "village"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInVillage }

        let moved =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" state

        ensureDefaultRoomBroadcastFilter ()
        let arriveDeliveries =
            RoomBroadcast.planRoomDelivery moved.State En GameSnapshots.PrototypeCharacterId moved.Messages
            |> List.filter (fun (recipient, line) ->
                recipient = GameSnapshots.PrototypeScoutCharacterId
                && line.Contains("arrives"))

        Assert.Equal(1, arriveDeliveries.Length)
        Assert.Equal("A prototype player arrives.", snd arriveDeliveries[0])

    [<Fact>]
    let ``Move leave room delivery does not target players in the destination room`` () =
        ensureDefaultRoomBroadcastFilter ()
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInVillage = PlayerObjects.withLocation scout "village"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInVillage }

        let moved =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" state

        ensureDefaultRoomBroadcastFilter ()
        let leaveDeliveries =
            RoomBroadcast.planRoomDelivery moved.State En GameSnapshots.PrototypeCharacterId moved.Messages
            |> List.filter (fun (recipient, _) -> recipient = GameSnapshots.PrototypeScoutCharacterId)

        Assert.Equal(1, leaveDeliveries.Length)
        Assert.Equal("A prototype player arrives.", snd leaveDeliveries[0])

    [<Fact>]
    let ``Inventory matches on the player object before the room`` () =
        let state = ObjectDatabase.initialState

        match CommandMatching.tryMatchForCharacter GameSnapshots.PrototypeCharacterId En "inventory" state with
        | CommandMatching.Matched matched -> Assert.Equal(GameSnapshots.PrototypeCharacterId, matched.ObjectId)
        | CommandMatching.NoMatch -> Assert.True(false, "Expected inventory to match the player object.")
        | CommandMatching.Ambiguous _ -> Assert.True(false, "Expected an unambiguous inventory match.")