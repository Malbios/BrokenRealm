namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module DisambiguationTests =
    let private withWood state amount =
        CarriedItems.addInventory state GameSnapshots.PrototypeCharacterId "wood" amount

    let private inVillage state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "go north" state
        |> fun result -> result.State

    let private craftStool state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "craft stool at workbench" state

    let private twoStoolsInVillage state =
        let first = craftStool (inVillage (withWood state 2))
        withWood first.State 2 |> craftStool |> fun result -> result.State

    [<Fact>]
    let ``Matcher reports ambiguous push when multiple stools match`` () =
        let state = twoStoolsInVillage ObjectDatabase.initialState

        match CommandMatching.tryMatchForCharacter GameSnapshots.PrototypeCharacterId En "push stool south" state with
        | CommandMatching.Ambiguous _ -> Assert.True(true)
        | CommandMatching.Matched _ -> Assert.True(false, "Expected ambiguity instead of a direct match.")
        | CommandMatching.MatchedSequence _ -> Assert.True(false, "Expected ambiguity instead of a batch match.")
        | CommandMatching.NoMatch -> Assert.True(false, "Expected ambiguity instead of no match.")

    [<Fact>]
    let ``Ambiguous object prompts for a numbered choice`` () =
        let state = twoStoolsInVillage (ObjectDatabase.initialState)

        let prompted =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "push stool south" state

        let line =
            RoomBroadcast.actorResponseLines prompted.State En prompted.Messages
            |> List.exactlyOne

        Assert.Contains("Which one do you mean?", line)
        Assert.Contains("1)", line)
        Assert.Contains("2)", line)
        Assert.NotNull(prompted.PendingDisambiguation)

    [<Fact>]
    let ``Numbered reply completes an ambiguous push`` () =
        let prompted =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "push stool south" (twoStoolsInVillage ObjectDatabase.initialState)

        let selected =
            Kernel.submitCommandForCharacterWithPending
                GameSnapshots.PrototypeCharacterId
                En
                "1"
                prompted.State
                prompted.PendingDisambiguation

        Assert.Null(selected.PendingDisambiguation)
        Assert.Equal(1, Kernel.contentsOf selected.State "forest" |> List.filter (fun o -> o.Tags.Contains "stool") |> List.length)

    [<Fact>]
    let ``Indexed object qualifier completes push without a pending prompt`` () =
        let state = twoStoolsInVillage ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "push 1 stool south" state

        Assert.Null(result.PendingDisambiguation)
        Assert.Equal(1, Kernel.contentsOf result.State "forest" |> List.filter (fun o -> o.Tags.Contains "stool") |> List.length)
        Assert.Equal(1, Kernel.contentsOf result.State "village" |> List.filter (fun o -> o.Tags.Contains "stool") |> List.length)

    [<Fact>]
    let ``Dot-indexed object qualifier completes push without a pending prompt`` () =
        let state = twoStoolsInVillage ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "push 1.stool south" state

        Assert.Null(result.PendingDisambiguation)
        Assert.Equal(1, Kernel.contentsOf result.State "forest" |> List.filter (fun o -> o.Tags.Contains "stool") |> List.length)

    [<Fact>]
    let ``All object qualifier executes the command for every match`` () =
        let state = twoStoolsInVillage ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "push all stool south" state

        Assert.Null(result.PendingDisambiguation)
        Assert.Equal(2, Kernel.contentsOf result.State "forest" |> List.filter (fun o -> o.Tags.Contains "stool") |> List.length)
        Assert.Empty(Kernel.contentsOf result.State "village" |> List.filter (fun o -> o.Tags.Contains "stool"))