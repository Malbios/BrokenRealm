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
                  villageCompiled ]
            |> List.map (fun behaviorModule -> behaviorModule.Id, behaviorModule)
            |> Map.ofList

        let forest =
            { Id = "forest"
              Name = "forest"
              DescriptionKey = Some "location.forest.description"
              Tags = Set.ofList [ "forest"; "wood" ]
              Properties =
                Map.ofList
                    [ "biome", StringValue "forest"
                      "resourceItem", StringValue "wood"
                      "elevation", IntegerValue 120L
                      "dangerous", BooleanValue false
                      "landmarks", ListValue [ StringValue "old-oak"; StringValue "brook" ]
                      "climate", MapValue(Map.ofList [ "humidity", FloatValue 0.72 ])
                      "nearestSettlement", ObjectReferenceValue "village" ]
              References = Map.ofList [ "north", "village" ]
              BehaviorModuleId = "forest-behaviors"
              BehaviorClassName = "ForestBehavior" }

        let village =
            { Id = "village"
              Name = "village"
              DescriptionKey = Some "location.village.description"
              Tags = Set.ofList [ "village" ]
              Properties =
                Map.ofList
                    [ "biome", StringValue "settlement"
                      "population", IntegerValue 24L
                      "nearestForest", ObjectReferenceValue "forest" ]
              References = Map.ofList [ "south", "forest" ]
              BehaviorModuleId = "village-behaviors"
              BehaviorClassName = "VillageBehavior" }

        { Player = { LocationId = forest.Id; Inventory = Map.empty }
          ItemIds = Set.ofList [ "wood" ]
          BehaviorModules = behaviorModules
          Objects = Map.ofList [ forest.Id, forest; village.Id, village ] }
