namespace BrokenRealm.Server

module ObjectDatabase =
    let initialState =
        let coreBehavior =
            let classes =
                match Scripting.inspectBehaviorModule BehaviorSources.coreCompiled with
                | Ok classes -> classes
                | Error diagnostic -> failwith diagnostic.message

            { Id = "core-world"
              Source = BehaviorSources.core
              CompiledSource = BehaviorSources.coreCompiled
              Classes = classes }

        let forest =
            { Id = "forest"
              Name = "forest"
              DescriptionKey = Some "location.forest.description"
              Tags = Set.ofList [ "forest"; "wood" ]
              Properties = Map.ofList [ "biome", "forest"; "resourceItem", "wood" ]
              References = Map.ofList [ "north", "village" ]
              BehaviorModuleId = coreBehavior.Id
              BehaviorClassName = "ForestBehavior" }

        let village =
            { Id = "village"
              Name = "village"
              DescriptionKey = Some "location.village.description"
              Tags = Set.ofList [ "village" ]
              Properties = Map.ofList [ "biome", "settlement" ]
              References = Map.ofList [ "south", "forest" ]
              BehaviorModuleId = coreBehavior.Id
              BehaviorClassName = "VillageBehavior" }

        { Player = { LocationId = forest.Id; Inventory = Map.empty }
          ItemIds = Set.ofList [ "wood" ]
          BehaviorModules = Map.ofList [ coreBehavior.Id, coreBehavior ]
          Objects = Map.ofList [ forest.Id, forest; village.Id, village ] }
