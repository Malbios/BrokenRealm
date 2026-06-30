namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module WorldEcologyTests =
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

    let private woodYield (state: GameState) =
        match state.Objects["forest"].Properties |> Map.tryFind "woodYield" with
        | Some(IntegerValue value) -> int value
        | _ -> 0

    [<Fact>]
    let ``Forest wood yield regrows on world tick`` () =
        let depleted =
            List.fold
                (fun state _ ->
                    Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "gather wood" state
                    |> fun result -> result.State)
                ObjectDatabase.initialState
                [ for _ in 1 .. 5 -> () ]

        Assert.Equal(0, woodYield depleted)

        let regrown = tick depleted 1
        Assert.Equal(1, woodYield regrown)

    [<Fact>]
    let ``Gather rejects depleted forest wood`` () =
        let depleted =
            List.fold
                (fun state _ ->
                    Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "gather wood" state
                    |> fun result -> result.State)
                ObjectDatabase.initialState
                [ for _ in 1 .. 5 -> () ]

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "gather wood" depleted

        Assert.Equal("gather.depleted", result.Messages |> List.head |> fun message -> message.Key)

    [<Fact>]
    let ``Talk to farmer dispatches to humanoid creature behavior`` () =
        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" ObjectDatabase.initialState

        match
            CommandMatching.tryMatchForCharacter GameSnapshots.PrototypeCharacterId En "talk to farmer" inVillage.State
        with
        | CommandMatching.Matched matched ->
            Assert.Equal("village-farmer", matched.ObjectId)
            Assert.Equal("talk", matched.MethodName)
            Assert.Equal("FarmerCreatureBehavior", matched.BehaviorClassName)
        | CommandMatching.Ambiguous _ -> Assert.True(false, "Expected a direct talk match.")
        | CommandMatching.MatchedSequence _ -> Assert.True(false, "Expected a direct talk match.")
        | CommandMatching.NoMatch -> Assert.True(false, "Expected talk to farmer to match.")

    [<Fact>]
    let ``Talk to farmer returns localized greeting`` () =
        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "talk to farmer" inVillage.State

        let line =
            result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("The farmer nods. \"Plenty of work to do around here.\"", line)

    [<Fact>]
    let ``Player hunger increases during world tick`` () =
        let updated = tick ObjectDatabase.initialState 1
        let player = updated.Objects[GameSnapshots.PrototypeCharacterId]

        match player.Properties |> Map.tryFind "hunger" with
        | Some(IntegerValue 1L) -> Assert.True(true)
        | _ -> Assert.True(false, "Expected player hunger to increase while in the world.")

    [<Fact>]
    let ``Inventory reports hunger when player is hungry`` () =
        let hungry =
            { ObjectDatabase.initialState.Objects[GameSnapshots.PrototypeCharacterId] with
                Properties =
                    ObjectDatabase.initialState.Objects[GameSnapshots.PrototypeCharacterId].Properties
                    |> Map.add "hunger" (IntegerValue 60L) }
            |> fun player ->
                { ObjectDatabase.initialState with
                    Objects = Map.add GameSnapshots.PrototypeCharacterId player ObjectDatabase.initialState.Objects }

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "inventory" hungry

        let lines =
            result.Messages
            |> List.map (ResponseFormatting.localizeMessage result.State En)

        Assert.Contains("You feel hungry.", lines)

    [<Fact>]
    let ``Inventory reports starvation at high hunger`` () =
        let starving =
            { ObjectDatabase.initialState.Objects[GameSnapshots.PrototypeCharacterId] with
                Properties =
                    ObjectDatabase.initialState.Objects[GameSnapshots.PrototypeCharacterId].Properties
                    |> Map.add "hunger" (IntegerValue 85L) }
            |> fun player ->
                { ObjectDatabase.initialState with
                    Objects = Map.add GameSnapshots.PrototypeCharacterId player ObjectDatabase.initialState.Objects }

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "inventory" starving

        let lines =
            result.Messages
            |> List.map (ResponseFormatting.localizeMessage result.State En)

        Assert.Contains("You are starving.", lines)

    [<Fact>]
    let ``Eat wood reduces hunger`` () =
        let hungry =
            CarriedItems.addInventory ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId "wood" 2
            |> fun state ->
                let player = state.Objects[GameSnapshots.PrototypeCharacterId]

                { state with
                    Objects =
                        Map.add
                            player.Id
                            { player with
                                Properties = player.Properties |> Map.add "hunger" (IntegerValue 60L) }
                            state.Objects }

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "eat wood" hungry

        let player = result.State.Objects[GameSnapshots.PrototypeCharacterId]

        match player.Properties |> Map.tryFind "hunger" with
        | Some(IntegerValue 40L) -> Assert.True(true)
        | _ -> Assert.True(false, "Expected eating wood to reduce hunger by 20.")

        Assert.Equal(1, PlayerObjects.inventory result.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)

    [<Fact>]
    let ``Limbo players do not gain hunger on world tick`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        let updated = tick limboState 3
        let player = updated.Objects[GameSnapshots.PrototypeCharacterId]

        match player.Properties |> Map.tryFind "hunger" with
        | Some(IntegerValue 0L) -> Assert.True(true)
        | None -> Assert.True(true)
        | _ -> Assert.True(false, "Expected limbo players to skip hunger ticks.")
