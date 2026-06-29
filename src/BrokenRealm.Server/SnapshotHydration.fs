namespace BrokenRealm.Server

open System
open System.IO

module SnapshotLoading =
    let objectBehaviorModuleIds = BehaviorGraph.objectBehaviorModuleIds

    let prepare snapshot =
        if snapshot.FormatVersion > GameSnapshots.CurrentFormatVersion then
            Error $"Snapshot format version {snapshot.FormatVersion} is newer than this server supports ({GameSnapshots.CurrentFormatVersion})."
        elif snapshot.FormatVersion < GameSnapshots.CurrentFormatVersion then
            Error
                $"Snapshot format version {snapshot.FormatVersion} is outdated. Delete the snapshot file and restart to create a fresh world (current format is {GameSnapshots.CurrentFormatVersion})."
        elif not (Map.isEmpty snapshot.Characters) then
            Error "Snapshot contains legacy character records. Delete the snapshot file and restart."
        else
            match BehaviorSources.tryResolveServerRoot () with
            | Some serverRoot -> BehaviorGraph.reconcileBehaviorModules serverRoot snapshot |> Ok
            | None -> Ok snapshot

module SnapshotHydration =
    let private behaviorModulesFromSnapshot (modules: Map<string, BehaviorModuleSnapshot>) =
        modules
        |> Map.map (fun _ moduleSnapshot ->
            { Id = moduleSnapshot.Id
              RegistryName = moduleSnapshot.RegistryName
              Dependencies = moduleSnapshot.Dependencies
              Source = moduleSnapshot.Source
              CompiledSource = ""
              Classes = Map.empty })

    let private mergeSeedBehaviorModules (modules: Map<string, BehaviorModule>) =
        ObjectDatabase.initialState.BehaviorModules
        |> Map.fold
            (fun merged moduleId seedModule ->
                if Map.containsKey moduleId merged then
                    merged
                else
                    Map.add moduleId seedModule merged)
            modules

    let private accountsFromSnapshot (accounts: Map<AccountId, AccountSnapshot>) =
        let mapped =
            accounts
            |> Map.map (fun _ account ->
                ({ Id = account.Id
                   DisplayName = account.DisplayName
                   PasswordHash = account.PasswordHash }: AccountState))

        match mapped |> Map.tryFind GameSnapshots.PrototypeAccountId with
        | Some account when account.PasswordHash.IsNone ->
            Map.add
                GameSnapshots.PrototypeAccountId
                { account with PasswordHash = Some(Auth.hashPassword "prototype") }
                mapped
        | _ -> mapped

    let private validateObjectIds (objects: Map<ObjectId, GameObject>) =
        objects
        |> Map.toList
        |> List.tryPick (fun (objectId, object) ->
            if objectId <> object.Id then
                Some $"Object map key '{objectId}' does not match embedded object id '{object.Id}'."
            elif not (ObjectIds.isValid objectId) then
                Some $"Invalid object id: {objectId}"
            else
                None)
        |> Option.map Error
        |> Option.defaultValue (Ok())

    let private validateAccountRecords (accounts: Map<AccountId, AccountSnapshot>) =
        accounts
        |> Map.toList
        |> List.tryPick (fun (accountId, account) ->
            if accountId <> account.Id then
                Some $"Account map key '{accountId}' does not match embedded account id '{account.Id}'."
            else
                None)
        |> Option.map Error
        |> Option.defaultValue (Ok())

    let private validateBehaviorModuleRecords (modules: Map<string, BehaviorModuleSnapshot>) =
        modules
        |> Map.toList
        |> List.tryPick (fun (moduleId, behaviorModule) ->
            if moduleId <> behaviorModule.Id then
                Some $"Behavior module map key '{moduleId}' does not match embedded module id '{behaviorModule.Id}'."
            elif
                behaviorModule.Dependencies
                |> List.exists (fun dependencyId -> not (modules.ContainsKey dependencyId))
            then
                Some $"Behavior module {moduleId} references a missing dependency."
            else
                None)
        |> Option.map Error
        |> Option.defaultValue (Ok())

    let private mapLayoutPropertyKeys =
        [ RoomMap.MapCodeProperty
          RoomMap.MapRegionProperty
          RoomMap.MapXProperty
          RoomMap.MapYProperty ]

    let private reconcileSeedMapLayout (objects: Map<ObjectId, GameObject>) =
        ObjectDatabase.initialState.Objects
        |> Map.fold
            (fun merged objectId seedObject ->
                match Map.tryFind objectId merged with
                | None -> merged
                | Some hydrated ->
                    let missing =
                        mapLayoutPropertyKeys
                        |> List.choose (fun key ->
                            if Map.containsKey key hydrated.Properties then
                                None
                            else
                                Map.tryFind key seedObject.Properties |> Option.map (fun value -> key, value))
                        |> Map.ofList

                    if Map.isEmpty missing then
                        merged
                    else
                        let properties =
                            missing
                            |> Map.fold (fun properties key value -> Map.add key value properties) hydrated.Properties

                        Map.add objectId { hydrated with Properties = properties } merged)
            objects

    let private validateObjectBehaviorModuleReferences (objects: Map<ObjectId, GameObject>) (modules: Map<string, BehaviorModuleSnapshot>) =
        objects
        |> Map.toList
        |> List.collect (fun (_, gameObject) -> SnapshotLoading.objectBehaviorModuleIds gameObject)
        |> List.distinct
        |> List.tryPick (fun moduleId ->
            if Map.containsKey moduleId modules then
                None
            else
                Some $"Object references behavior module '{moduleId}' that is missing from the snapshot.")
        |> Option.map Error
        |> Option.defaultValue (Ok())

    let validateSnapshot snapshot =
        [ validateObjectIds snapshot.World.Objects
          validateAccountRecords snapshot.Accounts
          validateBehaviorModuleRecords snapshot.World.BehaviorModules
          validateObjectBehaviorModuleReferences snapshot.World.Objects snapshot.World.BehaviorModules ]
        |> List.tryPick (function
            | Error error -> Some(Error error)
            | Ok() -> None)
        |> Option.defaultWith (fun () -> Ok())

    let hydrate
        (compile: string -> Result<string, CompilerDiagnostic list>)
        (inspect: string -> string -> Result<Map<string, BehaviorClassDefinition>, CompilerDiagnostic>)
        snapshot
        =
        SnapshotLoading.prepare snapshot
        |> Result.bind (fun prepared ->
            validateSnapshot prepared
            |> Result.bind (fun () ->
                let runtimeBehaviorModules =
                    behaviorModulesFromSnapshot prepared.World.BehaviorModules

                Kernel.recompileBehaviorModules compile inspect runtimeBehaviorModules
                |> Result.mapError (fun diagnostics ->
                    diagnostics
                    |> List.map _.message
                    |> String.concat Environment.NewLine)
                |> Result.bind (fun activeBehaviorModules ->
                    let activeBehaviorModules = mergeSeedBehaviorModules activeBehaviorModules
                    let graphReferences = BehaviorGraph.collectBehaviorGraphReferences prepared

                    BehaviorGraph.validateBehaviorGraphReferences activeBehaviorModules graphReferences
                    |> Result.mapError (String.concat Environment.NewLine)
                    |> Result.bind (fun () ->
                        let state =
                            { ItemIds = prepared.World.ItemIds
                              BehaviorModules = activeBehaviorModules
                              Objects = reconcileSeedMapLayout prepared.World.Objects
                              Accounts = accountsFromSnapshot prepared.Accounts }

                        Kernel.validateGameState state
                        |> Result.map (fun () -> state, prepared)))))

