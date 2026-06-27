namespace BrokenRealm.Server

module ObjectDatabase =
    let private behaviorModule id registryName dependencies source compiledSource =
        let classes =
            match Scripting.inspectBehaviorModule registryName compiledSource with
            | Ok classes -> classes
            | Error diagnostic -> failwith diagnostic.message

        { Id = id
          RegistryName = registryName
          Dependencies = dependencies
          Source = source
          CompiledSource = compiledSource
          Classes = classes }

    let initialState =
        let coreCompiled = BehaviorSources.coreCompiled
        let locationCompiled = BehaviorSources.join [ coreCompiled; BehaviorSources.locationCompiled ]
        let forestCompiled = BehaviorSources.join [ locationCompiled; BehaviorSources.forestCompiled ]
        let villageCompiled = BehaviorSources.join [ locationCompiled; BehaviorSources.villageCompiled ]
        let thingCompiled = BehaviorSources.join [ coreCompiled; BehaviorSources.thingCompiled ]

        let behaviorModules =
            [ behaviorModule "core-behaviors" "coreBehaviorClasses" [] BehaviorSources.core coreCompiled
              behaviorModule
                  "location-behaviors"
                  "locationBehaviorClasses"
                  [ "core-behaviors" ]
                  BehaviorSources.location
                  locationCompiled
              behaviorModule
                  "forest-behaviors"
                  "forestBehaviorClasses"
                  [ "location-behaviors" ]
                  BehaviorSources.forest
                  forestCompiled
              behaviorModule
                  "village-behaviors"
                  "villageBehaviorClasses"
                  [ "location-behaviors" ]
                  BehaviorSources.village
                  villageCompiled
              behaviorModule
                  "thing-behaviors"
                  "thingBehaviorClasses"
                  [ "core-behaviors" ]
                  BehaviorSources.thing
                  thingCompiled
              behaviorModule
                  "anonymous-behaviors"
                  "anonymousBehaviorClasses"
                  [ "core-behaviors" ]
                  BehaviorSources.anonymous
                  (BehaviorSources.join [ coreCompiled; BehaviorSources.anonymousCompiled ]) ]
            |> List.map (fun behaviorModule -> behaviorModule.Id, behaviorModule)
            |> Map.ofList

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
              DisplayName = Some "Prototype account" }

        let prototypeCharacter =
            { Id = GameSnapshots.PrototypeCharacterId
              AccountId = prototypeAccount.Id
              LocationId = forest.Id
              Inventory = Map.empty }

        let prototypeScout =
            { Id = GameSnapshots.PrototypeScoutCharacterId
              AccountId = prototypeAccount.Id
              LocationId = village.Id
              Inventory = Map.empty }

        { ItemIds = Set.ofList [ "wood" ]
          BehaviorModules = behaviorModules
          Objects = Map.ofList [ forest.Id, forest; village.Id, village; fallenLog.Id, fallenLog ]
          Accounts = Map.ofList [ prototypeAccount.Id, prototypeAccount ]
          Characters =
            Map.ofList
                [ prototypeCharacter.Id, prototypeCharacter
                  prototypeScout.Id, prototypeScout ] }
