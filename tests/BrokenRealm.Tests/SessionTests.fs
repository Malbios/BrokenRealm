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
                Map.empty

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
    let ``Inventory matches on the player object before the room`` () =
        let state = ObjectDatabase.initialState

        match CommandMatching.tryMatchForCharacter GameSnapshots.PrototypeCharacterId En "inventory" state with
        | Some matched -> Assert.Equal(GameSnapshots.PrototypeCharacterId, matched.ObjectId)
        | None -> Assert.True(false, "Expected inventory to match the player object.")