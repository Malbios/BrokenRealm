namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module WorldTickTests =
    let private tick state times =
        List.fold
            (fun current _ ->
                let nextTickIndex =
                    match current.Objects["forest"].Properties |> Map.tryFind "tickCount" with
                    | Some(IntegerValue value) -> int value + 1
                    | _ -> 1

                match Kernel.tickWorld current nextTickIndex 30 (fun _ -> false) with
                | Ok updated -> updated
                | Error error -> Assert.True(false, error); current)
            state
            [ for _ in 1 .. times -> () ]

    let private herbivoresIn (state: GameState) (locationId: ObjectId) =
        Kernel.contentsOf state locationId
        |> List.filter (fun gameObject -> gameObject.Tags.Contains "herbivore")

    let private herbivoreCount (state: GameState) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter (fun gameObject -> gameObject.Tags.Contains "herbivore")
        |> List.length

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
    let ``Hare waits and then wanders north on the third world tick`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        let updated = tick limboState 3
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

        let afterWander = tick limboState 3
        Assert.Empty(herbivoresIn afterWander "forest")

        let waiting = tick afterWander 1
        let recoveryAfterStart =
            match waiting.Objects["forest"].Properties |> Map.tryFind "hareRecoveryRemaining" with
            | Some(IntegerValue value) -> int value
            | _ -> 0

        Assert.True(recoveryAfterStart > 0, "Expected the forest to start a hare recovery timer when empty.")

        let repopulated = tick waiting 3

        Assert.True(
            herbivoreCount repopulated >= 2,
            "Expected the forest recovery timer to spawn a new hare after the seeded hare left.")

        let forestHares = herbivoresIn repopulated "forest"

        Assert.True(
            forestHares.Length <= 2,
            $"Expected at most two hares in the forest, got {forestHares.Length}.")

    [<Fact>]
    let ``Village wildlife property syncs when a creature is present`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        let synced = tick limboState 3
        let village = synced.Objects["village"]

        match village.Properties |> Map.tryFind "wildlife" with
        | Some(IntegerValue 1L) -> Assert.True(true)
        | _ -> Assert.True(false, "Expected village wildlife to sync when a hare is in the settlement.")

    [<Fact>]
    let ``Village look mentions wildlife when a creature is present`` () =
        let synced = tick ObjectDatabase.initialState 3

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

    [<Fact>]
    let ``Creature AI persists an active goal and deterministic random state`` () =
        let first = tick ObjectDatabase.initialState 1
        let second = tick ObjectDatabase.initialState 1

        Assert.Equal(first.Objects["forest-hare"].Properties["ai"], second.Objects["forest-hare"].Properties["ai"])

        match first.Objects["forest-hare"].Properties["ai"] with
        | MapValue ai ->
            match ai["stack"], ai["rngState"] with
            | ListValue [ MapValue frame ], IntegerValue rngState ->
                Assert.Equal(StringValue "wait", frame["kind"])
                Assert.Equal(1015568748L, rngState)
            | _ -> Assert.True(false, "Expected one persisted wait goal and deterministic RNG state.")
        | _ -> Assert.True(false, "Expected the hare to persist an AI state map.")

    [<Fact>]
    let ``Expired creature goal fails and clears its root stack`` () =
        let started = tick ObjectDatabase.initialState 1
        let hare = started.Objects["forest-hare"]

        let expiredAi =
            match hare.Properties["ai"] with
            | MapValue ai ->
                match ai["stack"] with
                | ListValue [ MapValue frame ] ->
                    MapValue(Map.add "stack" (ListValue [ MapValue(Map.add "deadlineTick" (IntegerValue 0L) frame) ]) ai)
                | _ -> failwith "Expected an active goal."
            | _ -> failwith "Expected AI state."

        let expiredState =
            { started with
                Objects =
                    Map.add
                        hare.Id
                        { hare with Properties = Map.add "ai" expiredAi hare.Properties }
                        started.Objects }

        let updated = tick expiredState 1

        match updated.Objects["forest-hare"].Properties["ai"] with
        | MapValue ai ->
            Assert.Equal(ListValue [], ai["stack"])
            match ai["memory"] with
            | MapValue memory -> Assert.Equal(StringValue "failure", memory["lastStatus"])
            | _ -> Assert.True(false, "Expected AI memory map.")
        | _ -> Assert.True(false, "Expected AI state map.")

    [<Fact>]
    let ``Persisted goal deadline is rebased when the process tick index restarts`` () =
        let started = tick ObjectDatabase.initialState 1
        let hare = started.Objects["forest-hare"]

        let persistedAi =
            match hare.Properties["ai"] with
            | MapValue ai ->
                match ai["stack"], ai["memory"] with
                | ListValue [ MapValue frame ], MapValue memory ->
                    MapValue(
                        ai
                        |> Map.add "stack" (ListValue [ MapValue(frame |> Map.add "enteredTick" (IntegerValue 50L) |> Map.add "deadlineTick" (IntegerValue 51L)) ])
                        |> Map.add "memory" (MapValue(Map.add "lastUpdatedTick" (IntegerValue 50L) memory)))
                | _ -> failwith "Expected an active goal and memory."
            | _ -> failwith "Expected AI state."

        let restored =
            { started with
                Objects =
                    Map.add
                        hare.Id
                        { hare with Properties = Map.add "ai" persistedAi hare.Properties }
                        started.Objects }

        let updated =
            match Kernel.tickWorld restored 1 30 (fun _ -> false) with
            | Ok state -> state
            | Error error -> failwith error

        match updated.Objects["forest-hare"].Properties["ai"] with
        | MapValue ai ->
            match ai["stack"], ai["memory"] with
            | ListValue [ MapValue frame ], MapValue memory ->
                Assert.Equal(IntegerValue 2L, frame["deadlineTick"])
                Assert.Equal(IntegerValue 50L, memory["lastRebasedFromTick"])
            | _ -> Assert.True(false, "Expected the rebased goal to remain active.")
        | _ -> Assert.True(false, "Expected AI state map.")
