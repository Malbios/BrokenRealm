namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module SessionTests =
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
        let session = store.CreateAnonymousPrototypeSession()
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
    let ``Session selection switches the acting character`` () =
        let store = SessionStore()
        let session = store.CreateAnonymousPrototypeSession()

        match
            store.SelectCharacter(session.Id, GameSnapshots.PrototypeScoutCharacterId, ObjectDatabase.initialState)
        with
        | Ok updated -> Assert.Equal(GameSnapshots.PrototypeScoutCharacterId, updated.SelectedCharacterId)
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Login accepts the seeded prototype account password`` () =
        let store = SessionStore()
        let session = store.CreateAnonymousPrototypeSession()

        match store.Login(session.Id, GameSnapshots.PrototypeAccountId, "prototype", ObjectDatabase.initialState) with
        | Ok updated ->
            Assert.True(updated.Authenticated)
            Assert.Equal(GameSnapshots.PrototypeAccountId, updated.AccountId)
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Login rejects invalid passwords`` () =
        let store = SessionStore()
        let session = store.CreateAnonymousPrototypeSession()

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
    let ``Say room messages enqueue for other players in the same room`` () =
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInForest = PlayerObjects.withLocation scout "forest"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInForest }

        let pending = PendingMessageStore()

        let said =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "say Hello scout" state

        RoomBroadcast.enqueueRoomMessages
            pending
            said.State
            En
            GameSnapshots.PrototypeCharacterId
            said.Messages

        let actorLines =
            RoomBroadcast.buildResponseLines
                pending
                said.State
                En
                GameSnapshots.PrototypeCharacterId
                said.Messages

        Assert.Equal<string list>([ "You say, \"Hello scout\"." ], actorLines)

        let scoutLines = pending.DequeueAll GameSnapshots.PrototypeScoutCharacterId

        Assert.Equal<string list>([ "A prototype player says, \"Hello scout\"." ], scoutLines)

    [<Fact>]
    let ``Pending room messages prepend the next command response for a recipient`` () =
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInForest = PlayerObjects.withLocation scout "forest"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInForest }

        let pending = PendingMessageStore()

        let said =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "emote waves" state

        RoomBroadcast.enqueueRoomMessages
            pending
            said.State
            En
            GameSnapshots.PrototypeCharacterId
            said.Messages

        let looked =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeScoutCharacterId En "look" said.State

        let lines =
            RoomBroadcast.buildResponseLines
                pending
                looked.State
                En
                GameSnapshots.PrototypeScoutCharacterId
                looked.Messages

        Assert.Equal("A prototype player waves.", lines[0])
        Assert.True(lines.Length >= 2)
        Assert.Contains("forest", lines[1].ToLowerInvariant())

    [<Fact>]
    let ``Inventory matches on the player object before the room`` () =
        let state = ObjectDatabase.initialState

        match CommandMatching.tryMatchForCharacter GameSnapshots.PrototypeCharacterId En "inventory" state with
        | Some matched -> Assert.Equal(GameSnapshots.PrototypeCharacterId, matched.ObjectId)
        | None -> Assert.True(false, "Expected inventory to match the player object.")