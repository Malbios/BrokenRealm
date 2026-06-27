namespace BrokenRealm.Server

open System
open System.IO

module ObjectDatabase =
    let private resolveContentRoot () =
        match BehaviorSources.tryResolveServerRoot () with
        | Some serverRoot -> serverRoot
        | None -> failwith "Could not resolve content root containing Scripting/game-api.d.ts."

    let private buildWorldState (behaviorModules: Map<string, BehaviorModule>) =
        let forest =
            { Id = "forest"
              Name = "forest"
              NameKey = "object.forest.name"
              Aliases = Map.ofList [ En, [ "forest" ]; De, [ "wald" ] ]
              DescriptionKey = Some "location.forest.description"
              LocationId = None
              Tags = Set.ofList [ "forest"; "wood" ]
              Properties =
                Map.ofList
                    [ "biome", StringValue "forest"
                      "tickCount", IntegerValue 0L
                      "resourceItem", StringValue "wood"
                      "elevation", IntegerValue 120L
                      "dangerous", BooleanValue false
                      "landmarks", ListValue [ StringValue "old-oak"; StringValue "brook" ]
                      "climate", MapValue(Map.ofList [ "humidity", FloatValue 0.72 ])
                      "nearestSettlement", ObjectReferenceValue "village"
                      "trailToken",
                      AnonymousValue
                          { BehaviorModuleId = "anonymous-behaviors"
                            BehaviorClassName = "TrailTokenBehavior"
                            Properties = Map.ofList [ "label", StringValue "old forest trail" ] } ]
              References = Map.ofList [ "north", "village" ]
              BehaviorModuleId = "forest-behaviors"
              BehaviorClassName = "ForestBehavior" }

        let village =
            { Id = "village"
              Name = "village"
              NameKey = "object.village.name"
              Aliases = Map.ofList [ En, [ "village" ]; De, [ "dorf" ] ]
              DescriptionKey = Some "location.village.description"
              LocationId = None
              Tags = Set.ofList [ "village" ]
              Properties =
                Map.ofList
                    [ "biome", StringValue "settlement"
                      "comfort", IntegerValue 0L
                      "population", IntegerValue 24L
                      "nearestForest", ObjectReferenceValue "forest" ]
              References = Map.ofList [ "south", "forest" ]
              BehaviorModuleId = "village-behaviors"
              BehaviorClassName = "VillageBehavior" }

        let fallenLog =
            { Id = "fallen-log"
              Name = "fallen log"
              NameKey = "object.fallen-log.name"
              Aliases =
                Map.ofList
                    [ En, [ "fallen log"; "log" ]
                      De, [ "umgestürzter stamm"; "stamm"; "baumstamm" ] ]
              DescriptionKey = Some "object.fallen-log.description"
              LocationId = Some forest.Id
              Tags = Set.ofList [ "thing"; "wood" ]
              Properties = Map.ofList [ "material", StringValue "wood" ]
              References = Map.empty
              BehaviorModuleId = "thing-behaviors"
              BehaviorClassName = "ThingBehavior" }

        let prototypeAccount: AccountState =
            { Id = GameSnapshots.PrototypeAccountId
              DisplayName = Some "Prototype account"
              PasswordHash = Some(Auth.hashPassword "prototype") }

        let prototypePlayer =
            { PlayerObjects.create
                GameSnapshots.PrototypeCharacterId
                "prototype player"
                "object.prototype-player.name"
                prototypeAccount.Id
                forest.Id
              with
                  Aliases =
                      Map.ofList
                          [ En, [ "prototype player"; "player" ]
                            De, [ "prototyp-spieler"; "spieler" ] ] }

        let prototypeScout =
            { PlayerObjects.create
                GameSnapshots.PrototypeScoutCharacterId
                "prototype scout"
                "object.prototype-scout.name"
                prototypeAccount.Id
                village.Id
              with
                  Aliases =
                      Map.ofList
                          [ En, [ "prototype scout"; "scout" ]
                            De, [ "prototyp-späher"; "späher" ] ] }

        { ItemIds = Set.ofList [ "wood" ]
          BehaviorModules = behaviorModules
          Objects =
            Map.ofList
                [ forest.Id, forest
                  village.Id, village
                  fallenLog.Id, fallenLog
                  prototypePlayer.Id, prototypePlayer
                  prototypeScout.Id, prototypeScout ]
          Accounts = Map.ofList [ prototypeAccount.Id, prototypeAccount ] }

    let private seedBehaviorModules (contentRoot: string) =
        match ScriptCompiler.tryFindServerRoot contentRoot with
        | None -> failwith "Could not find server root containing Scripting/game-api.d.ts."
        | Some serverRoot ->
            BehaviorSources.loadSeedModules serverRoot
            |> List.map (fun seedModule ->
                { Id = seedModule.Id
                  RegistryName = seedModule.RegistryName
                  Dependencies = seedModule.Dependencies
                  Source = seedModule.Source
                  CompiledSource = ""
                  Classes = Map.empty })
            |> List.map (fun behaviorModule -> behaviorModule.Id, behaviorModule)
            |> Map.ofList

    let private compileSeedBehaviorModules contentRoot (seedModules: Map<string, BehaviorModule>) =
        Kernel.recompileBehaviorModules
            (ScriptCompiler.compile contentRoot)
            Scripting.inspectBehaviorModule
            seedModules
        |> Result.defaultWith (fun diagnostics ->
            let message =
                diagnostics
                |> List.map _.message
                |> String.concat Environment.NewLine

            failwith $"Failed to compile seed behavior modules: {message}")

    let mutable private cachedInitialState: GameState option = None

    let bootstrap (contentRoot: string) =
        let seedModules = seedBehaviorModules contentRoot
        let compiledModules = compileSeedBehaviorModules contentRoot seedModules
        let state = buildWorldState compiledModules
        cachedInitialState <- Some state
        state

    let initialState =
        match cachedInitialState with
        | Some state -> state
        | None -> bootstrap (resolveContentRoot())