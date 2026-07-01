namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module InterruptTests =
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
    let ``Delivered talk interrupt clears farmer work stack and stores resume memory`` () =
        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" (farmerWithWorkGoal ObjectDatabase.initialState)

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "talk to farmer" inVillage.State

        let farmer = result.State.Objects["village-farmer"]

        match farmer.Properties |> Map.tryFind "ai" with
        | Some(MapValue ai) ->
            Assert.Equal(ListValue [], ai["stack"])

            match ai |> Map.tryFind "memory" with
            | Some(MapValue memory) ->
                match memory |> Map.tryFind "interruptedWork" with
                | Some(BooleanValue true) -> Assert.True(true)
                | _ -> Assert.True(false, "Expected interruptedWork after a talk interrupt.")
            | _ -> Assert.True(false, "Expected farmer AI memory after talk interrupt.")
        | _ -> Assert.True(false, "Expected farmer AI state after talk interrupt.")

    [<Fact>]
    let ``Player entering forest delivers flee interrupt to the hare`` () =
        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go south" inVillage.State

        Assert.Equal(Some "village", result.State.Objects["forest-hare"].LocationId)

        match result.State.Objects["forest-hare"].Properties |> Map.tryFind "ai" with
        | Some(MapValue ai) ->
            Assert.Equal(ListValue [], ai["stack"])

            match ai |> Map.tryFind "memory" with
            | Some(MapValue memory) ->
                match memory |> Map.tryFind "startled" with
                | Some(BooleanValue true) -> Assert.True(true)
                | _ -> Assert.True(false, "Expected the hare to remember being startled.")
            | _ -> Assert.True(false, "Expected hare AI memory after flee interrupt.")
        | _ -> Assert.True(false, "Expected hare AI state after flee interrupt.")