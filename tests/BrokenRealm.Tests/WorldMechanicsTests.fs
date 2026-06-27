namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module WorldMechanicsTests =
    let private withWood state amount =
        CarriedItems.addInventory state GameSnapshots.PrototypeCharacterId "wood" amount

    let private stoolsIn (state: GameState) (locationId: ObjectId) =
        Kernel.contentsOf state locationId
        |> List.filter (fun gameObject -> gameObject.Tags.Contains "stool")

    [<Fact>]
    let ``Dismantle stool destroys the object and returns wood`` () =
        let crafted =
            Kernel.submitCommandForCharacter
                GameSnapshots.PrototypeCharacterId
                En
                "craft stool"
                (withWood ObjectDatabase.initialState 2)

        Assert.Equal(1, stoolsIn crafted.State "forest" |> List.length)

        let dismantled =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "dismantle stool" crafted.State

        Assert.Empty(stoolsIn dismantled.State "forest")
        Assert.Equal(1, PlayerObjects.inventory dismantled.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)

    [<Fact>]
    let ``Village look reacts to crafted seating`` () =
        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" ObjectDatabase.initialState

        let crafted =
            Kernel.submitCommandForCharacter
                GameSnapshots.PrototypeCharacterId
                En
                "craft stool"
                (withWood inVillage.State 2)

        let looked = Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "look" crafted.State

        let lines =
            looked.Messages
            |> List.map (ResponseFormatting.localizeMessage looked.State En)

        Assert.Contains("Seating has been set out here.", lines)

    [<Fact>]
    let ``Village tick syncs comfort from room contents`` () =
        let inVillage =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" ObjectDatabase.initialState

        let crafted =
            Kernel.submitCommandForCharacter
                GameSnapshots.PrototypeCharacterId
                En
                "craft stool"
                (withWood inVillage.State 2)

        match Kernel.tickWorld crafted.State (fun _ -> true) with
        | Ok updated ->
            let village = updated.Objects["village"]

            match village.Properties |> Map.tryFind "comfort" with
            | Some(IntegerValue 1L) -> Assert.True(true)
            | _ -> Assert.True(false, "Expected village comfort to sync to 1 when a stool is present.")
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``World tick advances forest tickCount only for connected in play players`` () =
        let limboState =
            match Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId with
            | Ok state -> state
            | Error error -> Assert.True(false, error); ObjectDatabase.initialState

        match Kernel.tickWorld limboState (fun _ -> true) with
        | Ok updated ->
            let forest = updated.Objects["forest"]

            match forest.Properties |> Map.tryFind "tickCount" with
            | Some(IntegerValue 0L) -> Assert.True(true)
            | _ -> Assert.True(false, "Expected forest tickCount to remain unchanged without in-play players.")
        | Error error -> Assert.True(false, error)

        match Kernel.tickWorld ObjectDatabase.initialState (fun _ -> true) with
        | Ok updated ->
            let forest = updated.Objects["forest"]

            match forest.Properties |> Map.tryFind "tickCount" with
            | Some(IntegerValue value) when value >= 1L -> Assert.True(true)
            | _ -> Assert.True(false, "Expected forest tickCount to advance for connected in-play players.")
        | Error error -> Assert.True(false, error)