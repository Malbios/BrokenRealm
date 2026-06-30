namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module ContainerTests =
    let private withWood state amount =
        CarriedItems.addInventory state GameSnapshots.PrototypeCharacterId "wood" amount

    let private withStrongboxKey state =
        CarriedItems.addInventory state GameSnapshots.PrototypeCharacterId "strongbox-key" 1

    let private inVillage state =
        let player = PlayerObjects.get state GameSnapshots.PrototypeCharacterId
        { state with Objects = Map.add player.Id (PlayerObjects.withLocation player "village") state.Objects }

    let private crateItems (state: GameState) =
        CarriedItems.itemQuantitiesInContainer state "village-crate"

    [<Fact>]
    let ``Put wood in crate stores items inside the container`` () =
        let state = inVillage (withWood ObjectDatabase.initialState 3)

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "put 2 wood in crate" state

        Assert.Equal(1, PlayerObjects.inventory result.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)
        Assert.Equal(2, crateItems result.State |> Map.tryFind "wood" |> Option.defaultValue 0)

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You put 2 wood in a wooden crate.", line)

    [<Fact>]
    let ``Open crate reports stored items`` () =
        let stored =
            inVillage (withWood ObjectDatabase.initialState 2)
            |> fun state ->
                Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "put wood in crate" state

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "open crate" stored.State

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("Inside: 1 wood. (1/4)", line)

    [<Fact>]
    let ``Take wood from crate moves items back to inventory`` () =
        let stored =
            inVillage (withWood ObjectDatabase.initialState 2)
            |> fun state ->
                Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "put wood in crate" state

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "take wood from crate" stored.State

        Assert.Equal(2, PlayerObjects.inventory result.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)
        Assert.Equal(0, crateItems result.State |> Map.tryFind "wood" |> Option.defaultValue 0)

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You take 1 wood from a wooden crate.", line)

    [<Fact>]
    let ``German container commands work`` () =
        let state = inVillage (withWood ObjectDatabase.initialState 1)

        let putResult =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId De "lege holz in kiste" state

        Assert.Equal(1, crateItems putResult.State |> Map.tryFind "wood" |> Option.defaultValue 0)

        let openResult =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId De "öffne kiste" putResult.State

        let openLine =
            RoomBroadcast.actorMessages openResult.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage openResult.State De

        Assert.Equal("Inhalt: 1 Holz. (1/4)", openLine)

    [<Fact>]
    let ``Put rejects items when the container is at capacity`` () =
        let state = inVillage (withWood ObjectDatabase.initialState 5)

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "put 5 wood in crate" state

        let message = result.Messages |> List.head
        Assert.Equal("container.capacity.full", message.Key)
        Assert.Equal(0, crateItems result.State |> Map.tryFind "wood" |> Option.defaultValue 0)

    [<Fact>]
    let ``Locked strongbox rejects open without a key item`` () =
        let state = inVillage ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "open strongbox" state

        Assert.Equal("container.locked", result.Messages |> List.head |> fun message -> message.Key)

    [<Fact>]
    let ``Locked strongbox opens when the actor carries the key item`` () =
        let state = inVillage (withStrongboxKey ObjectDatabase.initialState)

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "open strongbox" state

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("It is empty. (0/2)", line)

    [<Fact>]
    let ``Put into locked strongbox is rejected without the key item`` () =
        let state = inVillage ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "put wood in strongbox" state

        Assert.Equal("container.locked", result.Messages |> List.head |> fun message -> message.Key)

    [<Fact>]
    let ``Put from another room does not match a distant crate`` () =
        let state = withWood ObjectDatabase.initialState 1

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "put wood in crate" state

        let message = result.Messages |> List.head
        Assert.Equal("command.unknown", message.Key)