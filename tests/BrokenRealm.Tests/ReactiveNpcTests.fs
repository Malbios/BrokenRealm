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
    let ``Talk to working farmer returns working dialogue`` () =
        let state =
            inVillage (farmerWithActivity ObjectDatabase.initialState "working")

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "talk to farmer" state.State

        let line =
            result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("The farmer wipes his brow. \"Let me finish stocking this crate.\"", line)

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