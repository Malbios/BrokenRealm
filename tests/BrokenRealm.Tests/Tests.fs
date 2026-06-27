namespace BrokenRealm.Tests

open System
open System.IO
open BrokenRealm.Server
open Xunit

[<AutoOpen>]
module GameStateTestCompatibility =
    type CharacterView =
        { Id: CharacterId
          AccountId: AccountId
          LocationId: ObjectId
          Inventory: Map<ItemId, Quantity> }

    let private toView state (player: GameObject) =
        { Id = player.Id
          AccountId = PlayerObjects.accountId player
          LocationId = PlayerObjects.locationId player
          Inventory = PlayerObjects.inventory state player.Id }

    let toPlayerObject (view: CharacterView) =
        PlayerObjects.createWithLegacyInventory view.Id view.Id view.Id view.AccountId view.LocationId view.Inventory

    type GameState with
        member state.Player = toView state (PlayerObjects.get state GameSnapshots.PrototypeCharacterId)

        member state.WithPlayer(playerId, updater: GameObject -> GameObject) =
            let player = PlayerObjects.get state playerId
            { state with Objects = Map.add playerId (updater player) state.Objects }

    let testActor = ObjectDatabase.initialState.Objects[GameSnapshots.PrototypeCharacterId]

module PersistenceTests =
    let private timestamp = DateTimeOffset(2026, 6, 27, 12, 0, 0, TimeSpan.Zero)
    let private createStore () = InMemoryGameStore(ObjectDatabase.initialState, fun () -> timestamp)

    [<Fact>]
    let ``Snapshot contains authoritative source and version metadata`` () =
        let snapshot = (createStore ()).GetSnapshot()
        let forest = snapshot.World.BehaviorModules["forest-behaviors"]

        Assert.Equal(GameSnapshots.CurrentFormatVersion, snapshot.FormatVersion)
        Assert.True(snapshot.World.Objects.ContainsKey GameSnapshots.PrototypeCharacterId)
        Assert.True(PlayerObjects.isPlayer snapshot.World.Objects[GameSnapshots.PrototypeCharacterId])
        Assert.Equal(BehaviorSources.forest, forest.Source)
        Assert.Equal(0L, forest.SourceRevision)
        Assert.Equal(0L, forest.ActivationRevision)
        Assert.Equal(timestamp, forest.ActivatedAt)
        Assert.Equal<GameValue>(
            ObjectDatabase.initialState.Objects["forest"].Properties["trailToken"],
            snapshot.World.Objects["forest"].Properties["trailToken"])

    [<Fact>]
    let ``Character-only commit advances only character revision`` () =
        let store = createStore ()
        let stored = store.Read()
        let result = Kernel.submitCommand En "gather wood" stored.State

        match store.TryCommit(stored.WorldRevision, stored.CharacterRevisions, result.State) with
        | Ok committed ->
            Assert.Equal(0L, committed.WorldRevision)
            Assert.Equal(1L, committed.CharacterRevisions[GameSnapshots.PrototypeCharacterId])
            Assert.Equal(2, committed.State.Player.Inventory["wood"])
        | Error _ -> Assert.True(false, "Expected the character commit to succeed.")

    [<Fact>]
    let ``World commit advances world revision and preserves character revision`` () =
        let store = createStore ()
        let stored = store.Read()
        let result = Kernel.submitCommand En "name trail green way" stored.State

        match store.TryCommit(stored.WorldRevision, stored.CharacterRevisions, result.State) with
        | Ok committed ->
            Assert.Equal(1L, committed.WorldRevision)
            Assert.Equal(0L, committed.CharacterRevisions[GameSnapshots.PrototypeCharacterId])
        | Error _ -> Assert.True(false, "Expected the world commit to succeed.")

    [<Fact>]
    let ``Behavior source commit advances source and activation revisions`` () =
        let store = createStore ()
        let stored = store.Read()
        let behavior = stored.State.BehaviorModules["forest-behaviors"]
        let changedBehavior = { behavior with Source = behavior.Source + "\n// persisted edit" }
        let changedState =
            { stored.State with
                BehaviorModules = Map.add behavior.Id changedBehavior stored.State.BehaviorModules }

        match store.TryCommit(stored.WorldRevision, stored.CharacterRevisions, changedState) with
        | Ok committed ->
            let persisted = store.GetSnapshot().World.BehaviorModules[behavior.Id]
            Assert.Equal(1L, committed.WorldRevision)
            Assert.Equal(1L, persisted.SourceRevision)
            Assert.Equal(1L, persisted.ActivationRevision)
            Assert.EndsWith("// persisted edit", persisted.Source)
        | Error _ -> Assert.True(false, "Expected the behavior commit to succeed.")

    [<Fact>]
    let ``Compiled artifact changes are not durable world changes`` () =
        let store = createStore ()
        let stored = store.Read()
        let behavior = stored.State.BehaviorModules["forest-behaviors"]
        let changedBehavior = { behavior with CompiledSource = behavior.CompiledSource + "\n// cache only" }
        let changedState =
            { stored.State with
                BehaviorModules = Map.add behavior.Id changedBehavior stored.State.BehaviorModules }

        match store.TryCommit(stored.WorldRevision, stored.CharacterRevisions, changedState) with
        | Ok committed ->
            Assert.Equal(0L, committed.WorldRevision)
            Assert.Equal(BehaviorSources.forest, store.GetSnapshot().World.BehaviorModules[behavior.Id].Source)
        | Error _ -> Assert.True(false, "Expected the cache-only commit to succeed.")

    [<Fact>]
    let ``Stale revisions reject the complete commit`` () =
        let store = createStore ()
        let first = store.Read()
        let gathered = Kernel.submitCommand En "gather wood" first.State
        store.TryCommit(first.WorldRevision, first.CharacterRevisions, gathered.State) |> Result.defaultWith (fun _ -> failwith "commit") |> ignore

        let moved = Kernel.submitCommand En "go north" first.State

        match store.TryCommit(first.WorldRevision, first.CharacterRevisions, moved.State) with
        | Error conflict ->
            Assert.Equal(0L, conflict.ExpectedCharacterRevisions[GameSnapshots.PrototypeCharacterId])
            Assert.Equal(1L, conflict.ActualCharacterRevisions[GameSnapshots.PrototypeCharacterId])
            Assert.Equal("forest", store.Read().State.Player.LocationId)
        | Ok _ -> Assert.True(false, "Expected the stale commit to be rejected.")

    [<Fact>]
    let ``Snapshots track character revisions independently`` () =
        let prototype = ObjectDatabase.initialState.Player
        let second = { prototype with Id = "second-character"; LocationId = "village" }
        let secondObject = toPlayerObject second
        let initial =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add second.Id secondObject }
        let store = InMemoryGameStore(initial, fun () -> timestamp)
        let stored = store.Read()
        let changedState =
            CarriedItems.addInventory stored.State second.Id "wood" 3

        match store.TryCommit(stored.WorldRevision, stored.CharacterRevisions, changedState) with
        | Ok committed ->
            Assert.Equal(0L, committed.CharacterRevisions[GameSnapshots.PrototypeCharacterId])
            Assert.Equal(1L, committed.CharacterRevisions[second.Id])
        | Error _ -> Assert.True(false, "Expected the second character commit to succeed.")

