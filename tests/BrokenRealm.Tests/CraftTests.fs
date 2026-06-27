namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module CraftTests =
    let private withWood state amount =
        let playerId = GameSnapshots.PrototypeCharacterId
        CarriedItems.addInventory state playerId "wood" amount

    let private stoolsIn (state: GameState) (locationId: ObjectId) =
        Kernel.contentsOf state locationId
        |> List.filter (fun gameObject -> gameObject.Tags.Contains "stool")

    [<Fact>]
    let ``Craft stool consumes wood and places a stool in the room`` () =
        let state = withWood ObjectDatabase.initialState 4

        let result = Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "craft stool" state

        Assert.Equal(2, PlayerObjects.inventory result.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)
        Assert.Equal(1, stoolsIn result.State "forest" |> List.length)

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You craft a wooden stool and set it down.", line)

    [<Fact>]
    let ``Craft stool rejects insufficient wood`` () =
        let state = withWood ObjectDatabase.initialState 1

        let result = Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "make stool" state

        let message = result.Messages |> List.head
        Assert.Equal("craft.insufficient", message.Key)
        Assert.Empty(stoolsIn result.State "forest")

    [<Fact>]
    let ``German craft command matches hocker recipe`` () =
        let state = withWood ObjectDatabase.initialState 2

        let result = Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId De "fertige hocker" state

        Assert.Equal(0, PlayerObjects.inventory result.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)
        Assert.Equal(1, stoolsIn result.State "forest" |> List.length)

    [<Fact>]
    let ``Use stool reports localized interaction`` () =
        let crafted =
            Kernel.submitCommandForCharacter
                GameSnapshots.PrototypeCharacterId
                En
                "craft stool"
                (withWood ObjectDatabase.initialState 2)

        let result = Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "use stool" crafted.State

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You sit on the wooden stool for a moment.", line)

    [<Fact>]
    let ``Limbo all players clears every seeded player location`` () =
        let state = Limbo.limboAllPlayers ObjectDatabase.initialState

        PlayerObjects.playersByAccount state GameSnapshots.PrototypeAccountId
        |> List.iter (fun player ->
            Assert.True(PlayerObjects.isInLimbo player)
            Assert.True(PlayerObjects.lastSafeLocationId player |> Option.isSome))