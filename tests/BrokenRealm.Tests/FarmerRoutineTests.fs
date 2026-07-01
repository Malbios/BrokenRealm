namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module FarmerRoutineTests =
    let private tick state times =
        List.fold
            (fun current index ->
                match Kernel.tickWorld current (index + 1) 30 (fun _ -> false) with
                | Ok updated -> updated
                | Error error -> Assert.True(false, error); current)
            state
            [ for index in 0 .. times - 1 -> index ]

    let private crateWood (state: GameState) =
        CarriedItems.itemQuantitiesInContainer state "village-crate"
        |> Map.tryFind "wood"
        |> Option.defaultValue 0

    let private farmerWithWorkGoal (state: GameState) =
        let farmer = state.Objects["village-farmer"]

        let workAi =
            MapValue(
                Map.ofList
                    [ "rootGoal", StringValue "farmerLife"
                      "stack",
                      ListValue
                          [ MapValue(
                                Map.ofList
                                    [ "id", StringValue "goal-1"
                                      "kind", StringValue "work"
                                      "parentId", NullValue
                                      "enteredTick", IntegerValue 1L
                                      "deadlineTick", IntegerValue 2L
                                      "parameters", MapValue(Map.ofList [ "targetContainer", StringValue "village-crate" ]) ]) ]
                      "memory", MapValue(Map.ofList [ "activity", StringValue "working" ])
                      "rngState", IntegerValue 7L
                      "nextGoalId", IntegerValue 2L ])

        { state with
            Objects =
                Map.add
                    farmer.Id
                    { farmer with
                        Properties =
                            farmer.Properties
                            |> Map.add "ai" workAi
                            |> Map.add "activity" (StringValue "working") }
                    state.Objects }

    [<Fact>]
    let ``Farmer work goal stocks the village crate`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        Assert.Equal(0, crateWood limboState)

        let prepared = farmerWithWorkGoal limboState
        let updated = tick prepared 2

        Assert.True(crateWood updated >= 1, "Expected the farmer to stock wood in the village crate.")

    [<Fact>]
    let ``Examine working farmer reports activity`` () =
        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" (farmerWithWorkGoal ObjectDatabase.initialState)

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "examine farmer" inVillage.State

        let line =
            result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("The farmer works at a wooden crate.", line)

    [<Fact>]
    let ``Farmer resumes work after an interrupted conversation`` () =
        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" (farmerWithWorkGoal ObjectDatabase.initialState)

        let interrupted =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "talk to farmer" inVillage.State

        let farmer = interrupted.State.Objects["village-farmer"]

        match farmer.Properties |> Map.tryFind "activity" with
        | Some(StringValue "idle") -> Assert.True(true)
        | _ -> Assert.True(false, "Expected the farmer to return to idle after being interrupted.")

        match farmer.Properties |> Map.tryFind "ai" with
        | Some(MapValue ai) ->
            match ai |> Map.tryFind "memory" with
            | Some(MapValue memory) ->
                match memory |> Map.tryFind "interruptedWork" with
                | Some(BooleanValue true) -> Assert.True(true)
                | _ -> Assert.True(false, "Expected interruptedWork to be stored in farmer AI memory.")
            | _ -> Assert.True(false, "Expected farmer AI memory after interruption.")
        | _ -> Assert.True(false, "Expected farmer AI state after interruption.")

        let resumed = tick interrupted.State 1
        let resumedFarmer = resumed.Objects["village-farmer"]

        match resumedFarmer.Properties |> Map.tryFind "activity" with
        | Some(StringValue "working") -> Assert.True(true)
        | _ -> Assert.True(false, "Expected the farmer to resume working on the next tick.")