module SnapshotPersistenceTests =
    let private timestamp = DateTimeOffset(2026, 6, 27, 12, 0, 0, TimeSpan.Zero)

    let private createSnapshot () =
        (InMemoryGameStore(ObjectDatabase.initialState, fun () -> timestamp)).GetSnapshot()

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

    [<Fact>]
    let ``Snapshot codec round-trips authoritative state`` () =
        let snapshot = createSnapshot ()

        match SnapshotCodec.tryDeserialize(SnapshotCodec.serialize snapshot) with
        | Ok decoded ->
            Assert.Equal(snapshot.FormatVersion, decoded.FormatVersion)
            Assert.Equal(snapshot.World.Revision, decoded.World.Revision)
            Assert.Equal<Set<ItemId>>(snapshot.World.ItemIds, decoded.World.ItemIds)
            Assert.Equal(snapshot.World.Objects.Count, decoded.World.Objects.Count)
            Assert.Equal(snapshot.PlayerRevisions.Count, decoded.PlayerRevisions.Count)
            Assert.Equal(snapshot.World.Objects.Count, decoded.World.Objects.Count)
            Assert.Equal<GameValue>(
                snapshot.World.Objects["forest"].Properties["trailToken"],
                decoded.World.Objects["forest"].Properties["trailToken"])
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Migration rejects newer snapshot format versions`` () =
        let snapshot = { createSnapshot () with FormatVersion = GameSnapshots.CurrentFormatVersion + 1 }

        match SnapshotMigrations.migrate snapshot with
        | Error error -> Assert.Contains("newer than this server supports", error)
        | Ok _ -> Assert.True(false, "Expected an unsupported format version error.")

    [<Fact>]
    let ``Migration upgrades format version 1 snapshots to accounts and ownership`` () =
        let character =
            { Id = GameSnapshots.PrototypeCharacterId
              AccountId = ""
              Revision = 0L
              LocationId = "forest"
              Inventory = Map.empty }

        let legacy =
            { FormatVersion = 1
              World = createSnapshot().World
              Accounts = Map.empty
              Characters = Map.ofList [ character.Id, character ]
              PlayerRevisions = Map.empty }

        match SnapshotMigrations.migrate legacy with
        | Ok migrated ->
            Assert.Equal(4, migrated.FormatVersion)
            Assert.True(migrated.Accounts.ContainsKey GameSnapshots.PrototypeAccountId)
            Assert.True(migrated.World.Objects.ContainsKey GameSnapshots.PrototypeCharacterId)
            Assert.True(migrated.World.Objects.ContainsKey GameSnapshots.PrototypeScoutCharacterId)
            Assert.Equal(GameSnapshots.PrototypeAccountId, PlayerObjects.accountId migrated.World.Objects[character.Id])
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Migration promotes legacy player inventory properties to carried stacks`` () =
        let player =
            PlayerObjects.createWithLegacyInventory
                GameSnapshots.PrototypeCharacterId
                "prototype player"
                "object.prototype-player.name"
                GameSnapshots.PrototypeAccountId
                "forest"
                (Map.ofList [ "wood", 2 ])

        let legacy =
            { FormatVersion = 3
              World =
                { Revision = 0L
                  ItemIds = Set.ofList [ "wood" ]
                  BehaviorModules = Map.empty
                  Objects = Map.ofList [ player.Id, player ] }
              Accounts = Map.empty
              Characters = Map.empty
              PlayerRevisions = Map.empty }

        match SnapshotMigrations.migrate legacy with
        | Ok migrated ->
            Assert.Equal(4, migrated.FormatVersion)
            Assert.False(migrated.World.Objects[player.Id].Properties.ContainsKey PlayerObjects.InventoryProperty)

            let stackId = CarriedItems.migrationStackId player.Id "wood"
            Assert.True(migrated.World.Objects.ContainsKey stackId)
            let migratedState = { ObjectDatabase.initialState with Objects = migrated.World.Objects }
            Assert.Equal(2, (CarriedItems.inventoryMap migratedState player.Id).["wood"])
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Gather wood creates a carried stack object`` () =
        let result = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        let stacks = CarriedItems.stacksFor result.State GameSnapshots.PrototypeCharacterId

        Assert.Single(stacks)
        Assert.Equal(2, result.State.Player.Inventory["wood"])

    [<Fact>]
    let ``Hydration rebuilds runtime behavior modules from stored source`` () =
        let snapshot = createSnapshot ()

        match SnapshotHydration.hydrate mockCompile mockInspect snapshot with
        | Ok(state, hydratedSnapshot) ->
            Assert.Equal(snapshot, hydratedSnapshot)
            Assert.Equal(ObjectDatabase.initialState.BehaviorModules.Count, state.BehaviorModules.Count)
            Assert.Equal(2, (Kernel.submitCommand En "gather wood" state).State.Player.Inventory["wood"])
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Migration repairs missing seed behavior modules referenced by stored objects`` () =
        let snapshot = createSnapshot ()

        let legacySnapshot =
            { snapshot with
                World =
                    { snapshot.World with
                        BehaviorModules = snapshot.World.BehaviorModules |> Map.remove "player-behaviors" } }

        match SnapshotMigrations.migrate legacySnapshot with
        | Ok repaired ->
            Assert.True(repaired.World.BehaviorModules.ContainsKey "player-behaviors")
            Assert.True(
                repaired.World.BehaviorModules["player-behaviors"].Source.Contains("PlayerBehavior"),
                "Expected repaired snapshot to restore player-behaviors source from seed.")
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Hydration restores missing seed behavior modules required by stored objects`` () =
        let snapshot = createSnapshot ()

        let legacySnapshot =
            { snapshot with
                World =
                    { snapshot.World with
                        BehaviorModules = snapshot.World.BehaviorModules |> Map.remove "player-behaviors" } }

        match SnapshotHydration.hydrate mockCompile mockInspect legacySnapshot with
        | Ok(state, _) ->
            Assert.True(state.BehaviorModules.ContainsKey "player-behaviors")

            let looked =
                Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "look" state

            Assert.NotEqual<string>("command.unknown", looked.Messages |> List.head |> fun message -> message.Key)
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``File game store persists commits across reload`` () =
        let path = Path.Combine(Path.GetTempPath(), "brokenrealm-test-" + Guid.NewGuid().ToString("N") + ".json")

        try
            let store = FileGameStore(path, ObjectDatabase.initialState, fun () -> timestamp)
            let stored = store.Read()
            let gathered = Kernel.submitCommand En "gather wood" stored.State

            match store.TryCommit(stored.WorldRevision, stored.CharacterRevisions, gathered.State) with
            | Ok _ -> ()
            | Error _ -> Assert.True(false, "Expected the persisted commit to succeed.")

            Assert.True(File.Exists path)

            let reloaded =
                match
                    SnapshotCodec.tryReadFile path
                    |> Result.bind SnapshotMigrations.migrate
                    |> Result.bind (SnapshotHydration.hydrate mockCompile mockInspect)
                with
                | Ok(state, _) -> state
                | Error error -> failwith error

            let player = reloaded.Objects[GameSnapshots.PrototypeCharacterId]
            Assert.Equal(2, (PlayerObjects.inventory reloaded player.Id)["wood"])
        finally
            if File.Exists path then
                File.Delete path

    [<Fact>]
    let ``Snapshot backup and restore round-trip through the file store`` () =
        let contentRoot =
            Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "BrokenRealm.Server"))

        let directory = Path.Combine(Path.GetTempPath(), "brokenrealm-backup-" + Guid.NewGuid().ToString("N"))
        let path = Path.Combine(directory, "game-snapshot.json")

        try
            Directory.CreateDirectory(directory) |> ignore
            let store = FileGameStore(path, ObjectDatabase.initialState, fun () -> timestamp)
            let stored = store.Read()
            let gathered = Kernel.submitCommand En "gather wood" stored.State

            match store.TryCommit(stored.WorldRevision, stored.CharacterRevisions, gathered.State) with
            | Ok _ -> ()
            | Error _ -> Assert.True(false, "Expected the persisted commit to succeed.")

            match store.CreateBackup(fun () -> timestamp) with
            | Ok backupFileName ->
                let backupPath = Path.Combine(SnapshotBackup.backupDirectoryFor path, backupFileName)
                Assert.True(File.Exists backupPath)

                let resetStore = FileGameStore(path, ObjectDatabase.initialState, fun () -> timestamp)
                let resetPlayer =
                    (let state = resetStore.Read().State
                     PlayerObjects.inventory state GameSnapshots.PrototypeCharacterId)

                Assert.Empty(resetPlayer)

                match resetStore.TryRestore(contentRoot, backupFileName) with
                | Ok snapshot ->
                    let restoredState = resetStore.Read().State

                    let restoredPlayer =
                        PlayerObjects.inventory restoredState GameSnapshots.PrototypeCharacterId

                    Assert.Equal(2, restoredPlayer["wood"])
                    Assert.True(snapshot.World.Revision >= 0L)
                | Error error -> Assert.True(false, error)
            | Error error -> Assert.True(false, error)
        finally
            if Directory.Exists directory then
                Directory.Delete(directory, recursive = true)

    [<Fact>]
    let ``Snapshot restore rejects path traversal backup names`` () =
        let contentRoot =
            Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "BrokenRealm.Server"))

        let directory = Path.Combine(Path.GetTempPath(), "brokenrealm-backup-" + Guid.NewGuid().ToString("N"))
        let path = Path.Combine(directory, "game-snapshot.json")

        try
            Directory.CreateDirectory(directory) |> ignore
            let store = FileGameStore(path, ObjectDatabase.initialState, fun () -> timestamp)

            match store.TryRestore(contentRoot, "../outside.json") with
            | Error error -> Assert.Contains("parent-directory", error)
            | Ok _ -> Assert.True(false, "Expected path traversal restore to be rejected.")
        finally
            if Directory.Exists directory then
                Directory.Delete(directory, recursive = true)