type FileGameStore(snapshotPath: string, initialState: GameState, ?clock: unit -> DateTimeOffset, ?seedSnapshot: GameSnapshot) =
    let inner = InMemoryGameStore(initialState, ?clock = clock, ?seedSnapshot = seedSnapshot)

    let persist () =
        SnapshotCodec.writeFile snapshotPath (inner.GetSnapshot())

    member _.SnapshotPath = snapshotPath

    member _.Read() = inner.Read()

    member _.GetSnapshot() = inner.GetSnapshot()

    member _.TryCommit(expectedWorldRevision, expectedCharacterRevisions, state: GameState) =
        match inner.TryCommit(expectedWorldRevision, expectedCharacterRevisions, state) with
        | Ok committed ->
            persist()
            Ok committed
        | Error conflict -> Error conflict

    member _.CreateBackup(clock: unit -> DateTimeOffset) =
        SnapshotBackup.create snapshotPath (inner.GetSnapshot()) clock

    member _.Flush() = persist()

    member _.TryRestore(contentRoot: string, fileName: string) =
        match SnapshotBackup.resolveBackupPath snapshotPath fileName with
        | Error error -> Error error
        | Ok sourcePath when not (File.Exists sourcePath) ->
            Error $"Backup file does not exist: {fileName}"
        | Ok sourcePath ->
            SnapshotCodec.tryReadFile sourcePath
            |> Result.bind SnapshotLoading.prepare
            |> Result.bind (SnapshotHydration.hydrate (ScriptCompiler.compile contentRoot) Scripting.inspectBehaviorModule)
            |> Result.map (fun (state, replacementSnapshot) ->
                inner.Replace(state, replacementSnapshot)
                persist()
                replacementSnapshot)

module GameStoreBootstrap =
    let defaultSnapshotPath contentRoot =
        Path.Combine(contentRoot, "data", "game-snapshot.json")

    let resolveSnapshotPath contentRoot =
        Environment.GetEnvironmentVariable("BROKENREALM_SNAPSHOT_PATH")
        |> Option.ofObj
        |> Option.map Path.GetFullPath
        |> Option.defaultValue (defaultSnapshotPath contentRoot)

    let tryLoad contentRoot snapshotPath =
        SnapshotCodec.tryReadFile snapshotPath
        |> Result.bind SnapshotLoading.prepare
        |> Result.bind (SnapshotHydration.hydrate (ScriptCompiler.compile contentRoot) Scripting.inspectBehaviorModule)

    let createGameStore contentRoot snapshotPath =
        ObjectDatabase.bootstrap contentRoot |> ignore

        let store =
            if File.Exists snapshotPath then
                match tryLoad contentRoot snapshotPath with
                | Ok(state, snapshot) ->
                    FileGameStore(snapshotPath, Limbo.limboAllPlayers state, seedSnapshot = snapshot)
                | Error error -> failwith $"Failed to hydrate game snapshot from '{snapshotPath}': {error}"
            else
                FileGameStore(snapshotPath, Limbo.limboAllPlayers ObjectDatabase.initialState)

        store.Flush()
        store