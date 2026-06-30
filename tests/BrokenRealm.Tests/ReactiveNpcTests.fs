namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module ReactiveNpcTests =
    let private inVillage state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" state

    let private farmerWithActivity state activity =
        let farmer = state.Objects["village-farmer"]

        { state with
            Objects =
                Map.add
                    farmer.Id
                    { farmer with Properties = farmer.Properties |> Map.add "activity" (StringValue activity) }
                    state.Objects }

    [<Fact>]
    let ``Talk to working farmer pauses work and returns interrupted dialogue`` () =
        let state =
            inVillage (farmerWithActivity ObjectDatabase.initialState "working")

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "talk to farmer" state.State

        let line =
            result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("The farmer pauses and wipes his brow. \"This can wait.\"", line)

        let farmer = result.State.Objects["village-farmer"]

        match farmer.Properties |> Map.tryFind "activity" with
        | Some(StringValue "idle") -> Assert.True(true)
        | _ -> Assert.True(false, "Expected the farmer to stop working after being spoken to.")

    [<Fact>]
    let ``Talk to resting farmer returns resting dialogue`` () =
        let state =
            inVillage (farmerWithActivity ObjectDatabase.initialState "resting")

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "talk to farmer" state.State

        let line =
            result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("The farmer stretches. \"A moment's rest never hurt.\"", line)

    [<Fact>]
    let ``Entering village notices a working farmer`` () =
        let state = farmerWithActivity ObjectDatabase.initialState "working"

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" state

        let lines =
            result.Messages
            |> List.map (ResponseFormatting.localizeMessage result.State En)

        Assert.Contains("The farmer glances up from his work.", lines)

    [<Fact>]
    let ``Entering forest startles a hare`` () =
        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go south" inVillage.State

        let lines =
            result.Messages
            |> List.map (ResponseFormatting.localizeMessage result.State En)

        Assert.Contains("A forest hare startles and bounds into the undergrowth.", lines)
        Assert.Equal(Some "village", result.State.Objects["forest-hare"].LocationId)