module KernelTests =
    let private diagnostic message = { message = message; file = ""; line = 0; column = 0 }

    let private forestSource = BehaviorSources.join [ BehaviorSources.core; BehaviorSources.location; BehaviorSources.forest ]
    let private forestCompiled =
        BehaviorSources.join [ BehaviorSources.coreCompiled; BehaviorSources.locationCompiled; BehaviorSources.forestCompiled ]

    let private updateForestBehavior compiledSource source =
        let classes = ObjectDatabase.initialState.BehaviorModules["forest-behaviors"].Classes

        Kernel.tryUpdateBehaviorModule
            (fun _ -> Ok compiledSource)
            (fun _ _ -> Ok classes)
            "forest-behaviors"
            source
            ObjectDatabase.initialState

    [<Fact>]
    let ``Commands use and mutate only the selected character`` () =
        let prototype = ObjectDatabase.initialState.Player
        let second = { prototype with Id = "second-character"; LocationId = "village" }
        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add second.Id (toPlayerObject second) }

        let result = Kernel.submitCommandForCharacter second.Id De "gehe nach süden" state

        Assert.Equal("forest", PlayerObjects.locationId result.State.Objects[second.Id])
        Assert.Equal("forest", result.State.Player.LocationId)
        Assert.Empty(result.State.Player.Inventory)

    [<Fact>]
    let ``Unknown character commands fail without changing state`` () =
        let state = ObjectDatabase.initialState
        let result = Kernel.submitCommandForCharacter "missing-character" En "look" state

        Assert.Equal(state, result.State)
        let message = result.Messages |> List.exactlyOne
        Assert.Equal("script.error", message.Key)
        Assert.Equal("Unknown character id: missing-character", message.Args["error"])

    [<Fact>]
    let ``Initial object IDs and references satisfy the durable ID contract`` () =
        let state = ObjectDatabase.initialState

        state.Objects
        |> Map.iter (fun objectId object ->
            Assert.True(ObjectIds.isValid objectId, $"Invalid object ID: {objectId}")
            Assert.Equal(objectId, object.Id)

            object.References
            |> Map.iter (fun _ destinationId ->
                Assert.True(state.Objects.ContainsKey destinationId, $"Unknown object reference: {destinationId}")))

    [<Fact>]
    let ``Generated object IDs are valid and unique`` () =
        let first = ObjectIds.create ()
        let second = ObjectIds.create ()

        Assert.True(ObjectIds.isValid first)
        Assert.True(ObjectIds.isValid second)
        Assert.StartsWith("obj_", first)
        Assert.NotEqual<string>(first, second)

    [<Theory>]
    [<InlineData("")>]
    [<InlineData("Forest")>]
    [<InlineData("1forest")>]
    [<InlineData("forest room")>]
    [<InlineData("forest/room")>]
    let ``Invalid object IDs are rejected`` value =
        Assert.False(ObjectIds.isValid value)

    [<Fact>]
    let ``Admin catalog lists behavior modules and classes`` () =
        let modules = Kernel.listAdminBehaviorModules ObjectDatabase.initialState

        Assert.Equal<string list>(
            [ "anonymous-behaviors"; "core-behaviors"; "forest-behaviors"; "location-behaviors"; "player-behaviors"; "thing-behaviors"; "village-behaviors" ],
            modules |> List.map _.moduleId)

        let forest = modules |> List.find (fun behaviorModule -> behaviorModule.moduleId = "forest-behaviors")
        Assert.Equal<string list>([ "location-behaviors" ], forest.dependencies)
        Assert.Equal<string list>([ "ForestBehavior" ], forest.classes)

    [<Fact>]
    let ``Base behavior impact includes transitive modules and objects`` () =
        let modules, objects = Kernel.behaviorImpact "core-behaviors" ObjectDatabase.initialState

        Assert.Equal<string list>(
            [ "anonymous-behaviors"; "core-behaviors"; "forest-behaviors"; "location-behaviors"; "player-behaviors"; "thing-behaviors"; "village-behaviors" ],
            modules)
        Assert.Equal<string list>([ "fallen-log"; "forest"; "prototype-player"; "prototype-scout"; "village" ], objects)

    [<Fact>]
    let ``Updating a base module recompiles dependents in dependency order`` () =
        let state = ObjectDatabase.initialState
        let editedCore = BehaviorSources.core + "\n// edited core"

        let inspect registryName _ =
            state.BehaviorModules
            |> Map.toList
            |> List.map snd
            |> List.find (fun behaviorModule -> behaviorModule.RegistryName = registryName)
            |> _.Classes
            |> Ok

        let result =
            Kernel.tryUpdateBehaviorModule
                Ok
                inspect
                "core-behaviors"
                editedCore
                state

        match result with
        | Ok(Some update) ->
            Assert.Equal<string list>(
                [ "core-behaviors"; "anonymous-behaviors"; "location-behaviors"; "forest-behaviors"; "player-behaviors"; "thing-behaviors"; "village-behaviors" ],
                update.AffectedModules)
            Assert.Equal<string list>([ "fallen-log"; "forest"; "prototype-player"; "prototype-scout"; "village" ], update.AffectedObjects)
            Assert.Equal(editedCore, update.State.BehaviorModules["core-behaviors"].Source)
            Assert.Contains(editedCore, update.State.BehaviorModules["forest-behaviors"].CompiledSource)
            Assert.Contains(BehaviorSources.location, update.State.BehaviorModules["forest-behaviors"].CompiledSource)
            Assert.Contains(BehaviorSources.forest, update.State.BehaviorModules["forest-behaviors"].CompiledSource)
        | Ok None -> Assert.True(false, "Expected the base module to update.")
        | Error diagnostics -> Assert.True(false, diagnostics |> List.map _.message |> String.concat "\n")

    [<Fact>]
    let ``Behavior validation reports impact without returning activatable state`` () =
        let state = ObjectDatabase.initialState
        let source = BehaviorSources.forest + "\n// unsaved validation"
        let classes = state.BehaviorModules["forest-behaviors"].Classes

        match
            Kernel.tryValidateBehaviorModule
                (fun _ -> Ok forestCompiled)
                (fun _ _ -> Ok classes)
                "forest-behaviors"
                source
                state
        with
        | Ok(Some validated) ->
            Assert.Equal<string list>([ "forest-behaviors" ], validated.AffectedModules)
            Assert.Equal<string list>([ "forest" ], validated.AffectedObjects)
            Assert.Equal(BehaviorSources.forest, state.BehaviorModules["forest-behaviors"].Source)
        | Ok None -> Assert.True(false, "Expected the behavior module to validate.")
        | Error diagnostics -> Assert.True(false, diagnostics |> List.map _.message |> String.concat "\n")

    [<Fact>]
    let ``Behavior module dependency cycles are rejected`` () =
        let state = ObjectDatabase.initialState
        let core = state.BehaviorModules["core-behaviors"]
        let cyclicCore = { core with Dependencies = [ "forest-behaviors" ] }
        let cyclicState =
            { state with
                BehaviorModules = state.BehaviorModules |> Map.add core.Id cyclicCore }

        let result =
            Kernel.tryUpdateBehaviorModule
                Ok
                (fun _ _ -> Ok Map.empty)
                core.Id
                core.Source
                cyclicState

        match result with
        | Error [ diagnostic ] -> Assert.Contains("dependency cycle", diagnostic.message)
        | _ -> Assert.True(false, "Expected the dependency cycle to be rejected.")

    [<Fact>]
    let ``Missing behavior module dependencies are rejected`` () =
        let state = ObjectDatabase.initialState
        let forest = state.BehaviorModules["forest-behaviors"]
        let brokenForest = { forest with Dependencies = [ "missing-behaviors" ] }
        let brokenState =
            { state with
                BehaviorModules = state.BehaviorModules |> Map.add forest.Id brokenForest }

        let result =
            Kernel.tryUpdateBehaviorModule
                Ok
                (fun _ _ -> Ok Map.empty)
                forest.Id
                forest.Source
                brokenState

        match result with
        | Error [ diagnostic ] -> Assert.Equal("Missing behavior module dependency: missing-behaviors.", diagnostic.message)
        | _ -> Assert.True(false, "Expected the missing dependency to be rejected.")

    [<Fact>]
    let ``Failed descendant compilation leaves the entire graph unchanged`` () =
        let state = ObjectDatabase.initialState
        let editedCore = BehaviorSources.core + "\n// edited"
        let failure = diagnostic "forest compile failed"

        let compile (source: string) =
            if source.Contains(BehaviorSources.forest) then Error [ failure ] else Ok source

        let inspect registryName _ =
            state.BehaviorModules
            |> Map.toList
            |> List.map snd
            |> List.find (fun behaviorModule -> behaviorModule.RegistryName = registryName)
            |> _.Classes
            |> Ok

        let result =
            Kernel.tryUpdateBehaviorModule compile inspect "core-behaviors" editedCore state

        match result with
        | Error [ diagnostic ] -> Assert.Equal("forest compile failed", diagnostic.message)
        | _ -> Assert.True(false, "Expected descendant compilation to fail.")

        Assert.Equal(BehaviorSources.core, state.BehaviorModules["core-behaviors"].Source)

    [<Fact>]
    let ``Behavior command metadata is read from compiled TypeScript classes`` () =
        let classes =
            match Scripting.inspectBehaviorModule "forestBehaviorClasses" forestCompiled with
            | Ok classes -> classes
            | Error diagnostic -> failwith diagnostic.message

        let forestCommands = classes["ForestBehavior"].Commands |> List.map _.MethodName
        Assert.Equal<string list>([ "look"; "move"; "gather"; "renameTrail" ], forestCommands)

    [<Fact>]
    let ``Updating class command metadata changes localized dispatch`` () =
        let compiled = forestCompiled.Replace("gather {item}", "harvest {item}")

        let state =
            match
                Kernel.tryUpdateBehaviorModule
                    (fun _ -> Ok compiled)
                    Scripting.inspectBehaviorModule
                    "forest-behaviors"
                    BehaviorSources.forest
                    ObjectDatabase.initialState
            with
            | Ok(Some update) -> update.State
            | Ok None -> failwith "Expected behavior module to update."
            | Error diagnostics -> failwith (diagnostics |> List.map _.message |> String.concat "\n")

        let matched = CommandMatching.tryMatch En "harvest wood" state
        Assert.Equal("gather", (matched |> Option.get).MethodName)

    [<Fact>]
    let ``Behavior updates cannot remove a class used by an object`` () =
        let compiled =
            forestCompiled.Replace(
                "const forestBehaviorClasses = { ForestBehavior };",
                "const forestBehaviorClasses = {};")

        let result =
            Kernel.tryUpdateBehaviorModule
                (fun _ -> Ok compiled)
                Scripting.inspectBehaviorModule
                "forest-behaviors"
                BehaviorSources.forest
                ObjectDatabase.initialState

        match result with
        | Error [ diagnostic ] ->
            Assert.Equal("Behavior module is missing class ForestBehavior, used by object forest.", diagnostic.message)
        | _ -> Assert.True(false, "Expected the behavior update to be rejected.")

    [<Fact>]
    let ``Behavior command metadata must reference an implemented method`` () =
        let compiled = forestCompiled.Replace("methodName: \"gather\"", "methodName: \"missing\"")

        match Scripting.inspectBehaviorModule "forestBehaviorClasses" compiled with
        | Error diagnostic ->
            Assert.Equal("Behavior class ForestBehavior registers a command without a matching method.", diagnostic.message)
        | Ok _ -> Assert.True(false, "Expected invalid command metadata to be rejected.")

    [<Fact>]
    let ``German movement command resolves a neutral direction`` () =
        let matched = CommandMatching.tryMatch De "gehe nach norden" ObjectDatabase.initialState

        match matched with
        | Some value ->
            Assert.Equal("move", value.MethodName)
            Assert.Equal("north", value.Args["direction"])
        | None -> Assert.True(false, "Expected command to match the movement verb.")

    [<Fact>]
    let ``Forest contents are derived from permanent object locations`` () =
        let contents = Kernel.contentsOf ObjectDatabase.initialState "forest"

        Assert.Equal<string list>([ "fallen-log"; "prototype-player" ], contents |> List.map _.Id)

    [<Theory>]
    [<InlineData("examine log", "en")>]
    [<InlineData("untersuche baumstamm", "de")>]
    let ``Localized examine commands dispatch to visible object behavior`` command cultureName =
        let culture = if cultureName = "de" then De else En
        let state = ObjectDatabase.initialState
        let matched = CommandMatching.tryMatch culture command state |> Option.get

        Assert.Equal("fallen-log", matched.ObjectId)
        Assert.Equal("ThingBehavior", matched.BehaviorClassName)
        Assert.Equal("examine", matched.MethodName)

        let result = Kernel.submitCommand culture command state
        let line = result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage result.State culture
        let expected =
            if culture = De then
                "Ein moosbedeckter Baumstamm liegt auf dem Waldboden."
            else
                "A moss-covered log lies across the forest floor."

        Assert.Equal(expected, line)

    [<Fact>]
    let ``Look lists visible contents with localized object names`` () =
        let english = Kernel.submitCommand En "look" ObjectDatabase.initialState
        let englishLines = english.Messages |> List.map (ResponseFormatting.localizeMessage english.State En)
        Assert.Contains(englishLines, fun line -> line.Contains("fallen log"))
        Assert.DoesNotContain(englishLines, fun line -> line.Contains("prototype player"))

        let german = Kernel.submitCommand De "schau" ObjectDatabase.initialState
        let germanLines = german.Messages |> List.map (ResponseFormatting.localizeMessage german.State De)
        Assert.Contains(germanLines, fun line -> line.Contains("umgestürzten Baumstamm"))
        Assert.DoesNotContain(germanLines, fun line -> line.Contains("Prototyp-Spieler"))

    [<Fact>]
    let ``Objects outside the current location are not visible or matchable`` () =
        let villageState = (Kernel.submitCommand En "go north" ObjectDatabase.initialState).State

        let villageContents = Kernel.contentsOf villageState "village" |> List.map _.Id
        Assert.Contains("prototype-player", villageContents)
        Assert.DoesNotContain("fallen-log", villageContents)
        Assert.True(CommandMatching.tryMatch En "examine log" villageState |> Option.isNone)

        let result = Kernel.submitCommand En "examine log" villageState
        let line = result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage result.State En
        Assert.Equal("I do not understand that command.", line)

    [<Fact>]
    let ``Player movement does not move contained world objects`` () =
        let result = Kernel.submitCommand En "go north" ObjectDatabase.initialState

        Assert.Equal("village", result.State.Player.LocationId)
        Assert.Equal(Some "forest", result.State.Objects["fallen-log"].LocationId)

    [<Fact>]
    let ``Containment rejects missing locations`` () =
        let state = ObjectDatabase.initialState
        let log = state.Objects["fallen-log"]
        let broken = { log with LocationId = Some "missing" }
        let brokenState = { state with Objects = state.Objects |> Map.add log.Id broken }

        Assert.Equal(Error "Object fallen-log has unknown location id: missing", Kernel.validateContainment brokenState)

    [<Fact>]
    let ``Containment rejects self containment`` () =
        let state = ObjectDatabase.initialState
        let log = state.Objects["fallen-log"]
        let broken = { log with LocationId = Some log.Id }
        let brokenState = { state with Objects = state.Objects |> Map.add log.Id broken }

        Assert.Equal(Error "Object cannot contain itself: fallen-log", Kernel.validateContainment brokenState)

    [<Fact>]
    let ``Containment rejects cycles`` () =
        let state = ObjectDatabase.initialState
        let forest = { state.Objects["forest"] with LocationId = Some "fallen-log" }
        let brokenState = { state with Objects = state.Objects |> Map.add forest.Id forest }

        match Kernel.validateContainment brokenState with
        | Error error -> Assert.Contains("Containment cycle", error)
        | Ok() -> Assert.True(false, "Expected containment cycle to be rejected.")

    [<Fact>]
    let ``Movement follows object references between locations`` () =
        let villageResult = Kernel.submitCommand En "go north" ObjectDatabase.initialState

        Assert.Equal("village", villageResult.State.Player.LocationId)
        Assert.Equal(
            "You travel north.",
            RoomBroadcast.actorMessages villageResult.Messages
            |> List.exactlyOne
            |> ResponseFormatting.localizeMessage villageResult.State En)

        let forestResult = Kernel.submitCommand De "gehe nach süden" villageResult.State
        Assert.Equal("forest", forestResult.State.Player.LocationId)

        Assert.Equal(
            "Du gehst nach Süden.",
            RoomBroadcast.actorMessages forestResult.Messages
            |> List.exactlyOne
            |> ResponseFormatting.localizeMessage forestResult.State De)

    [<Fact>]
    let ``Movement without an exit leaves the player in place`` () =
        let result = Kernel.submitCommand En "go south" ObjectDatabase.initialState

        Assert.Equal("forest", result.State.Player.LocationId)
        Assert.Equal("You cannot go that way.", result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage result.State En)

    [<Fact>]
    let ``Kernel rejects movement to an unknown object`` () =
        let state = ObjectDatabase.initialState
        let forest = state.Objects["forest"]
        let brokenForest = { forest with References = Map.ofList [ "north", "missing" ] }
        let brokenState = { state with Objects = state.Objects |> Map.add forest.Id brokenForest }

        let result = Kernel.submitCommand En "go north" brokenState

        Assert.Equal("forest", result.State.Player.LocationId)
        let message = result.Messages |> List.exactlyOne
        Assert.Equal("script.error", message.Key)
        Assert.Equal("Unknown destination object id: missing", message.Args["error"])

    [<Fact>]
    let ``Kernel rejects unknown object references nested in properties`` () =
        let state = ObjectDatabase.initialState
        let forest = state.Objects["forest"]
        let brokenProperties =
            forest.Properties
            |> Map.add "config" (MapValue(Map.ofList [ "targets", ListValue [ ObjectReferenceValue "missing" ] ]))

        let brokenForest = { forest with Properties = brokenProperties }
        let brokenState = { state with Objects = state.Objects |> Map.add forest.Id brokenForest }
        let result = Kernel.submitCommand En "look" brokenState

        Assert.Equal("forest", result.State.Player.LocationId)
        let message = result.Messages |> List.exactlyOne
        Assert.Equal("script.error", message.Key)
        Assert.Equal("Property config.targets[0] references unknown object id: missing", message.Args["error"])

    [<Fact>]
    let ``German gather command matches forest gather verb with neutral item id`` () =
        let state = ObjectDatabase.initialState

        let matched = CommandMatching.tryMatch De "holz sammeln" state

        match matched with
        | Some value ->
            Assert.Equal("forest", value.ObjectId)
            Assert.Equal("gather", value.MethodName)
            Assert.Equal("wood", value.Args["item"])
        | None -> Assert.True(false, "Expected command to match a verb.")

    [<Fact>]
    let ``Gather verb returns neutral effects applied by kernel`` () =
        let state = ObjectDatabase.initialState

        let result = Kernel.submitCommand En "gather wood" state

        Assert.Equal(2, result.State.Player.Inventory["wood"])
        Assert.Collection(
            result.Messages,
            fun message ->
                Assert.Equal("gather.wood.success", message.Key)
                Assert.Equal("2", message.Args["amount"])
                Assert.Equal("wood", message.Args["item"]))

    [<Fact>]
    let ``Localized inventory output uses localized item names`` () =
        let stateAfterGather = (Kernel.submitCommand De "sammle holz" ObjectDatabase.initialState).State

        let result = Kernel.submitCommand De "inventar" stateAfterGather
        let line = result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage result.State De

        Assert.Equal("Inventar: 2 Holz.", line)

    [<Fact>]
    let ``Drop removes one item and places a stack in the room`` () =
        let gathered = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        Assert.Equal(2, gathered.State.Player.Inventory["wood"])

        let dropped = Kernel.submitCommand En "drop wood" gathered.State

        Assert.Equal(1, dropped.State.Player.Inventory["wood"])

        let roomStacks = CarriedItems.stacksIn dropped.State "forest"

        Assert.Equal(1, roomStacks.Length)

        match roomStacks[0].Properties |> Map.tryFind CarriedItems.QuantityProperty with
        | Some(IntegerValue quantity) -> Assert.Equal(1L, quantity)
        | _ -> Assert.True(false, "Expected a floor stack with quantity 1.")

        Assert.Contains(dropped.Messages, fun message -> message.Key = "drop.success")

    [<Fact>]
    let ``German drop command removes one carried item`` () =
        let gathered = Kernel.submitCommand De "sammle holz" ObjectDatabase.initialState
        let dropped = Kernel.submitCommand De "lege holz ab" gathered.State

        Assert.Equal(1, dropped.State.Player.Inventory["wood"])
        Assert.Contains(dropped.Messages, fun message -> message.Key = "drop.success")

    [<Fact>]
    let ``Drop with empty inventory reports drop none`` () =
        let result = Kernel.submitCommand En "drop wood" ObjectDatabase.initialState

        Assert.Empty(result.State.Player.Inventory)

        let message = result.Messages |> List.exactlyOne
        Assert.Equal("drop.none", message.Key)
        Assert.Equal("wood", message.Args["item"])

    [<Fact>]
    let ``Give transfers one item to another player in the same room`` () =
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInForest = PlayerObjects.withLocation scout "forest"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInForest }

        let gathered = Kernel.submitCommand En "gather wood" state
        let given = Kernel.submitCommand En "give wood to scout" gathered.State

        Assert.Equal(1, given.State.Player.Inventory["wood"])
        Assert.Equal(1, (PlayerObjects.inventory given.State GameSnapshots.PrototypeScoutCharacterId)["wood"])
        Assert.Contains(given.Messages, fun message -> message.Key = "give.success")

    [<Fact>]
    let ``Give fails when recipient is not in the same room`` () =
        let gathered = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        let given = Kernel.submitCommand En "give wood to scout" gathered.State

        Assert.Equal(2, given.State.Player.Inventory["wood"])
        Assert.Equal(0, Map.count (PlayerObjects.inventory given.State GameSnapshots.PrototypeScoutCharacterId))

        let message = given.Messages |> List.exactlyOne
        Assert.Equal("give.not_here", message.Key)

    [<Fact>]
    let ``Give with empty inventory reports give none`` () =
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInForest = PlayerObjects.withLocation scout "forest"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInForest }

        let result = Kernel.submitCommand En "give wood to scout" state

        let message = result.Messages |> List.exactlyOne
        Assert.Equal("give.none", message.Key)
        Assert.Equal("wood", message.Args["item"])

    [<Fact>]
    let ``Give command matches player aliases in the same room`` () =
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInForest = PlayerObjects.withLocation scout "forest"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInForest }

        let matched = CommandMatching.tryMatch En "give wood to scout" state

        match matched with
        | Some value ->
            Assert.Equal(GameSnapshots.PrototypeCharacterId, value.ObjectId)
            Assert.Equal("give", value.MethodName)
            Assert.Equal("wood", value.Args["item"])
            Assert.Equal(GameSnapshots.PrototypeScoutCharacterId, value.Args["player"])
        | None -> Assert.True(false, "Expected give command to match.")

    [<Fact>]
    let ``Take picks up one item from a floor stack in the room`` () =
        let gathered = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        let dropped = Kernel.submitCommand En "drop wood" gathered.State
        let taken = Kernel.submitCommand En "take wood" dropped.State

        Assert.Equal(2, taken.State.Player.Inventory["wood"])
        Assert.Empty(CarriedItems.stacksIn taken.State "forest")
        Assert.Contains(taken.Messages, fun message -> message.Key = "take.success")

    [<Fact>]
    let ``German take command picks up a floor stack`` () =
        let gathered = Kernel.submitCommand De "sammle holz" ObjectDatabase.initialState
        let dropped = Kernel.submitCommand De "lege holz ab" gathered.State
        let taken = Kernel.submitCommand De "nimm holz" dropped.State

        Assert.Equal(2, taken.State.Player.Inventory["wood"])
        Assert.Contains(taken.Messages, fun message -> message.Key = "take.success")

    [<Fact>]
    let ``Pick up command alias takes one item from the floor`` () =
        let gathered = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        let dropped = Kernel.submitCommand En "drop wood" gathered.State
        let taken = Kernel.submitCommand En "pick up wood" dropped.State

        Assert.Equal(2, taken.State.Player.Inventory["wood"])

    [<Fact>]
    let ``Take with no floor stack reports take none`` () =
        let result = Kernel.submitCommand En "take wood" ObjectDatabase.initialState

        let message = result.Messages |> List.exactlyOne
        Assert.Equal("take.none", message.Key)
        Assert.Equal("wood", message.Args["item"])

    [<Fact>]
    let ``Say command returns localized speech with preserved text`` () =
        let result = Kernel.submitCommand En "say Hello there" ObjectDatabase.initialState

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.exactlyOne
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You say, \"Hello there\".", line)

    [<Fact>]
    let ``German say command matches sag pattern`` () =
        let result = Kernel.submitCommand De "sag Guten Tag" ObjectDatabase.initialState

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.exactlyOne
            |> ResponseFormatting.localizeMessage result.State De

        Assert.Equal("Du sagst: \"Guten Tag\".", line)

    [<Fact>]
    let ``Say with no text reports say empty`` () =
        let result = Kernel.submitCommand En "say" ObjectDatabase.initialState

        let message = result.Messages |> List.exactlyOne
        Assert.Equal("say.empty", message.Key)

    [<Fact>]
    let ``Emote command returns localized action text`` () =
        let result = Kernel.submitCommand En "emote wave happily" ObjectDatabase.initialState

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.exactlyOne
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You wave happily.", line)

    [<Fact>]
    let ``Colon emote alias matches actor emote`` () =
        let matched = CommandMatching.tryMatch En ": smile" ObjectDatabase.initialState

        match matched with
        | Some value ->
            Assert.Equal("emote", value.MethodName)
            Assert.Equal("smile", value.Args["text"])
        | None -> Assert.True(false, "Expected colon emote to match.")

        let result = Kernel.submitCommand En ": smile" ObjectDatabase.initialState

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.exactlyOne
            |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("You smile.", line)

    [<Fact>]
    let ``German star emote alias matches actor emote`` () =
        let result = Kernel.submitCommand De "* winkst" ObjectDatabase.initialState

        let line =
            RoomBroadcast.actorMessages result.Messages
            |> List.exactlyOne
            |> ResponseFormatting.localizeMessage result.State De

        Assert.Equal("Du winkst.", line)

    [<Fact>]
    let ``Drop amount moves multiple units to a floor stack`` () =
        let gathered = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        let dropped = Kernel.submitCommand En "drop 2 wood" gathered.State

        Assert.Equal(0, PlayerObjects.inventory dropped.State GameSnapshots.PrototypeCharacterId |> Map.tryFind "wood" |> Option.defaultValue 0)

        let roomStacks = CarriedItems.stacksIn dropped.State "forest"
        Assert.Equal(1, roomStacks.Length)

        match CarriedItems.stackQuantity roomStacks[0] with
        | Some quantity -> Assert.Equal(2, quantity)
        | None -> Assert.True(false, "Expected floor stack quantity.")

        let line =
            dropped.Messages
            |> List.find (fun message -> message.Key = "drop.success")
            |> ResponseFormatting.localizeMessage dropped.State En

        Assert.Equal("You drop 2 wood.", line)

    [<Fact>]
    let ``Take amount picks up multiple units from a floor stack`` () =
        let gathered = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        let dropped = Kernel.submitCommand En "drop 2 wood" gathered.State
        let taken = Kernel.submitCommand En "take 2 wood" dropped.State

        Assert.Equal(2, taken.State.Player.Inventory["wood"])
        Assert.Empty(CarriedItems.stacksIn taken.State "forest")

    [<Fact>]
    let ``Look shows floor stack quantities in room contents`` () =
        let gathered = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        let dropped = Kernel.submitCommand En "drop 2 wood" gathered.State
        let result = Kernel.submitCommand En "look" dropped.State
        let contents = result.Messages |> List.find (fun message -> message.Key = "location.contents")
        let line = ResponseFormatting.localizeMessage result.State En contents

        Assert.Contains("pile of wood (2)", line)

    [<Fact>]
    let ``Look lists other players in the same room`` () =
        let scout = PlayerObjects.get ObjectDatabase.initialState GameSnapshots.PrototypeScoutCharacterId
        let scoutInForest = PlayerObjects.withLocation scout "forest"

        let state =
            { ObjectDatabase.initialState with
                Objects = ObjectDatabase.initialState.Objects |> Map.add scout.Id scoutInForest }

        let result = Kernel.submitCommand En "look" state
        let contents = result.Messages |> List.find (fun message -> message.Key = "location.contents")

        Assert.Contains("prototype-scout", contents.Args["objects"])

        let line = ResponseFormatting.localizeMessage result.State En contents
        Assert.Contains("prototype scout", line)

    [<Fact>]
    let ``Drop take round trip restores inventory without duplicating stacks`` () =
        let gathered = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        Assert.Equal(2, gathered.State.Player.Inventory["wood"])

        let dropped = Kernel.submitCommand En "drop wood" gathered.State
        Assert.Equal(1, dropped.State.Player.Inventory["wood"])

        let taken = Kernel.submitCommand En "take wood" dropped.State
        Assert.Equal(2, taken.State.Player.Inventory["wood"])
        Assert.Empty(CarriedItems.stacksIn taken.State "forest")

    [<Fact>]
    let ``Updating forest gather source changes later gather behavior`` () =
        let updatedSource = BehaviorSources.forest.Replace("const amount = 2;", "const amount = 5;")
        let updatedCompiled = forestCompiled.Replace("const amount = 2;", "const amount = 5;")
        let state =
            match updateForestBehavior updatedCompiled updatedSource with
            | Ok(Some update) -> update.State
            | Ok None -> failwith "Expected behavior module to update."
            | Error diagnostics -> failwith (diagnostics |> List.map _.message |> String.concat "\n")

        let result = Kernel.submitCommand En "gather wood" state

        Assert.Equal(5, result.State.Player.Inventory["wood"])
        let line = result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage result.State En
        Assert.Equal("You gather 5 wood.", line)

    [<Fact>]
    let ``Invalid behavior source is rejected and previous source remains active`` () =
        let result =
            Kernel.tryUpdateBehaviorModule
                (fun _ -> Error [ diagnostic "bad script" ])
                (fun _ _ -> Ok Map.empty)
                "forest-behaviors"
                "broken"
                ObjectDatabase.initialState

        match result with
        | Error diagnostics -> Assert.Equal("bad script", (diagnostics |> List.exactlyOne).message)
        | Ok _ -> Assert.True(false, "Expected update to be rejected.")

        let gatherResult = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        Assert.Equal(2, gatherResult.State.Player.Inventory["wood"])

    [<Fact>]
    let ``Unknown command returns localized unknown message key`` () =
        let result = Kernel.submitCommand En "dance" ObjectDatabase.initialState

        let line = result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage result.State En

        Assert.Equal("I do not understand that command.", line)

    [<Fact>]
    let ``Kernel rejects unknown inventory item effects without mutating state`` () =
        let overrideSource =
            """ForestBehavior.prototype.gather = function(context) {
  return {
    effects: [
      { type: "addInventory", itemId: "stone", amount: 1 }
    ]
  };
};"""

        let compiledSource = forestCompiled + "\n" + overrideSource

        let state =
            match updateForestBehavior compiledSource BehaviorSources.forest with
            | Ok(Some update) -> update.State
            | Ok None -> failwith "Expected behavior module to update."
            | Error diagnostics -> failwith (diagnostics |> List.map _.message |> String.concat "\n")

        let result = Kernel.submitCommand En "gather wood" state

        Assert.Empty(result.State.Player.Inventory)
        let message = result.Messages |> List.exactlyOne
        Assert.Equal("script.error", message.Key)
        Assert.Equal("Unknown item id: stone", message.Args["error"])

    [<Fact>]
    let ``Rejected effect batches never partially mutate state`` () =
        let messages =
            List.replicate Scripting.defaultLimits.MaxEffects "{ type: 'message', key: 'test' }"
            |> String.concat ","

        let overrideSource =
            $"ForestBehavior.prototype.gather = function(context) {{ return {{ effects: [{{ type: 'addInventory', itemId: 'wood', amount: 1 }},{messages}] }}; }};"

        let compiledSource = forestCompiled + "\n" + overrideSource

        let state =
            match updateForestBehavior compiledSource BehaviorSources.forest with
            | Ok(Some update) -> update.State
            | Ok None -> failwith "Expected behavior module to update."
            | Error diagnostics -> failwith (diagnostics |> List.map _.message |> String.concat "\n")

        let result = Kernel.submitCommand En "gather wood" state

        Assert.Empty(result.State.Player.Inventory)
        let message = result.Messages |> List.exactlyOne
        Assert.Equal("script.error", message.Key)
        Assert.Equal("Scripts may return at most 32 effects.", message.Args["error"])

