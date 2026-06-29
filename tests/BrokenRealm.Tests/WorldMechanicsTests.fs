namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module WorldMechanicsTests =
    let private withWood state amount =
        CarriedItems.addInventory state GameSnapshots.PrototypeCharacterId "wood" amount

    let private inVillage state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" state
        |> fun result -> result.State

    let private craftStool state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "craft stool at workbench" (inVillage state)

    let private stoolsIn (state: GameState) (locationId: ObjectId) =
        Kernel.contentsOf state locationId
        |> List.filter (fun gameObject -> gameObject.Tags.Contains "stool")

    [<Fact>]
    let ``Dismantle stool destroys the object and returns wood`` () =
        let crafted = craftStool (withWood ObjectDatabase.initialState 2)

        Assert.Equal(1, stoolsIn crafted.State "village" |> List.length)

        let dismantled =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "dismantle stool" crafted.State

        Assert.Empty(stoolsIn dismantled.State "village")
        Assert.Equal(1, PlayerObjects.inventory dismantled.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)

    [<Fact>]
    let ``Village look reacts to crafted seating`` () =
        let crafted = craftStool (withWood ObjectDatabase.initialState 2)

        let synced =
            match Kernel.tickWorld crafted.State 1 30 (fun _ -> true) with
            | Ok state -> state
            | Error error -> Assert.True(false, error); crafted.State

        let looked = Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "look" synced

        let lines =
            looked.Messages
            |> List.map (ResponseFormatting.localizeMessage looked.State En)

        Assert.Contains("The village feels more welcoming.", lines)

    [<Fact>]
    let ``Village tick syncs comfort from room contents`` () =
        let crafted = craftStool (withWood ObjectDatabase.initialState 2)

        match Kernel.tickWorld crafted.State 1 30 (fun _ -> true) with
        | Ok updated ->
            let village = updated.Objects["village"]

            match village.Properties |> Map.tryFind "comfort" with
            | Some(IntegerValue 1L) -> Assert.True(true)
            | _ -> Assert.True(false, "Expected village comfort to sync to 1 when a stool is present.")
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``World tick advances forest tickCount for all rooms`` () =
        match Kernel.tickWorld ObjectDatabase.initialState 1 30 (fun _ -> false) with
        | Ok updated ->
            let forest = updated.Objects["forest"]

            match forest.Properties |> Map.tryFind "tickCount" with
            | Some(IntegerValue value) when value >= 1L -> Assert.True(true)
            | _ -> Assert.True(false, "Expected forest tickCount to advance during autonomous world tick.")
        | Error error -> Assert.True(false, error)