namespace BrokenRealm.Tests

open System
open System.IO
open BrokenRealm.Server
open Xunit

module LegacySnapshotTests =
    let private fixturePath =
        Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", "legacy-missing-player-behaviors.snapshot.json")

    let private contentRoot =
        Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "BrokenRealm.Server"))

    let private mockCompile (source: string) =
        ObjectDatabase.initialState.BehaviorModules
        |> Map.toList
        |> List.filter (fun (moduleId, _) ->
            source.Contains(BehaviorSources.moduleMarkerPrefix + moduleId))
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

    let private loadLegacySnapshot () =
        SnapshotCodec.tryReadFile fixturePath
        |> Result.bind SnapshotMigrations.migrate
        |> Result.bind (SnapshotHydration.hydrate (ScriptCompiler.compile contentRoot) Scripting.inspectBehaviorModule)

    [<Fact>]
    let ``Checked-in legacy snapshot fixture is repaired and supports look after enter play`` () =
        Assert.True(File.Exists fixturePath, $"Missing fixture: {fixturePath}")

        match loadLegacySnapshot () with
        | Ok(state, _) ->
            Assert.True(state.BehaviorModules.ContainsKey "player-behaviors")

            let limboState = Limbo.limboAllPlayers state

            match Kernel.tryEnterPlayForCharacter GameSnapshots.PrototypeCharacterId limboState with
            | Error error -> Assert.True(false, error)
            | Ok enterResult ->
                let looked =
                    Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "look" enterResult.State

                let lines = looked.Messages |> List.map (ResponseFormatting.localizeMessage looked.State En)
                Assert.NotEmpty(lines)
                Assert.Contains(lines, fun line -> line.Contains("forest"))
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Game store bootstrap loads the legacy snapshot fixture without crashing`` () =
        let directory = Path.Combine(Path.GetTempPath(), "brokenrealm-legacy-" + Guid.NewGuid().ToString("N"))
        let snapshotCopy = Path.Combine(directory, "game-snapshot.json")

        try
            Directory.CreateDirectory(directory) |> ignore
            File.Copy(fixturePath, snapshotCopy)

            let store = GameStoreBootstrap.createGameStore contentRoot snapshotCopy
            let player = PlayerObjects.get (store.Read().State) GameSnapshots.PrototypeCharacterId
            Assert.True(PlayerObjects.isInLimbo player)
            Assert.True(store.Read().State.BehaviorModules.ContainsKey "player-behaviors")
            Assert.True(store.GetSnapshot().World.BehaviorModules.ContainsKey "player-behaviors")
        finally
            if Directory.Exists directory then
                Directory.Delete(directory, true)