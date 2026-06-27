namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module SessionTests =
    [<Fact>]
    let ``Prototype account owns both seeded characters`` () =
        let state = ObjectDatabase.initialState

        Assert.True(state.Accounts.ContainsKey GameSnapshots.PrototypeAccountId)

        state.Characters
        |> Map.iter (fun _ character ->
            Assert.Equal(GameSnapshots.PrototypeAccountId, character.AccountId))

    [<Fact>]
    let ``Session selection rejects characters owned by another account`` () =
        let store = SessionStore()
        let session = store.CreateAnonymousPrototypeSession()
        let otherAccountCharacter =
            { ObjectDatabase.initialState.Characters[GameSnapshots.PrototypeCharacterId] with
                Id = "other-account-character"
                AccountId = "other-account" }

        let state =
            { ObjectDatabase.initialState with
                Characters =
                    ObjectDatabase.initialState.Characters
                    |> Map.add otherAccountCharacter.Id otherAccountCharacter }

        match store.SelectCharacter(session.Id, otherAccountCharacter.Id, state) with
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