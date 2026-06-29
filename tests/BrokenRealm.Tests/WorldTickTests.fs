namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module WorldTickTests =
    let private tick state times =
        List.fold
            (fun current _ ->
                match Kernel.tickWorld current 1 30 (fun _ -> false) with
                | Ok updated -> updated
                | Error error -> Assert.True(false, error); current)
            state
            [ for _ in 1 .. times -> () ]

    let private herbivoresIn (state: GameState) (locationId: ObjectId) =
        Kernel.contentsOf state locationId
        |> List.filter (fun gameObject -> gameObject.Tags.Contains "herbivore")

    [<Fact>]
    let ``World tick advances forest without connected players`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        let updated = tick limboState 1
        let forest = updated.Objects["forest"]

        match forest.Properties |> Map.tryFind "tickCount" with
        | Some(IntegerValue value) when value >= 1L -> Assert.True(true)
        | _ -> Assert.True(false, "Expected forest tickCount to advance without in-play players.")

    [<Fact>]
    let ``Creature tick advances while no player is connected`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        let updated = tick limboState 1
        let hare = updated.Objects["forest-hare"]

        match hare.Properties |> Map.tryFind "tickSteps" with
        | Some(IntegerValue 1L) -> Assert.True(true)
        | _ -> Assert.True(false, "Expected forest hare tickSteps to advance during autonomous world tick.")

    [<Fact>]
    let ``Hare wanders north to the village on the second world tick`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        let updated = tick limboState 2
        let hare = updated.Objects["forest-hare"]

        Assert.Equal(Some "village", hare.LocationId)

    [<Fact>]
    let ``Examine hare dispatches to creature behavior`` () =
        let state = ObjectDatabase.initialState

        match CommandMatching.tryMatchForCharacter GameSnapshots.PrototypeCharacterId En "examine hare" state with
        | CommandMatching.Matched matched ->
            Assert.Equal("forest-hare", matched.ObjectId)
            Assert.Equal("examine", matched.MethodName)
            Assert.Equal("CreatureBehavior", matched.BehaviorClassName)
        | CommandMatching.Ambiguous _ -> Assert.True(false, "Expected a direct examine match.")
        | CommandMatching.MatchedSequence _ -> Assert.True(false, "Expected a direct examine match.")
        | CommandMatching.NoMatch -> Assert.True(false, "Expected examine hare to match.")

    [<Fact>]
    let ``Forest spawns a hare when none remain in the forest`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        let afterWander = tick limboState 2
        Assert.Empty(herbivoresIn afterWander "forest")

        let repopulated = tick afterWander 1
        let forestHares = herbivoresIn repopulated "forest"

        Assert.True(
            forestHares.Length >= 1,
            "Expected the forest to repopulate at least one hare when none remain.")
        Assert.True(
            forestHares.Length <= 2,
            $"Expected at most two hares in the forest (hare returned from village), got {forestHares.Length}.")

    [<Fact>]
    let ``Village wildlife property syncs when a creature is present`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        let afterWander = tick limboState 2
        let synced = tick afterWander 1
        let village = synced.Objects["village"]

        match village.Properties |> Map.tryFind "wildlife" with
        | Some(IntegerValue 1L) -> Assert.True(true)
        | _ -> Assert.True(false, "Expected village wildlife to sync when a hare is in the settlement.")

    [<Fact>]
    let ``Village look mentions wildlife when a creature is present`` () =
        let afterWander = tick ObjectDatabase.initialState 2
        let synced = tick afterWander 1

        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" synced

        let looked = Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "look" inVillage.State

        let lines =
            looked.Messages
            |> List.map (ResponseFormatting.localizeMessage looked.State En)

        Assert.Contains("You notice wildlife moving through the settlement.", lines)

    [<Fact>]
    let ``Player creatures in world receive tick pulses`` () =
        let updated = tick ObjectDatabase.initialState 1
        let player = updated.Objects[GameSnapshots.PrototypeCharacterId]

        Assert.Contains("creature", player.Tags)
        Assert.Equal(Some "forest", player.LocationId)