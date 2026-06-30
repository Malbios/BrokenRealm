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