namespace BrokenRealm.Server

open System
open System.IO
open System.Security.Cryptography
open System.Text

module BehaviorSources =
    let moduleMarkerPrefix = "// @brokenrealm-module "

    type SeedModuleDefinition =
        { Id: string
          RegistryName: string
          Dependencies: string list
          FileName: string }

    type SeedManifestEntry = { ModuleId: string; Sha256: string }

    type SeedModule =
        { Id: string
          RegistryName: string
          Dependencies: string list
          Source: string }

    let seedModuleDefinitions: SeedModuleDefinition list =
        [ { Id = "core-behaviors"
            RegistryName = "coreBehaviorClasses"
            Dependencies = []
            FileName = "core-behaviors.ts" }
          { Id = "player-behaviors"
            RegistryName = "playerBehaviorClasses"
            Dependencies = [ "core-behaviors" ]
            FileName = "player-behaviors.ts" }
          { Id = "active-entity-behaviors"
            RegistryName = "activeEntityBehaviorClasses"
            Dependencies = [ "core-behaviors" ]
            FileName = "active-entity-behaviors.ts" }
          { Id = "location-behaviors"
            RegistryName = "locationBehaviorClasses"
            Dependencies = [ "core-behaviors" ]
            FileName = "location-behaviors.ts" }
          { Id = "forest-behaviors"
            RegistryName = "forestBehaviorClasses"
            Dependencies = [ "location-behaviors" ]
            FileName = "forest-behaviors.ts" }
          { Id = "village-behaviors"
            RegistryName = "villageBehaviorClasses"
            Dependencies = [ "location-behaviors" ]
            FileName = "village-behaviors.ts" }
          { Id = "thing-behaviors"
            RegistryName = "thingBehaviorClasses"
            Dependencies = [ "core-behaviors"; "active-entity-behaviors" ]
            FileName = "thing-behaviors.ts" }
          { Id = "anonymous-behaviors"
            RegistryName = "anonymousBehaviorClasses"
            Dependencies = [ "core-behaviors" ]
            FileName = "anonymous-behaviors.ts" } ]

    let private seedDirectory (serverRoot: string) = Path.Combine(serverRoot, "behaviors", "seed")

    let private tryFindServerRoot contentRoot =
        [ contentRoot
          Path.Combine(contentRoot, "src", "BrokenRealm.Server") ]
        |> List.map Path.GetFullPath
        |> List.tryFind (fun path -> File.Exists(Path.Combine(path, "Scripting", "game-api.d.ts")))

    let rec private findServerRootFromDirectory (directory: DirectoryInfo) depth =
        if depth > 12 then
            None
        else
            match tryFindServerRoot directory.FullName with
            | Some serverRoot -> Some serverRoot
            | None ->
                if isNull directory.Parent then
                    None
                else
                    findServerRootFromDirectory directory.Parent (depth + 1)

    let tryResolveServerRoot () =
        findServerRootFromDirectory (DirectoryInfo(AppContext.BaseDirectory)) 0

    let private resolveServerRoot () =
        tryResolveServerRoot ()
        |> Option.defaultWith (fun () ->
            failwith "Could not find server root containing Scripting/game-api.d.ts.")

    let hashSource (text: string) =
        use algorithm = SHA256.Create()
        let bytes = Encoding.UTF8.GetBytes text
        Convert.ToHexStringLower(algorithm.ComputeHash(bytes))

    let loadSeedSource (serverRoot: string) (definition: SeedModuleDefinition) =
        let path = Path.Combine(seedDirectory serverRoot, definition.FileName)

        if not (File.Exists path) then
            failwith $"Missing behavior seed file: {path}"

        File.ReadAllText path

    let loadSeedModules (serverRoot: string) : SeedModule list =
        seedModuleDefinitions
        |> List.map (fun definition ->
            { Id = definition.Id
              RegistryName = definition.RegistryName
              Dependencies = definition.Dependencies
              Source = loadSeedSource serverRoot definition }
            : SeedModule)

    let seedManifest (serverRoot: string) =
        seedModuleDefinitions
        |> List.map (fun definition ->
            let source = loadSeedSource serverRoot definition

            { ModuleId = definition.Id
              Sha256 = hashSource source })

    let join (sources: string list) =
        System.String.Join(System.Environment.NewLine + System.Environment.NewLine, sources)

    let joinModules (modules: (string * string) list) =
        modules
        |> List.map (fun (moduleId, source) -> moduleMarkerPrefix + moduleId + System.Environment.NewLine + source)
        |> join

    let mutable private cachedSources: Map<string, string> option = None

    let private ensureSources () =
        match cachedSources with
        | Some sources -> sources
        | None ->
            let serverRoot = resolveServerRoot ()
            let sources =
                seedModuleDefinitions
                |> List.map (fun definition -> definition.Id, loadSeedSource serverRoot definition)
                |> Map.ofList

            cachedSources <- Some sources
            sources

    let core = ensureSources().["core-behaviors"]
    let player = ensureSources().["player-behaviors"]
    let activeEntity = ensureSources().["active-entity-behaviors"]
    let location = ensureSources().["location-behaviors"]
    let forest = ensureSources().["forest-behaviors"]
    let village = ensureSources().["village-behaviors"]
    let thing = ensureSources().["thing-behaviors"]
    let anonymous = ensureSources().["anonymous-behaviors"]
