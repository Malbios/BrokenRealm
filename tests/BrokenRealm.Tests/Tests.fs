namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module KernelTests =
    [<Fact>]
    let ``Initial object IDs and references satisfy the durable ID contract`` () =
        let state = ObjectDatabase.initialState

        state.Objects
        |> Map.iter (fun objectId object ->
            Assert.True(ObjectIds.isValid objectId, $"Invalid object ID: {objectId}")
            Assert.Equal(objectId, object.Id)

            object.References
            |> Map.iter (fun _ destinationId ->
                Assert.True(state.Objects.ContainsKey destinationId, $"Unknown object reference: {destinationId}")))

    [<Fact>]
    let ``Generated object IDs are valid and unique`` () =
        let first = ObjectIds.create ()
        let second = ObjectIds.create ()

        Assert.True(ObjectIds.isValid first)
        Assert.True(ObjectIds.isValid second)
        Assert.StartsWith("obj_", first)
        Assert.NotEqual<string>(first, second)

    [<Theory>]
    [<InlineData("")>]
    [<InlineData("Forest")>]
    [<InlineData("1forest")>]
    [<InlineData("forest room")>]
    [<InlineData("forest/room")>]
    let ``Invalid object IDs are rejected`` value =
        Assert.False(ObjectIds.isValid value)

    [<Fact>]
    let ``Admin object catalog lists every editable verb`` () =
        let objects = Kernel.listAdminObjects ObjectDatabase.initialState

        Assert.Equal<string list>([ "forest"; "village" ], objects |> List.map _.objectId)
        let forest = objects |> List.find (fun object -> object.objectId = "forest")
        Assert.Equal<string list>([ "gather"; "inventory"; "look"; "move" ], forest.verbs)

    [<Fact>]
    let ``German movement command resolves a neutral direction`` () =
        let matched = CommandMatching.tryMatch De "gehe nach norden" ObjectDatabase.initialState

        match matched with
        | Some value ->
            Assert.Equal("move", value.Verb.Name)
            Assert.Equal("north", value.Args["direction"])
        | None -> Assert.True(false, "Expected command to match the movement verb.")

    [<Fact>]
    let ``Movement follows object references between locations`` () =
        let villageResult = Kernel.submitCommand En "go north" ObjectDatabase.initialState

        Assert.Equal("village", villageResult.State.Player.LocationId)
        Assert.Equal("You travel north.", villageResult.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage En)

        let forestResult = Kernel.submitCommand De "gehe nach süden" villageResult.State
        Assert.Equal("forest", forestResult.State.Player.LocationId)
        Assert.Equal("Du gehst nach Süden.", forestResult.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage De)

    [<Fact>]
    let ``Movement without an exit leaves the player in place`` () =
        let result = Kernel.submitCommand En "go south" ObjectDatabase.initialState

        Assert.Equal("forest", result.State.Player.LocationId)
        Assert.Equal("You cannot go that way.", result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage En)

    [<Fact>]
    let ``Kernel rejects movement to an unknown object`` () =
        let state = ObjectDatabase.initialState
        let forest = state.Objects["forest"]
        let brokenForest = { forest with References = Map.ofList [ "north", "missing" ] }
        let brokenState = { state with Objects = state.Objects |> Map.add forest.Id brokenForest }

        let result = Kernel.submitCommand En "go north" brokenState

        Assert.Equal("forest", result.State.Player.LocationId)
        let message = result.Messages |> List.exactlyOne
        Assert.Equal("script.error", message.Key)
        Assert.Equal("Unknown destination object id: missing", message.Args["error"])

    [<Fact>]
    let ``German gather command matches forest gather verb with neutral item id`` () =
        let state = ObjectDatabase.initialState

        let matched = CommandMatching.tryMatch De "holz sammeln" state

        match matched with
        | Some value ->
            Assert.Equal("forest", value.ObjectId)
            Assert.Equal("gather", value.Verb.Name)
            Assert.Equal("wood", value.Args["item"])
        | None -> Assert.True(false, "Expected command to match a verb.")

    [<Fact>]
    let ``Gather verb returns neutral effects applied by kernel`` () =
        let state = ObjectDatabase.initialState

        let result = Kernel.submitCommand En "gather wood" state

        Assert.Equal(2, result.State.Player.Inventory["wood"])
        Assert.Collection(
            result.Messages,
            fun message ->
                Assert.Equal("gather.wood.success", message.Key)
                Assert.Equal("2", message.Args["amount"])
                Assert.Equal("wood", message.Args["item"]))

    [<Fact>]
    let ``Localized inventory output uses localized item names`` () =
        let stateAfterGather = (Kernel.submitCommand De "sammle holz" ObjectDatabase.initialState).State

        let result = Kernel.submitCommand De "inventar" stateAfterGather
        let line = result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage De

        Assert.Equal("Inventar: 2 Holz.", line)

    [<Fact>]
    let ``Updating forest gather source changes later gather behavior`` () =
        let updatedSource = ScriptSources.gather.Replace("const amount = 2;", "const amount = 5;")
        let state =
            match Kernel.tryUpdateVerbSource (fun _ -> Ok(ScriptSources.gatherCompiled.Replace("const amount = 2;", "const amount = 5;"))) "forest" "gather" updatedSource ObjectDatabase.initialState with
            | Ok(Some state) -> state
            | Ok None -> failwith "Expected gather verb to update."
            | Error diagnostics -> failwith (String.concat "\n" diagnostics)

        let result = Kernel.submitCommand En "gather wood" state

        Assert.Equal(5, result.State.Player.Inventory["wood"])
        let line = result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage En
        Assert.Equal("You gather 5 wood.", line)

    [<Fact>]
    let ``Invalid verb source is rejected and previous source remains active`` () =
        let result = Kernel.tryUpdateVerbSource (fun _ -> Error [ "bad script" ]) "forest" "gather" "broken" ObjectDatabase.initialState

        match result with
        | Error diagnostics -> Assert.Equal("bad script", diagnostics |> List.exactlyOne)
        | Ok _ -> Assert.True(false, "Expected update to be rejected.")

        let gatherResult = Kernel.submitCommand En "gather wood" ObjectDatabase.initialState
        Assert.Equal(2, gatherResult.State.Player.Inventory["wood"])

    [<Fact>]
    let ``Unknown command returns localized unknown message key`` () =
        let result = Kernel.submitCommand En "dance" ObjectDatabase.initialState

        let line = result.Messages |> List.exactlyOne |> ResponseFormatting.localizeMessage En

        Assert.Equal("I do not understand that command.", line)

    [<Fact>]
    let ``Kernel rejects unknown inventory item effects without mutating state`` () =
        let source =
            """function execute(context) {
  return {
    effects: [
      { type: "addInventory", itemId: "stone", amount: 1 }
    ]
  };
}"""

        let state =
            match Kernel.tryUpdateVerbSource (fun _ -> Ok source) "forest" "gather" source ObjectDatabase.initialState with
            | Ok(Some state) -> state
            | Ok None -> failwith "Expected gather verb to update."
            | Error diagnostics -> failwith (String.concat "\n" diagnostics)

        let result = Kernel.submitCommand En "gather wood" state

        Assert.Empty(result.State.Player.Inventory)
        let message = result.Messages |> List.exactlyOne
        Assert.Equal("script.error", message.Key)
        Assert.Equal("Unknown item id: stone", message.Args["error"])

    [<Fact>]
    let ``Rejected effect batches never partially mutate state`` () =
        let messages =
            List.replicate Scripting.defaultLimits.MaxEffects "{ type: 'message', key: 'test' }"
            |> String.concat ","

        let source =
            $"function execute(context) {{ return {{ effects: [{{ type: 'addInventory', itemId: 'wood', amount: 1 }},{messages}] }}; }}"

        let state =
            match Kernel.tryUpdateVerbSource (fun _ -> Ok source) "forest" "gather" source ObjectDatabase.initialState with
            | Ok(Some state) -> state
            | Ok None -> failwith "Expected gather verb to update."
            | Error diagnostics -> failwith (String.concat "\n" diagnostics)

        let result = Kernel.submitCommand En "gather wood" state

        Assert.Empty(result.State.Player.Inventory)
        let message = result.Messages |> List.exactlyOne
        Assert.Equal("script.error", message.Key)
        Assert.Equal("Scripts may return at most 32 effects.", message.Args["error"])

