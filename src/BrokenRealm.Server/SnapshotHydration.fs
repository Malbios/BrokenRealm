namespace BrokenRealm.Server

open System
open System.IO

module SnapshotMigrations =
    let private prototypeAccount: AccountSnapshot =
        { Id = GameSnapshots.PrototypeAccountId
          DisplayName = Some "Prototype account" }

    let private migrateV1 snapshot =
        let accounts =
            if Map.isEmpty snapshot.Accounts then
                Map.ofList [ prototypeAccount.Id, prototypeAccount ]
            else
                snapshot.Accounts

        let characters =
            snapshot.Characters
            |> Map.map (fun _ character ->
                { character with
                    AccountId =
                        if String.IsNullOrWhiteSpace character.AccountId then
                            GameSnapshots.PrototypeAccountId
                        else
                            character.AccountId })

        { snapshot with
            FormatVersion = 2
            Accounts = accounts
            Characters = characters
            PlayerRevisions = Map.empty }

    let private characterToPlayerObject (character: CharacterSnapshot) =
        let name =
            if character.Id = GameSnapshots.PrototypeCharacterId then
                "prototype player"
            elif character.Id = GameSnapshots.PrototypeScoutCharacterId then
                "prototype scout"
            else
                character.Id.Replace('-', ' ')

        let nameKey = $"object.{character.Id}.name"

        PlayerObjects.create character.Id name nameKey character.AccountId character.LocationId character.Inventory

    let private ensurePrototypeDevelopmentCharacters snapshot =
        let seedPlayers =
            ObjectDatabase.initialState.Objects
            |> Map.toList
            |> List.choose (fun (_, gameObject) ->
                if PlayerObjects.isPlayer gameObject then
                    Some
                        { Id = gameObject.Id
                          AccountId = PlayerObjects.accountId gameObject
                          Revision = 0L
                          LocationId = PlayerObjects.locationId gameObject
                          Inventory = PlayerObjects.inventory gameObject }
                else
                    None)
            |> List.map (fun character -> character.Id, character)
            |> Map.ofList

        let accounts =
            if Map.containsKey GameSnapshots.PrototypeAccountId snapshot.Accounts then
                snapshot.Accounts
            elif Map.isEmpty seedPlayers then
                snapshot.Accounts
            else
                Map.add GameSnapshots.PrototypeAccountId prototypeAccount snapshot.Accounts

        let characters =
            seedPlayers
            |> Map.fold (fun characters characterId seedCharacter ->
                match Map.tryFind characterId characters with
                | Some (existing: CharacterSnapshot) ->
                    Map.add
                        characterId
                        { existing with
                            AccountId = GameSnapshots.PrototypeAccountId }
                        characters
                | None ->
                    let added: CharacterSnapshot =
                        { Id = seedCharacter.Id
                          AccountId = seedCharacter.AccountId
                          Revision = 0L
                          LocationId = seedCharacter.LocationId
                          Inventory = seedCharacter.Inventory }

                    Map.add characterId added characters) snapshot.Characters

        { snapshot with
            Accounts = accounts
            Characters = characters }

    let private migrateV2 snapshot =
        let playerObjects =
            snapshot.Characters
            |> Map.map (fun _ character -> characterToPlayerObject character)

        let objects =
            playerObjects
            |> Map.fold (fun objects playerId playerObject -> Map.add playerId playerObject objects) snapshot.World.Objects

        let playerRevisions =
            snapshot.Characters
            |> Map.map (fun _ character -> character.Revision)

        { snapshot with
            FormatVersion = 3
            World = { snapshot.World with Objects = objects }
            Characters = Map.empty
            PlayerRevisions = playerRevisions }

    let migrate snapshot =
        if snapshot.FormatVersion > GameSnapshots.CurrentFormatVersion then
            Error $"Snapshot format version {snapshot.FormatVersion} is newer than this server supports ({GameSnapshots.CurrentFormatVersion})."
        elif snapshot.FormatVersion < 1 then
            Error $"Snapshot format version {snapshot.FormatVersion} is not supported."
        else
            let migrated =
                if snapshot.FormatVersion = 1 then
                    migrateV1 snapshot
                else
                    snapshot

            let withSeed =
                if migrated.FormatVersion < 3 then
                    ensurePrototypeDevelopmentCharacters migrated
                else
                    migrated

            Ok(
                if withSeed.FormatVersion < 3 then
                    migrateV2 withSeed
                else
                    withSeed)

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

    let private accountsFromSnapshot (accounts: Map<AccountId, AccountSnapshot>) =
        accounts
        |> Map.map (fun _ account ->
            ({ Id = account.Id; DisplayName = account.DisplayName }: AccountState))

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

    let private validateLegacyCharacterRecords (snapshot: GameSnapshot) =
        if snapshot.FormatVersion >= 3 then
            Ok()
        else
            snapshot.Characters
            |> Map.toList
            |> List.tryPick (fun (characterId, character) ->
                if characterId <> character.Id then
                    Some $"Character map key '{characterId}' does not match embedded character id '{character.Id}'."
                elif not (snapshot.Accounts.ContainsKey character.AccountId) then
                    Some $"Character {character.Id} references unknown account id: {character.AccountId}"
                elif not (snapshot.World.Objects.ContainsKey character.LocationId) then
                    Some $"Character {character.Id} references unknown location id: {character.LocationId}"
                elif
                    character.Inventory
                    |> Map.toList
                    |> List.exists (fun (itemId, quantity) ->
                        not (snapshot.World.ItemIds.Contains itemId) || quantity < 0)
                then
                    Some $"Character {character.Id} has invalid inventory entries."
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

    let validateSnapshot snapshot =
        [ validateObjectIds snapshot.World.Objects
          validateAccountRecords snapshot.Accounts
          validateLegacyCharacterRecords snapshot
          validateBehaviorModuleRecords snapshot.World.BehaviorModules ]
        |> List.tryPick (function
            | Error error -> Some(Error error)
            | Ok() -> None)
        |> Option.defaultWith (fun () -> Ok())

    let hydrate
        (compile: string -> Result<string, CompilerDiagnostic list>)
        (inspect: string -> string -> Result<Map<string, BehaviorClassDefinition>, CompilerDiagnostic>)
        snapshot
        =
        validateSnapshot snapshot
        |> Result.bind (fun () ->
            let runtimeBehaviorModules =
                behaviorModulesFromSnapshot snapshot.World.BehaviorModules

            Kernel.recompileBehaviorModules compile inspect runtimeBehaviorModules
            |> Result.mapError (fun diagnostics ->
                diagnostics
                |> List.map _.message
                |> String.concat Environment.NewLine)
            |> Result.bind (fun activeBehaviorModules ->
                let state =
                    { ItemIds = snapshot.World.ItemIds
                      BehaviorModules = activeBehaviorModules
                      Objects = snapshot.World.Objects
                      Accounts = accountsFromSnapshot snapshot.Accounts }

                Kernel.validateGameState state
                |> Result.map (fun () -> state, snapshot)))

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
        |> Result.bind SnapshotMigrations.migrate
        |> Result.bind (SnapshotHydration.hydrate (ScriptCompiler.compile contentRoot) Scripting.inspectBehaviorModule)

    let createGameStore contentRoot snapshotPath =
        if File.Exists snapshotPath then
            match tryLoad contentRoot snapshotPath with
            | Ok(state, snapshot) -> FileGameStore(snapshotPath, state, seedSnapshot = snapshot)
            | Error error -> failwith $"Failed to hydrate game snapshot from '{snapshotPath}': {error}"
        else
            FileGameStore(snapshotPath, ObjectDatabase.initialState)