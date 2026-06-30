namespace BrokenRealm.Server

open System
open System.Text.RegularExpressions

module BehaviorGraph =
    type SeedDriftInfo =
        { SeedHashChanged: bool
          SyncedSeedHash: string
          CurrentSeedHash: string }

    let hashSource = BehaviorSources.hashSource

    let private classNamePattern = Regex(@"\bclass\s+(\w+)", RegexOptions.Compiled)

    let private behaviorClassReferencePattern =
        Regex(
            @"behaviorModuleId:\s*""([^""]+)""\s*,\s*behaviorClassName:\s*""([^""]+)""",
            RegexOptions.Compiled)

    let classNamesDeclaredInSource (source: string) =
        classNamePattern.Matches source
        |> Seq.map (fun match' -> match'.Groups[1].Value)
        |> Set.ofSeq

    let private behaviorClassReferencesFromSource (source: string) =
        behaviorClassReferencePattern.Matches source
        |> Seq.map (fun match' -> match'.Groups[1].Value, match'.Groups[2].Value)
        |> Set.ofSeq

    let rec private valueBehaviorClassReferences value =
        match value with
        | AnonymousValue anonymous ->
            (anonymous.BehaviorModuleId, anonymous.BehaviorClassName)
            :: (anonymous.Properties
                |> Map.toList
                |> List.collect (fun (_, nested) -> valueBehaviorClassReferences nested))
        | ListValue values -> values |> List.collect valueBehaviorClassReferences
        | MapValue values -> values |> Map.toList |> List.collect (fun (_, nested) -> valueBehaviorClassReferences nested)
        | _ -> []

    let rec private valueBehaviorModuleIds value =
        match value with
        | AnonymousValue anonymous ->
            anonymous.BehaviorModuleId
            :: (anonymous.Properties |> Map.toList |> List.collect (fun (_, nested) -> valueBehaviorModuleIds nested))
        | ListValue values -> values |> List.collect valueBehaviorModuleIds
        | MapValue values -> values |> Map.toList |> List.collect (fun (_, nested) -> valueBehaviorModuleIds nested)
        | _ -> []

    let objectBehaviorModuleIds (gameObject: GameObject) =
        gameObject.BehaviorModuleId
        :: (gameObject.Properties |> Map.toList |> List.collect (fun (_, value) -> valueBehaviorModuleIds value))

    let private objectBehaviorClassReferences (objects: Map<ObjectId, GameObject>) =
        objects
        |> Map.toList
        |> List.collect (fun (_, gameObject) ->
            (gameObject.BehaviorModuleId, gameObject.BehaviorClassName)
            :: (gameObject.Properties
                |> Map.toList
                |> List.collect (fun (_, value) -> valueBehaviorClassReferences value)))

    let collectBehaviorGraphReferences (snapshot: GameSnapshot) =
        let fromObjects = objectBehaviorClassReferences snapshot.World.Objects |> Set.ofList

        let fromSources =
            snapshot.World.BehaviorModules
            |> Map.toList
            |> List.collect (fun (_, behaviorModule) ->
                behaviorClassReferencesFromSource behaviorModule.Source |> Set.toList)
            |> Set.ofList

        Set.union fromObjects fromSources

    let validateBehaviorGraphReferences
        (modules: Map<string, BehaviorModule>)
        (references: Set<string * string>)
        : Result<unit, string list> =
        references
        |> Set.toList
        |> List.choose (fun (moduleId, className) ->
            match Map.tryFind moduleId modules with
            | None -> Some $"Behavior graph references missing module '{moduleId}' for class '{className}'."
            | Some behaviorModule when not (behaviorModule.Classes.ContainsKey className) ->
                Some $"Behavior graph references missing class '{className}' in module '{moduleId}'."
            | _ -> None)
        |> function
            | [] -> Ok()
            | errors -> Error errors

    let graphWarnings (modules: Map<string, BehaviorModule>) (references: Set<string * string>) =
        match validateBehaviorGraphReferences modules references with
        | Ok() -> []
        | Error errors -> errors

    let private seedHashMap (serverRoot: string) =
        BehaviorSources.seedManifest serverRoot
        |> List.map (fun entry -> entry.ModuleId, entry.Sha256)
        |> Map.ofList

    let private lookupCurrentSeedHash (serverRoot: string) moduleId =
        seedHashMap serverRoot
        |> Map.tryFind moduleId
        |> Option.defaultValue ""

    let computeSeedDrift (serverRoot: string) (moduleSnapshot: BehaviorModuleSnapshot) : SeedDriftInfo =
        let seedHash = lookupCurrentSeedHash serverRoot moduleSnapshot.Id

        { SeedHashChanged = moduleSnapshot.SyncedSeedHash <> seedHash
          SyncedSeedHash = moduleSnapshot.SyncedSeedHash
          CurrentSeedHash = seedHash }

    let private forceReseedEnabled () =
        Environment.GetEnvironmentVariable("BROKENREALM_RESEED_BEHAVIORS") = "1"

    let private seedModuleSnapshot
        (serverRoot: string)
        (activatedAt: DateTimeOffset)
        (seedModule: BehaviorSources.SeedModule)
        =
        { Id = seedModule.Id
          RegistryName = seedModule.RegistryName
          Dependencies = seedModule.Dependencies
          Source = seedModule.Source
          SourceRevision = 0L
          ActivationRevision = 0L
          ActivatedAt = activatedAt
          Provenance = SeedSynced
          SyncedSeedHash = lookupCurrentSeedHash serverRoot seedModule.Id }

    let private missingReferencedClasses moduleId (referenced: Set<string * string>) (declared: Set<string>) =
        referenced
        |> Set.filter (fun (referencedModuleId, className) -> referencedModuleId = moduleId)
        |> Set.map snd
        |> Set.filter (fun className -> not (declared.Contains className))

    let private shouldUpgradeFromSeed
        (serverRoot: string)
        (moduleSnapshot: BehaviorModuleSnapshot)
        (seedSource: string)
        (referenced: Set<string * string>)
        =
        let forceReseed = forceReseedEnabled ()
        let sourceHash = hashSource moduleSnapshot.Source
        let seedSourceHash = hashSource seedSource
        let seedDeclared = classNamesDeclaredInSource seedSource
        let declared = classNamesDeclaredInSource moduleSnapshot.Source
        let missing = missingReferencedClasses moduleSnapshot.Id referenced declared

        match moduleSnapshot.Provenance, forceReseed with
        | AdminEdited, false -> false
        | AdminEdited, true -> true
        | SeedSynced, true -> true
        | SeedSynced, false ->
            if sourceHash <> seedSourceHash then
                true
            elif not missing.IsEmpty && missing |> Set.forall (fun className -> seedDeclared.Contains className) then
                true
            else
                false

    let private upgradeFromSeed
        (serverRoot: string)
        (moduleSnapshot: BehaviorModuleSnapshot)
        (seedModule: BehaviorSources.SeedModule)
        =
        { moduleSnapshot with
            RegistryName = seedModule.RegistryName
            Dependencies = seedModule.Dependencies
            Source = seedModule.Source
            Provenance = SeedSynced
            SyncedSeedHash = lookupCurrentSeedHash serverRoot seedModule.Id }

    let reconcileBehaviorModules (serverRoot: string) (snapshot: GameSnapshot) =
        let activatedAt = DateTimeOffset.UtcNow
        let seedModules = BehaviorSources.loadSeedModules serverRoot
        let seedById = seedModules |> List.map (fun seedModule -> seedModule.Id, seedModule) |> Map.ofList
        let seedSnapshots = seedModules |> List.map (seedModuleSnapshot serverRoot activatedAt) |> List.map (fun s -> s.Id, s) |> Map.ofList
        let referenced = collectBehaviorGraphReferences snapshot

        let repairedModules =
            let existing =
                snapshot.World.BehaviorModules
                |> Map.map (fun moduleId moduleSnapshot ->
                    match Map.tryFind moduleId seedById with
                    | None -> moduleSnapshot
                    | Some seedModule ->
                        if shouldUpgradeFromSeed serverRoot moduleSnapshot seedModule.Source referenced then
                            upgradeFromSeed serverRoot moduleSnapshot seedModule
                        else
                            { moduleSnapshot with
                                RegistryName = seedModule.RegistryName
                                Dependencies = seedModule.Dependencies })

            seedSnapshots
            |> Map.fold
                (fun modules moduleId seedSnapshot ->
                    if Map.containsKey moduleId modules then
                        modules
                    else
                        Map.add moduleId seedSnapshot modules)
                existing

        { snapshot with
            World = { snapshot.World with BehaviorModules = repairedModules } }
