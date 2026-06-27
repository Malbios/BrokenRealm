namespace BrokenRealm.Server

module ObjectDatabase =
    let private verb name patterns source compiledSource =
        { Name = name
          Patterns = patterns
          Source = source
          CompiledSource = compiledSource }

    let initialState =
        let forestVerbs =
            [ verb
                  "look"
                  [ { Culture = En; Pattern = "look" }
                    { Culture = En; Pattern = "l" }
                    { Culture = De; Pattern = "schau" }
                    { Culture = De; Pattern = "umsehen" }
                    { Culture = De; Pattern = "sieh dich um" } ]
                  ScriptSources.look
                  ScriptSources.lookCompiled
              verb
                  "gather"
                  [ { Culture = En; Pattern = "gather {item}" }
                    { Culture = En; Pattern = "collect {item}" }
                    { Culture = De; Pattern = "sammle {item}" }
                    { Culture = De; Pattern = "{item} sammeln" } ]
                  ScriptSources.gather
                  ScriptSources.gatherCompiled
              verb
                  "inventory"
                  [ { Culture = En; Pattern = "inventory" }
                    { Culture = En; Pattern = "inv" }
                    { Culture = De; Pattern = "inventar" }
                    { Culture = De; Pattern = "inv" } ]
                  ScriptSources.inventory
                  ScriptSources.inventoryCompiled ]
            |> List.map (fun verb -> verb.Name, verb)
            |> Map.ofList

        let forest =
            { Id = "forest"
              Name = "forest"
              DescriptionKey = Some "location.forest.description"
              Tags = Set.ofList [ "forest"; "wood" ]
              Properties =
                Map.ofList
                    [ "biome", "forest"
                      "resourceItem", "wood" ]
              Verbs = forestVerbs }

        { Player =
            { LocationId = forest.Id
              Inventory = Map.empty }
          ItemIds = Set.ofList [ "wood" ]
          Objects = Map.ofList [ forest.Id, forest ] }