module ScriptingTests =
    let private forest = ObjectDatabase.initialState.Objects["forest"]
    let private testActor = ObjectDatabase.initialState.Objects[GameSnapshots.PrototypeCharacterId]

    [<Fact>]
    let ``Script must return effects array`` () =
        let source = "function execute(context) { return {}; }"

        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("Script must return an object with an effects array.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to fail.")

    [<Fact>]
    let ``Script effects must be an array`` () =
        let source = "function execute(context) { return { effects: {} }; }"

        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("Script effects must be an array.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to fail.")

    [<Fact>]
    let ``Unknown script effect types are rejected`` () =
        let source = "function execute(context) { return { effects: [{ type: 'teleport' }] }; }"

        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("Unknown script effect type: teleport", error)
        | Ok _ -> Assert.True(false, "Expected script execution to fail.")

    [<Fact>]
    let ``Malformed addInventory effects are rejected`` () =
        let source = "function execute(context) { return { effects: [{ type: 'addInventory', itemId: 'wood', amount: 0 }] }; }"

        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("addInventory effects require itemId and an amount from 1 to 100.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to fail.")

    [<Fact>]
    let ``Runtime script exceptions are returned as errors`` () =
        let source = "function execute(context) { throw new Error('boom'); }"

        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("Script execution failed.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to fail.")

    [<Fact>]
    let ``Infinite scripts are stopped by the execution timeout`` () =
        let limits = { Scripting.defaultLimits with Timeout = System.TimeSpan.FromMilliseconds(25.0) }
        let source = "function execute(context) { while (true) {} }"

        let result = Scripting.executeVerbWithLimits limits ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("Script execution timed out.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to time out.")

    [<Fact>]
    let ``Scripts are stopped when they exceed the memory limit`` () =
        let limits =
            { Scripting.defaultLimits with
                MemoryBytes = 250_000L
                Timeout = System.TimeSpan.FromSeconds(2.0) }

        let source =
            "function execute(context) { const values = []; while (true) { values.push('x'.repeat(1000)); } }"

        let result = Scripting.executeVerbWithLimits limits ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("Script exceeded its memory limit.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to exceed its memory limit.")

    [<Fact>]
    let ``Scripts cannot return too many effects`` () =
        let effects =
            List.replicate (Scripting.defaultLimits.MaxEffects + 1) "{ type: 'message', key: 'test' }"
            |> String.concat ","

        let source = $"function execute(context) {{ return {{ effects: [{effects}] }}; }}"
        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("Scripts may return at most 32 effects.", error)
        | Ok _ -> Assert.True(false, "Expected excessive effects to be rejected.")

    [<Fact>]
    let ``Scripts cannot return too many messages`` () =
        let effects =
            List.replicate (Scripting.defaultLimits.MaxMessages + 1) "{ type: 'message', key: 'test' }"
            |> String.concat ","

        let source = $"function execute(context) {{ return {{ effects: [{effects}] }}; }}"
        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("Scripts may return at most 16 message effects.", error)
        | Ok _ -> Assert.True(false, "Expected excessive messages to be rejected.")

    [<Fact>]
    let ``Message argument values have a bounded size`` () =
        let oversized = String.replicate (Scripting.defaultLimits.MaxMessageArgumentCharacters + 1) "x"
        let source = $"function execute(context) {{ return {{ effects: [{{ type: 'message', key: 'test', args: {{ value: '{oversized}' }} }}] }}; }}"
        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("Message argument values may contain at most 1024 characters.", error)
        | Ok _ -> Assert.True(false, "Expected oversized message arguments to be rejected.")

    [<Fact>]
    let ``Message argument counts are bounded`` () =
        let args =
            [ 1 .. Scripting.defaultLimits.MaxMessageArguments + 1 ]
            |> List.map (fun index -> $"value{index}: 'x'")
            |> String.concat ","

        let source = $"function execute(context) {{ return {{ effects: [{{ type: 'message', key: 'test', args: {{ {args} }} }}] }}; }}"
        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Error error -> Assert.Equal("Message effects may contain at most 16 arguments.", error)
        | Ok _ -> Assert.True(false, "Expected excessive message arguments to be rejected.")

    [<Fact>]
    let ``Script source length is bounded before execution`` () =
        let limits = { Scripting.defaultLimits with MaxSourceCharacters = 10 }
        let result = Scripting.executeVerbWithLimits limits ObjectDatabase.initialState forest Map.empty testActor "function execute() {}"

        match result with
        | Error error -> Assert.Equal("Script source may contain at most 10 characters.", error)
        | Ok _ -> Assert.True(false, "Expected oversized source to be rejected.")

    [<Fact>]
    let ``Script context includes object properties`` () =
        let source =
            """function execute(context) {
  return {
    effects: [
      {
        type: "message",
        key: "property.value",
        args: { value: context.this.properties.resourceItem }
      }
    ]
  };
}"""

        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Ok [ EmitMessage message ] ->
            Assert.Equal("property.value", message.Key)
            Assert.Equal("wood", message.Args["value"])
        | Ok _ -> Assert.True(false, "Expected one message effect.")
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Typed object properties become plain JavaScript values`` () =
        let typedForest =
            { forest with
                Properties =
                    Map.ofList
                        [ "nothing", NullValue
                          "name", StringValue "forest"
                          "count", IntegerValue 3L
                          "ratio", FloatValue 0.5
                          "enabled", BooleanValue true
                          "target", ObjectReferenceValue "village"
                          "items", ListValue [ StringValue "wood"; IntegerValue 2L ]
                          "nested", MapValue(Map.ofList [ "value", BooleanValue false ]) ] }

        let source =
            """function execute(context) {
  const p = context.this.properties;
  const valid = p.nothing === null
    && p.name === "forest"
    && p.count === 3
    && p.ratio === 0.5
    && p.enabled === true
    && p.target === "village"
    && Array.isArray(p.items)
    && p.items[0] === "wood"
    && p.items[1] === 2
    && p.nested.value === false;
  if (!valid) throw new Error("invalid typed properties");
  return { effects: [{ type: "message", key: "typed.ok" }] };
}"""

        match Scripting.executeVerb ObjectDatabase.initialState typedForest Map.empty testActor source with
        | Ok [ EmitMessage message ] -> Assert.Equal("typed.ok", message.Key)
        | Ok _ -> Assert.True(false, "Expected one message effect.")
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``moveObject can target an explicit player object`` () =
        let source =
            """function execute(context) {
  return {
    effects: [
      { type: "moveObject", objectId: context.actor.id, destinationId: context.this.references.north }
    ]
  };
}"""

        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Ok [ MoveObject(Some "prototype-player", destinationId) ] -> Assert.Equal("village", destinationId)
        | Ok effects -> Assert.True(false, $"Expected one movement effect, got {effects.Length}.")
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Script context includes object references`` () =
        let source =
            """function execute(context) {
  return {
    effects: [
      { type: "movePlayer", destinationId: context.this.references.north }
    ]
  };
}"""

        let result = Scripting.executeVerb ObjectDatabase.initialState forest Map.empty testActor source

        match result with
        | Ok [ MoveObject(None, destinationId) ] -> Assert.Equal("village", destinationId)
        | Ok _ -> Assert.True(false, "Expected one movement effect.")
        | Error error -> Assert.True(false, error)

