namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module PlaceableMovementTests =
    let private withWood state amount =
        CarriedItems.addInventory state GameSnapshots.PrototypeCharacterId "wood" amount

    let private craftStool state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "craft stool" state

    let private stoolsIn (state: GameState) (locationId: ObjectId) =
        Kernel.contentsOf state locationId
        |> List.filter (fun gameObject -> gameObject.Tags.Contains "stool")

    let private benchesIn (state: GameState) (locationId: ObjectId) =
        Kernel.contentsOf state locationId
        |> List.filter (fun gameObject -> gameObject.Tags.Contains "bench")

    [<Fact>]
    let ``Push stool north relocates the placeable to the adjacent room`` () =
        let crafted = craftStool (withWood ObjectDatabase.initialState 2)

        Assert.Equal(1, stoolsIn crafted.State "forest" |> List.length)

        let pushed =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "push stool north" crafted.State

        Assert.Empty(stoolsIn pushed.State "forest")
        Assert.Equal(1, stoolsIn pushed.State "village" |> List.length)

        let line =
            RoomBroadcast.actorMessages pushed.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage pushed.State En

        Assert.Equal("You push a wooden stool to the north.", line)

    [<Fact>]
    let ``Push rejects non placeable objects`` () =
        let result = Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "push log north" ObjectDatabase.initialState

        Assert.Equal("move_object.not_here", result.Messages |> List.head |> fun message -> message.Key)
        Assert.Equal(Some "forest", ObjectDatabase.initialState.Objects["fallen-log"].LocationId)

    [<Fact>]
    let ``Move log to village relocates permanent things by destination name`` () =
        let moved =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "move log to village" ObjectDatabase.initialState

        Assert.Equal(Some "village", moved.State.Objects["fallen-log"].LocationId)

        let line =
            RoomBroadcast.actorMessages moved.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage moved.State En

        Assert.Equal("You move a fallen log to village.", line)

    [<Fact>]
    let ``Craft bench consumes three wood and places seating in the room`` () =
        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "craft bench" (withWood ObjectDatabase.initialState 3)

        Assert.Equal(0, PlayerObjects.inventory result.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)
        Assert.Equal(1, benchesIn result.State "forest" |> List.length)

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You craft a wooden bench and set it down.", line)

    [<Fact>]
    let ``German bench craft and push commands work`` () =
        let crafted =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId De "fertige bank" (withWood ObjectDatabase.initialState 3)

        Assert.Equal(1, benchesIn crafted.State "forest" |> List.length)

        let pushed =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId De "schiebe bank nach norden" crafted.State

        Assert.Equal(1, benchesIn pushed.State "village" |> List.length)