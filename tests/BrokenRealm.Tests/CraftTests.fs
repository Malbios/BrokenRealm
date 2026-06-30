namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module CraftTests =
    let private withWood state amount =
        let playerId = GameSnapshots.PrototypeCharacterId
        CarriedItems.addInventory state playerId "wood" amount

    let private inVillage state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" state
        |> fun result -> result.State

    let private craftStool state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "craft stool at workbench" (inVillage state)

    let private stoolsIn (state: GameState) (locationId: ObjectId) =
        Kernel.contentsOf state locationId
        |> List.filter (fun gameObject -> gameObject.Tags.Contains "stool")

    [<Fact>]
    let ``Craft stool consumes wood and places a stool in the room`` () =
        let result = craftStool (withWood ObjectDatabase.initialState 4)

        Assert.Equal(2, PlayerObjects.inventory result.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)
        Assert.Equal(1, stoolsIn result.State "village" |> List.length)

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You craft a wooden stool and set it down.", line)

    [<Fact>]
    let ``Craft stool rejects insufficient wood`` () =
        let result =
            Kernel.submitCommandForCharacter
                GameSnapshots.PrototypeCharacterId
                En
                "make stool at workbench"
                (inVillage (withWood ObjectDatabase.initialState 1))

        let message = result.Messages |> List.head
        Assert.Equal("craft.insufficient", message.Key)
        Assert.Empty(stoolsIn result.State "village")

    [<Fact>]
    let ``German craft command matches hocker recipe`` () =
        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId De "gehe nach norden" ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter
                GameSnapshots.PrototypeCharacterId
                De
                "fertige hocker an werkbank"
                (withWood inVillage.State 2)

        Assert.Equal(0, PlayerObjects.inventory result.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)
        Assert.Equal(1, stoolsIn result.State "village" |> List.length)

    [<Fact>]
    let ``Use stool reports localized interaction`` () =
        let crafted = craftStool (withWood ObjectDatabase.initialState 2)

        let result = Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "use stool" crafted.State

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You sit on the wooden stool for a moment.", line)

    let private crateWood (state: GameState) =
        CarriedItems.itemQuantitiesInContainer state "village-crate"
        |> Map.tryFind "wood"
        |> Option.defaultValue 0

    [<Fact>]
    let ``Craft strongbox key can consume wood from the village crate`` () =
        let stocked =
            inVillage (withWood ObjectDatabase.initialState 2)
            |> fun state ->
                Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "put 2 wood in crate" state

        Assert.Equal(2, crateWood stocked.State)

        let result =
            Kernel.submitCommandForCharacter
                GameSnapshots.PrototypeCharacterId
                En
                "craft strongbox-key at workbench"
                stocked.State

        Assert.Equal(0, crateWood result.State)
        Assert.Equal(1, PlayerObjects.inventory result.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "strongbox-key" |> Option.defaultValue 0)

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You carve a strongbox key from spare wood.", line)

    [<Fact>]
    let ``Limbo all players clears every seeded player location`` () =
        let state = Limbo.limboAllPlayers ObjectDatabase.initialState

        PlayerObjects.playersByAccount state GameSnapshots.PrototypeAccountId
        |> List.iter (fun player ->
            Assert.True(PlayerObjects.isInLimbo player)
            Assert.True(PlayerObjects.lastSafeLocationId player |> Option.isSome))