module ScriptCompilerTests =
    [<Fact>]
    let ``Compiler reports a clear error when TypeScript is not installed`` () =
        let tempRoot =
            Path.Combine(Path.GetTempPath(), "brokenrealm-no-tsc-" + Guid.NewGuid().ToString("N"))

        let serverRoot = Path.Combine(tempRoot, "src", "BrokenRealm.Server")
        let clientRoot = Path.Combine(tempRoot, "src", "BrokenRealm.Client")
        let scriptingRoot = Path.Combine(serverRoot, "Scripting")

        try
            Directory.CreateDirectory(scriptingRoot) |> ignore
            Directory.CreateDirectory(clientRoot) |> ignore
            File.WriteAllText(Path.Combine(scriptingRoot, "game-api.d.ts"), "declare const x: number;") |> ignore

            match ScriptCompiler.compile tempRoot "const y: number = 1;" with
            | Error [ diagnostic ] ->
                Assert.Contains("npm install", diagnostic.message)
                Assert.Contains("BrokenRealm.Client", diagnostic.message)
                Assert.Contains("TypeScript compiler not found", diagnostic.message)
            | Error diagnostics -> Assert.True(false, diagnostics |> List.map _.message |> String.concat "\n")
            | Ok _ -> Assert.True(false, "Expected a missing TypeScript compiler diagnostic.")
        finally
            if Directory.Exists tempRoot then
                Directory.Delete(tempRoot, true)

    [<Fact>]
    let ``Compiler rejects oversized source before invoking TypeScript`` () =
        let source = String.replicate (Scripting.defaultLimits.MaxSourceCharacters + 1) "x"
        let result = ScriptCompiler.compile "." source

        match result with
        | Error [ diagnostic ] ->
            Assert.Equal("Behavior source may contain at most 64000 characters.", diagnostic.message)
            Assert.Equal("", diagnostic.file)
            Assert.Equal(0, diagnostic.line)
            Assert.Equal(0, diagnostic.column)
        | Error _ -> Assert.True(false, "Expected one source-length diagnostic.")
        | Ok _ -> Assert.True(false, "Expected oversized source to be rejected.")

