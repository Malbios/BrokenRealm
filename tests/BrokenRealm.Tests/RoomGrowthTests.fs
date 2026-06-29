namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module RoomGrowthTests =
    let private withWood state amount =
        CarriedItems.addInventory state GameSnapshots.PrototypeCharacterId "wood" amount

    let private inVillage state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" state
        |> fun result -> result.State

    let private roomIds (state: GameState) =
        state.Objects
        |> Map.toList
        |> List.choose (fun (_, gameObject) ->
            if gameObject.LocationId.IsNone && not (PlayerObjects.isPlayer gameObject) then
                Some gameObject.Id
            else
                None)
        |> List.sort

    [<Fact>]
    let ``Build clearing east creates a linked room and allows travel`` () =
        let state = inVillage (withWood ObjectDatabase.initialState 4)

        let built =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "build clearing east" state

        Assert.Equal(0, PlayerObjects.inventory built.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)

        let village = built.State.Objects["village"]

        match village.References |> Map.tryFind "east" with
        | Some clearingId ->
            let clearing = built.State.Objects[clearingId]
            Assert.Equal(Some "village", clearing.References |> Map.tryFind "west")
            Assert.Contains("clearing", clearing.Tags)

            let entered =
                Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go east" built.State

            Assert.Equal(clearingId, PlayerObjects.locationId (PlayerObjects.get entered.State GameSnapshots.PrototypeCharacterId))

            let lines =
                Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "look" entered.State
                |> fun result ->
                    result.Messages
                    |> List.map (ResponseFormatting.localizeMessage result.State En)

            Assert.Contains("You are standing in a grassy clearing.", lines)

            let returned =
                Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go west" entered.State

            Assert.Equal("village", PlayerObjects.locationId (PlayerObjects.get returned.State GameSnapshots.PrototypeCharacterId))
        | None -> Assert.True(false, "Expected village to gain an east exit to a clearing.")

    [<Fact>]
    let ``Build clearing rejects duplicate exits`` () =
        let built =
            Kernel.submitCommandForCharacter
                GameSnapshots.PrototypeCharacterId
                En
                "build clearing east"
                (inVillage (withWood ObjectDatabase.initialState 8))

        let duplicate =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "build clearing east" built.State

        Assert.Equal("build.exit_exists", duplicate.Messages |> List.head |> fun message -> message.Key)
        Assert.Equal(3, roomIds duplicate.State |> List.length)

    [<Fact>]
    let ``German build clearing command works`` () =
        let built =
            Kernel.submitCommandForCharacter
                GameSnapshots.PrototypeCharacterId
                De
                "baue lichtung nach osten"
                (inVillage (withWood ObjectDatabase.initialState 4))

        let line =
            RoomBroadcast.actorMessages built.Messages
            |> List.head
            |> ResponseFormatting.localizeMessage built.State De

        Assert.Equal("Du markierst eine grasbewachsene Lichtung im Osten.", line)

        match built.State.Objects["village"].References |> Map.tryFind "east" with
        | Some _ -> Assert.True(true)
        | None -> Assert.True(false, "Expected a clearing east of the village.")