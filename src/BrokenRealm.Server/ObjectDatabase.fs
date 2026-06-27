namespace BrokenRealm.Server

module ObjectDatabase =
    let private verb name patterns source compiledSource =
        { Name = name
          Patterns = patterns
          Source = source
          CompiledSource = compiledSource }

    let private lookVerb =
        verb
            "look"
            [ { Culture = En; Pattern = "look" }
              { Culture = En; Pattern = "l" }
              { Culture = De; Pattern = "schau" }
              { Culture = De; Pattern = "umsehen" }
              { Culture = De; Pattern = "sieh dich um" } ]
            ScriptSources.look
            ScriptSources.lookCompiled

    let private inventoryVerb =
        verb
            "inventory"
            [ { Culture = En; Pattern = "inventory" }
              { Culture = En; Pattern = "inv" }
              { Culture = De; Pattern = "inventar" }
              { Culture = De; Pattern = "inv" } ]
            ScriptSources.inventory
            ScriptSources.inventoryCompiled

    let private moveVerb =
        verb
            "move"
            [ { Culture = En; Pattern = "go {direction}" }
              { Culture = En; Pattern = "walk {direction}" }
              { Culture = De; Pattern = "gehe nach {direction}" }
              { Culture = De; Pattern = "geh nach {direction}" } ]
            ScriptSources.move
            ScriptSources.moveCompiled

    let initialState =
        let forestVerbs =
            [ lookVerb
              verb
                  "gather"
                  [ { Culture = En; Pattern = "gather {item}" }
                    { Culture = En; Pattern = "collect {item}" }
                    { Culture = De; Pattern = "sammle {item}" }
                    { Culture = De; Pattern = "{item} sammeln" } ]
                  ScriptSources.gather
                  ScriptSources.gatherCompiled
              inventoryVerb
              moveVerb ]
            |> List.map (fun verb -> verb.Name, verb)
            |> Map.ofList

        let villageVerbs =
            [ lookVerb; inventoryVerb; moveVerb ]
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
              References = Map.ofList [ "north", "village" ]
              Verbs = forestVerbs }

        let village =
            { Id = "village"
              Name = "village"
              DescriptionKey = Some "location.village.description"
              Tags = Set.ofList [ "village" ]
              Properties = Map.ofList [ "biome", "settlement" ]
              References = Map.ofList [ "south", "forest" ]
              Verbs = villageVerbs }

        { Player =
            { LocationId = forest.Id
              Inventory = Map.empty }
          ItemIds = Set.ofList [ "wood" ]
          Objects = Map.ofList [ forest.Id, forest; village.Id, village ] }