module BehaviorClassRuntimeTests =
    let private forest = ObjectDatabase.initialState.Objects["forest"]
    let private forestSource = BehaviorSources.join [ BehaviorSources.core; BehaviorSources.location; BehaviorSources.forest ]
    let private forestCompiled =
        BehaviorSources.join [ BehaviorSources.coreCompiled; BehaviorSources.locationCompiled; BehaviorSources.forestCompiled ]

    let rec private findRepoRoot (directory: System.IO.DirectoryInfo) =
        if System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "BrokenRealm.slnx")) then
            directory.FullName
        elif isNull directory.Parent then
            failwith "Could not find the BrokenRealm repository root."
        else
            findRepoRoot directory.Parent

    let private compileBehavior source =
        let repoRoot = findRepoRoot (System.IO.DirectoryInfo(System.AppContext.BaseDirectory))

        match ScriptCompiler.compile repoRoot source with
        | Ok compiled -> Ok compiled
        | Error diagnostics -> Error(diagnostics |> List.map _.message |> String.concat "\n")

    [<Fact>]
    let ``Editor scripting declarations come from the compiler contract`` () =
        let repoRoot = findRepoRoot (System.IO.DirectoryInfo(System.AppContext.BaseDirectory))

        match ScriptCompiler.tryReadApiDeclarations repoRoot with
        | Some declarations ->
            Assert.Contains("declare interface VerbContext", declarations)
            Assert.Contains("invokeAnonymous", declarations)
            Assert.Contains("AnonymousBehaviorContext", declarations)
        | None -> Assert.True(false, "Expected the scripting declarations to be available.")

    [<Fact>]
    let ``Compiler diagnostics map to behavior module local lines`` () =
        let repoRoot = findRepoRoot (System.IO.DirectoryInfo(System.AppContext.BaseDirectory))
        let source =
            BehaviorSources.joinModules
                [ "valid-module", "const validValue: string = 'ok';"
                  "broken-module", "const brokenValue: string = 42;" ]

        match ScriptCompiler.compile repoRoot source with
        | Error diagnostics ->
            let diagnostic = diagnostics |> List.find (fun diagnostic -> diagnostic.file = "broken-module")
            Assert.Equal(1, diagnostic.line)
            Assert.True(diagnostic.column > 0)
            Assert.Contains("number", diagnostic.message)
        | Ok _ -> Assert.True(false, "Expected the broken module to fail compilation.")

    [<Fact>]
    let ``Compiled behavior classes use native super dispatch`` () =
        let compiled = compileBehavior forestSource |> Result.defaultWith failwith

        let result =
            Scripting.executeBehaviorMethod "ForestBehavior" "look" ObjectDatabase.initialState forest Map.empty testActor compiled

        match result with
        | Ok [ EmitMessage description; EmitMessage atmosphere ] ->
            Assert.Equal("location.forest.description", description.Key)
            Assert.Equal("location.forest.atmosphere", atmosphere.Key)
        | Ok _ -> Assert.True(false, "Expected parent and child message effects.")
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Checked-in compiled behavior matches TypeScript source behavior`` () =
        let compiled = compileBehavior forestSource |> Result.defaultWith failwith
        let args = Map.ofList [ "item", "wood" ]

        let fromCompiler =
            Scripting.executeBehaviorMethod "ForestBehavior" "gather" ObjectDatabase.initialState forest args testActor compiled

        let checkedIn =
            Scripting.executeBehaviorMethod "ForestBehavior" "gather" ObjectDatabase.initialState forest args testActor forestCompiled

        Assert.Equal<Result<ScriptEffect list, string>>(fromCompiler, checkedIn)

        let compiledMetadata = Scripting.inspectBehaviorModule "forestBehaviorClasses" compiled
        let checkedInMetadata = Scripting.inspectBehaviorModule "forestBehaviorClasses" forestCompiled
        Assert.Equal<Result<Map<string, BehaviorClassDefinition>, CompilerDiagnostic>>(compiledMetadata, checkedInMetadata)

    [<Fact>]
    let ``Gatherable interface requires a gather method`` () =
        let invalidSource =
            forestSource.Replace(
                "  gather(context: VerbContext): VerbResult {",
                "  harvest(context: VerbContext): VerbResult {")

        match compileBehavior invalidSource with
        | Error error -> Assert.Contains("Gatherable", error)
        | Ok _ -> Assert.True(false, "Expected the missing Gatherable method to fail compilation.")

    [<Fact>]
    let ``Checked-in anonymous behavior matches TypeScript source behavior`` () =
        let source = BehaviorSources.join [ BehaviorSources.core; BehaviorSources.anonymous ]
        let compiled = compileBehavior source |> Result.defaultWith failwith
        let checkedIn = BehaviorSources.join [ BehaviorSources.coreCompiled; BehaviorSources.anonymousCompiled ]
        let value =
            { BehaviorModuleId = "anonymous-behaviors"
              BehaviorClassName = "TrailTokenBehavior"
              Properties = Map.ofList [ "label", StringValue "test token" ] }

        let fromCompiler =
            Scripting.executeAnonymousBehaviorMethod "TrailTokenBehavior" "describe" value Map.empty ObjectDatabase.initialState testActor compiled

        let fromCheckedIn =
            Scripting.executeAnonymousBehaviorMethod "TrailTokenBehavior" "describe" value Map.empty ObjectDatabase.initialState testActor checkedIn

        Assert.Equal<Result<ScriptEffect list, string>>(fromCompiler, fromCheckedIn)

    [<Theory>]
    [<InlineData("ForestBehavior;attack", "look")>]
    [<InlineData("ForestBehavior", "look()")>]
    let ``Behavior invocation rejects invalid identifiers`` className methodName =
        let result =
            Scripting.executeBehaviorMethod className methodName ObjectDatabase.initialState forest Map.empty testActor ""

        match result with
        | Error error -> Assert.Equal("Behavior class and method names must be valid JavaScript identifiers.", error)
        | Ok _ -> Assert.True(false, "Expected invalid identifiers to be rejected.")

