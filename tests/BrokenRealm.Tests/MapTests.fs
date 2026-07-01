namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module MapTests =
    let private timestamp = System.DateTimeOffset(2026, 6, 29, 12, 0, 0, System.TimeSpan.Zero)

    let private createSnapshot () =
        (InMemoryGameStore(ObjectDatabase.initialState, fun () -> timestamp)).GetSnapshot()

    let private mockCompile (source: string) =
        ObjectDatabase.initialState.BehaviorModules
        |> Map.toList
        |> List.filter (fun (moduleId, _) -> source.Contains(BehaviorSources.moduleMarkerPrefix + moduleId))
        |> List.sortByDescending (fun (_, behaviorModule) -> behaviorModule.Dependencies.Length)
        |> List.tryHead
        |> function
        | Some(_, behaviorModule) -> Ok behaviorModule.CompiledSource
        | None -> Error [ { message = "Unknown compilation unit."; file = ""; line = 0; column = 0 } ]

    let private mockInspect registryName _compiled =
        ObjectDatabase.initialState.BehaviorModules
        |> Map.toList
        |> List.tryPick (fun (_, behaviorModule) ->
            if behaviorModule.RegistryName = registryName then
                Some behaviorModule.Classes
            else
                None)
        |> function
        | Some classes -> Ok classes
        | None -> Error { message = "Unknown registry."; file = ""; line = 0; column = 0 }

    let private enterPlay state =
        Kernel.tryEnterPlayForCharacter GameSnapshots.PrototypeCharacterId state
        |> function
            | Ok result -> result.State
            | Error error -> failwith error

    let private mapLine state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "map" state
        |> fun result ->
            RoomBroadcast.actorResponseLines result.State En result.Messages
            |> String.concat System.Environment.NewLine

    [<Fact>]
    let ``Hydration restores missing map layout properties on seeded rooms`` () =
        let forest = ObjectDatabase.initialState.Objects["forest"]
        let village = ObjectDatabase.initialState.Objects["village"]

        let withoutMapProperties properties =
            properties
            |> Map.remove RoomMap.MapCodeProperty
            |> Map.remove RoomMap.MapRegionProperty
            |> Map.remove RoomMap.MapXProperty
            |> Map.remove RoomMap.MapYProperty

        let baseSnapshot = createSnapshot ()

        let snapshot =
            { baseSnapshot with
                World =
                    { baseSnapshot.World with
                        Objects =
                            baseSnapshot.World.Objects
                            |> Map.add "forest" { forest with Properties = withoutMapProperties forest.Properties }
                            |> Map.add "village" { village with Properties = withoutMapProperties village.Properties } } }

        match SnapshotHydration.hydrate mockCompile mockInspect snapshot with
        | Ok(state, _) ->
            let hydratedForest = state.Objects["forest"]
            let hydratedVillage = state.Objects["village"]

            Assert.Equal(Some "FO", hydratedForest.Properties |> Map.tryFind RoomMap.MapCodeProperty |> Option.bind (function StringValue value -> Some value | _ -> None))
            Assert.Equal(Some "VI", hydratedVillage.Properties |> Map.tryFind RoomMap.MapCodeProperty |> Option.bind (function StringValue value -> Some value | _ -> None))
            Assert.Equal(Some -1, hydratedVillage.Properties |> Map.tryFind RoomMap.MapYProperty |> Option.bind (function IntegerValue value -> Some(int value) | _ -> None))
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Seed rooms expose map layout properties`` () =
        let forest = ObjectDatabase.initialState.Objects["forest"]
        let village = ObjectDatabase.initialState.Objects["village"]

        Assert.Equal(Some "FO", forest.Properties |> Map.tryFind RoomMap.MapCodeProperty |> Option.bind (function StringValue value -> Some value | _ -> None))
        Assert.Equal(Some "VI", village.Properties |> Map.tryFind RoomMap.MapCodeProperty |> Option.bind (function StringValue value -> Some value | _ -> None))
        Assert.Equal(Some -1, village.Properties |> Map.tryFind RoomMap.MapYProperty |> Option.bind (function IntegerValue value -> Some(int value) | _ -> None))

    [<Fact>]
    let ``Entering play records the current room as visited`` () =
        let limbo =
            Limbo.enterLimbo ObjectDatabase.initialState GameSnapshots.PrototypeCharacterId
            |> Result.defaultWith failwith

        let state = enterPlay limbo
        let player = PlayerObjects.get state GameSnapshots.PrototypeCharacterId

        Assert.Contains("forest", PlayerObjects.visitedRoomIds player)

    [<Fact>]
    let ``Map command shows the current room and fog for unvisited rooms`` () =
        let state = enterPlay ObjectDatabase.initialState
        let line = mapLine state

        Assert.Contains("[FO]", line)
        Assert.Contains("??", line)

    [<Fact>]
    let ``Moving north reveals the village on the map`` () =
        let state =
            enterPlay ObjectDatabase.initialState
            |> fun current ->
                Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" current
                |> fun result -> result.State

        let line = mapLine state

        Assert.Contains("[VI]", line)
        Assert.Contains("FO", line)
        Assert.DoesNotContain("??", line)

    [<Fact>]
    let ``Grow room exit assigns map coordinates east of the village`` () =
        let withWood state amount = CarriedItems.addInventory state GameSnapshots.PrototypeCharacterId "wood" amount

        let built =
            Kernel.submitCommandForCharacter
                GameSnapshots.PrototypeCharacterId
                En
                "build clearing east"
                (withWood
                    (Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" (enterPlay ObjectDatabase.initialState)).State
                    4)

        match built.State.Objects["village"].References |> Map.tryFind "east" with
        | Some clearingId ->
            let clearing = built.State.Objects[clearingId]

            Assert.Equal(
                Some 1,
                clearing.Properties
                |> Map.tryFind RoomMap.MapXProperty
                |> Option.bind (function IntegerValue value -> Some(int value) | _ -> None))

            Assert.Equal(
                Some -1,
                clearing.Properties
                |> Map.tryFind RoomMap.MapYProperty
                |> Option.bind (function IntegerValue value -> Some(int value) | _ -> None))

            Assert.Equal(
                Some "CL",
                clearing.Properties
                |> Map.tryFind RoomMap.MapCodeProperty
                |> Option.bind (function StringValue value -> Some value | _ -> None))
        | None -> Assert.True(false, "Expected a clearing east of the village.")