module ScriptingTests =
    let private forest = ObjectDatabase.initialState.Objects["forest"]

    [<Fact>]
    let ``Script must return effects array`` () =
        let source = "function execute(context) { return {}; }"

        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("Script must return an object with an effects array.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to fail.")

    [<Fact>]
    let ``Script effects must be an array`` () =
        let source = "function execute(context) { return { effects: {} }; }"

        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("Script effects must be an array.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to fail.")

    [<Fact>]
    let ``Unknown script effect types are rejected`` () =
        let source = "function execute(context) { return { effects: [{ type: 'teleport' }] }; }"

        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("Unknown script effect type: teleport", error)
        | Ok _ -> Assert.True(false, "Expected script execution to fail.")

    [<Fact>]
    let ``Malformed addInventory effects are rejected`` () =
        let source = "function execute(context) { return { effects: [{ type: 'addInventory', itemId: 'wood', amount: 0 }] }; }"

        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("addInventory effects require itemId and an amount from 1 to 100.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to fail.")

    [<Fact>]
    let ``Runtime script exceptions are returned as errors`` () =
        let source = "function execute(context) { throw new Error('boom'); }"

        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("Script execution failed.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to fail.")

    [<Fact>]
    let ``Infinite scripts are stopped by the execution timeout`` () =
        let limits = { Scripting.defaultLimits with Timeout = System.TimeSpan.FromMilliseconds(25.0) }
        let source = "function execute(context) { while (true) {} }"

        let result = Scripting.executeVerbWithLimits limits forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("Script execution timed out.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to time out.")

    [<Fact>]
    let ``Scripts are stopped when they exceed the memory limit`` () =
        let limits =
            { Scripting.defaultLimits with
                MemoryBytes = 250_000L
                Timeout = System.TimeSpan.FromSeconds(2.0) }

        let source =
            "function execute(context) { const values = []; while (true) { values.push('x'.repeat(1000)); } }"

        let result = Scripting.executeVerbWithLimits limits forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("Script exceeded its memory limit.", error)
        | Ok _ -> Assert.True(false, "Expected script execution to exceed its memory limit.")

    [<Fact>]
    let ``Scripts cannot return too many effects`` () =
        let effects =
            List.replicate (Scripting.defaultLimits.MaxEffects + 1) "{ type: 'message', key: 'test' }"
            |> String.concat ","

        let source = $"function execute(context) {{ return {{ effects: [{effects}] }}; }}"
        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("Scripts may return at most 32 effects.", error)
        | Ok _ -> Assert.True(false, "Expected excessive effects to be rejected.")

    [<Fact>]
    let ``Scripts cannot return too many messages`` () =
        let effects =
            List.replicate (Scripting.defaultLimits.MaxMessages + 1) "{ type: 'message', key: 'test' }"
            |> String.concat ","

        let source = $"function execute(context) {{ return {{ effects: [{effects}] }}; }}"
        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("Scripts may return at most 16 message effects.", error)
        | Ok _ -> Assert.True(false, "Expected excessive messages to be rejected.")

    [<Fact>]
    let ``Message argument values have a bounded size`` () =
        let oversized = String.replicate (Scripting.defaultLimits.MaxMessageArgumentCharacters + 1) "x"
        let source = $"function execute(context) {{ return {{ effects: [{{ type: 'message', key: 'test', args: {{ value: '{oversized}' }} }}] }}; }}"
        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("Message argument values may contain at most 1024 characters.", error)
        | Ok _ -> Assert.True(false, "Expected oversized message arguments to be rejected.")

    [<Fact>]
    let ``Message argument counts are bounded`` () =
        let args =
            [ 1 .. Scripting.defaultLimits.MaxMessageArguments + 1 ]
            |> List.map (fun index -> $"value{index}: 'x'")
            |> String.concat ","

        let source = $"function execute(context) {{ return {{ effects: [{{ type: 'message', key: 'test', args: {{ {args} }} }}] }}; }}"
        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Error error -> Assert.Equal("Message effects may contain at most 16 arguments.", error)
        | Ok _ -> Assert.True(false, "Expected excessive message arguments to be rejected.")

    [<Fact>]
    let ``Verb source length is bounded before execution`` () =
        let limits = { Scripting.defaultLimits with MaxSourceCharacters = 10 }
        let result = Scripting.executeVerbWithLimits limits forest Map.empty Map.empty "function execute() {}"

        match result with
        | Error error -> Assert.Equal("Verb source may contain at most 10 characters.", error)
        | Ok _ -> Assert.True(false, "Expected oversized source to be rejected.")

    [<Fact>]
    let ``Script context includes object properties`` () =
        let source =
            """function execute(context) {
  return {
    effects: [
      {
        type: "message",
        key: "property.value",
        args: { value: context.this.properties.resourceItem }
      }
    ]
  };
}"""

        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Ok [ EmitMessage message ] ->
            Assert.Equal("property.value", message.Key)
            Assert.Equal("wood", message.Args["value"])
        | Ok _ -> Assert.True(false, "Expected one message effect.")
        | Error error -> Assert.True(false, error)

    [<Fact>]
    let ``Script context includes object references`` () =
        let source =
            """function execute(context) {
  return {
    effects: [
      { type: "movePlayer", destinationId: context.this.references.north }
    ]
  };
}"""

        let result = Scripting.executeVerb forest Map.empty Map.empty source

        match result with
        | Ok [ MovePlayer destinationId ] -> Assert.Equal("village", destinationId)
        | Ok _ -> Assert.True(false, "Expected one movement effect.")
        | Error error -> Assert.True(false, error)

module ScriptCompilerTests =
    [<Fact>]
    let ``Compiler rejects oversized source before invoking TypeScript`` () =
        let source = String.replicate (Scripting.defaultLimits.MaxSourceCharacters + 1) "x"
        let result = ScriptCompiler.compile "." source

        match result with
        | Error [ diagnostic ] ->
            Assert.Equal("Verb source may contain at most 64000 characters.", diagnostic.message)
            Assert.Equal(0, diagnostic.line)
            Assert.Equal(0, diagnostic.column)
        | Error _ -> Assert.True(false, "Expected one source-length diagnostic.")
        | Ok _ -> Assert.True(false, "Expected oversized source to be rejected.")