module AnonymousBehaviorValueTests =
    [<Fact>]
    let ``Anonymous values are stored in permanent object properties and execute behavior`` () =
        let state = ObjectDatabase.initialState

        match state.Objects["forest"].Properties["trailToken"] with
        | AnonymousValue value ->
            Assert.Equal("anonymous-behaviors", value.BehaviorModuleId)
            Assert.Equal("TrailTokenBehavior", value.BehaviorClassName)
            Assert.Equal<GameValue>(StringValue "old forest trail", value.Properties["label"])

            match Kernel.executeAnonymousValueMethod "describe" Map.empty value state with
            | Ok [ EmitMessage message ] ->
                Assert.Equal("token.describe", message.Key)
                Assert.Equal("old forest trail", message.Args["label"])
            | Ok _ -> Assert.True(false, "Expected one message effect.")
            | Error error -> Assert.True(false, error)
        | _ -> Assert.True(false, "Expected an anonymous trail token.")

    [<Fact>]
    let ``Anonymous values recursively validate permanent object references`` () =
        let state = ObjectDatabase.initialState
        let value =
            { BehaviorModuleId = "anonymous-behaviors"
              BehaviorClassName = "TrailTokenBehavior"
              Properties = Map.ofList [ "target", ObjectReferenceValue "missing" ] }

        match Kernel.executeAnonymousValueMethod "describe" Map.empty value state with
        | Error error -> Assert.Equal("Property anonymous.target references unknown object id: missing", error)
        | Ok _ -> Assert.True(false, "Expected an invalid nested object reference to be rejected.")

    [<Fact>]
    let ``Anonymous values require a registered behavior class`` () =
        let value =
            { BehaviorModuleId = "anonymous-behaviors"
              BehaviorClassName = "MissingBehavior"
              Properties = Map.empty }

        match Kernel.executeAnonymousValueMethod "describe" Map.empty value ObjectDatabase.initialState with
        | Error error -> Assert.Equal("Anonymous value anonymous references unknown behavior class: MissingBehavior", error)
        | Ok _ -> Assert.True(false, "Expected an unknown behavior class to be rejected.")

    [<Fact>]
    let ``Behavior updates cannot remove classes referenced by anonymous values`` () =
        let state = ObjectDatabase.initialState

        let result =
            Kernel.tryUpdateBehaviorModule
                Ok
                (fun _ _ -> Ok Map.empty)
                "anonymous-behaviors"
                BehaviorSources.anonymous
                state

        match result with
        | Error [ diagnostic ] ->
            Assert.Equal("Anonymous value trailToken references unknown behavior class: TrailTokenBehavior", diagnostic.message)
        | Error _ -> Assert.True(false, "Expected one missing-class diagnostic.")
        | Ok _ -> Assert.True(false, "Expected the referenced anonymous behavior class removal to be rejected.")

    [<Fact>]
    let ``Stored anonymous behavior atomically replaces its nested property`` () =
        let state = ObjectDatabase.initialState
        let path = [ PropertySegment "trailToken" ]

        match Kernel.invokeStoredAnonymousValueMethod "forest" path "rename" (Map.ofList [ "label", "new trail" ]) state with
        | Ok result ->
            Assert.Equal("trail.renamed", (result.Messages |> List.exactlyOne).Key)

            match result.State.Objects["forest"].Properties["trailToken"] with
            | AnonymousValue updated -> Assert.Equal<GameValue>(StringValue "new trail", updated.Properties["label"])
            | _ -> Assert.True(false, "Expected the trail token to remain anonymous.")

            match state.Objects["forest"].Properties["trailToken"] with
            | AnonymousValue original -> Assert.Equal<GameValue>(StringValue "old forest trail", original.Properties["label"])
            | _ -> Assert.True(false, "Expected the original trail token.")
        | Error error -> Assert.True(false, error)

    [<Theory>]
    [<InlineData("name trail green way", "en", "You name the trail green way.")>]
    [<InlineData("nenne pfad grüner weg", "de", "Du nennst den Pfad grüner weg.")>]
    let ``Permanent behavior invokes stored anonymous behavior from localized commands`` command cultureName expected =
        let culture = if cultureName = "de" then De else En
        let result = Kernel.submitCommand culture command ObjectDatabase.initialState

        match result.State.Objects["forest"].Properties["trailToken"] with
        | AnonymousValue updated -> Assert.Equal<GameValue>(StringValue(command.Split(' ', 3)[2]), updated.Properties["label"])
        | _ -> Assert.True(false, "Expected the trail token to remain anonymous.")

        let line = result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage result.State culture
        Assert.Equal(expected, line)

    [<Fact>]
    let ``Recursive anonymous invocation is bounded and atomic`` () =
        let state = ObjectDatabase.initialState
        let behaviorModule = state.BehaviorModules["anonymous-behaviors"]
        let recursiveRename =
            """TrailTokenBehavior.prototype.rename = function(context) {
  return { effects: [{
    type: "invokeAnonymous",
    path: context.this.storagePath,
    methodName: "rename",
    args: context.args
  }] };
};"""
        let changedModule = { behaviorModule with CompiledSource = behaviorModule.CompiledSource + "\n" + recursiveRename }
        let changedState = { state with BehaviorModules = Map.add behaviorModule.Id changedModule state.BehaviorModules }
        let result = Kernel.submitCommand En "name trail loop" changedState

        let error = result.Messages |> List.exactlyOne
        Assert.Equal("script.error", error.Key)
        Assert.Equal("Anonymous behavior invocation depth may not exceed 8.", error.Args["error"])
        Assert.Equal(changedState.Objects["forest"].Properties["trailToken"], result.State.Objects["forest"].Properties["trailToken"])

    [<Fact>]
    let ``Invalid replacement rolls back earlier effects in the batch`` () =
        let state = ObjectDatabase.initialState
        let behaviorModule = state.BehaviorModules["anonymous-behaviors"]
        let invalidRename =
            """TrailTokenBehavior.prototype.rename = function() {
  return { effects: [
    { type: "addInventory", itemId: "wood", amount: 1 },
    { type: "replaceValue", path: ["missing"], value: "changed" }
  ] };
};"""
        let changedModule = { behaviorModule with CompiledSource = behaviorModule.CompiledSource + "\n" + invalidRename }
        let changedState = { state with BehaviorModules = Map.add behaviorModule.Id changedModule state.BehaviorModules }

        let result =
            Kernel.invokeStoredAnonymousValueMethod
                "forest"
                [ PropertySegment "trailToken" ]
                "rename"
                Map.empty
                changedState

        match result with
        | Error error ->
            Assert.Equal("replaceValue path does not contain object property: missing", error)
            Assert.Empty(changedState.Player.Inventory)
        | Ok _ -> Assert.True(false, "Expected the invalid replacement batch to fail.")

    [<Fact>]
    let ``Replacement paths traverse anonymous maps and lists`` () =
        let state = ObjectDatabase.initialState
        let forest = state.Objects["forest"]
        let token =
            match forest.Properties["trailToken"] with
            | AnonymousValue value ->
                { value with
                    Properties =
                        value.Properties
                        |> Map.add
                            "settings"
                            (MapValue(Map.ofList [ "labels", ListValue [ StringValue "first"; StringValue "second" ] ])) }
            | _ -> failwith "Expected the trail token."
        let changedForest = { forest with Properties = Map.add "trailToken" (AnonymousValue token) forest.Properties }
        let behaviorModule = state.BehaviorModules["anonymous-behaviors"]
        let nestedRename =
            """TrailTokenBehavior.prototype.rename = function(context) {
  return { effects: [{
    type: "replaceValue",
    path: [...context.this.storagePath, "settings", "labels", 1],
    value: context.args.label
  }] };
};"""
        let changedModule = { behaviorModule with CompiledSource = behaviorModule.CompiledSource + "\n" + nestedRename }
        let changedState =
            { state with
                Objects = Map.add forest.Id changedForest state.Objects
                BehaviorModules = Map.add behaviorModule.Id changedModule state.BehaviorModules }

        match
            Kernel.invokeStoredAnonymousValueMethod
                "forest"
                [ PropertySegment "trailToken" ]
                "rename"
                (Map.ofList [ "label", "updated" ])
                changedState
        with
        | Ok result ->
            match result.State.Objects["forest"].Properties["trailToken"] with
            | AnonymousValue updated ->
                match updated.Properties["settings"] with
                | MapValue settings ->
                    Assert.Equal<GameValue>(
                        ListValue [ StringValue "first"; StringValue "updated" ],
                        settings["labels"])
                | _ -> Assert.True(false, "Expected settings to remain a map.")
            | _ -> Assert.True(false, "Expected the token to remain anonymous.")
        | Error error -> Assert.True(false, error)
