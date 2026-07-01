namespace BrokenRealm.Tests

open BrokenRealm.Server
open Xunit

module HelpTests =
    let private helpCommand culture =
        match culture with
        | De -> "hilfe"
        | _ -> "help"

    let private helpLines culture state =
        Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId culture (helpCommand culture) state
        |> fun result ->
            result.Messages
            |> RoomBroadcast.actorResponseLines result.State culture

    [<Fact>]
    let ``Help command lists localized command reference in English`` () =
        let lines = helpLines En ObjectDatabase.initialState

        Assert.Contains("Available commands (angle brackets show placeholders):", lines)
        Assert.Contains("look, l — Look around the current room.", lines)
        Assert.Contains("help [command] — Explain one command, or show this full reference.", lines)

    [<Fact>]
    let ``Help command lists localized command reference in German`` () =
        let lines = helpLines De ObjectDatabase.initialState

        Assert.Contains("Verfügbare Befehle (spitze Klammern zeigen Platzhalter):", lines)
        Assert.Contains("schau, l — Schau dich im aktuellen Raum um.", lines)
        Assert.Contains("hilfe [befehl] — Erkläre einen Befehl oder zeige diese Übersicht.", lines)

    [<Fact>]
    let ``Question mark alias shows help`` () =
        let lines = helpLines En ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "?" ObjectDatabase.initialState

        let questionLines =
            result.Messages
            |> RoomBroadcast.actorResponseLines result.State En

        Assert.Equal<string list>(lines, questionLines)

    [<Fact>]
    let ``Help with a topic explains that command in English`` () =
        let lines =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "help gather" ObjectDatabase.initialState
            |> fun result -> RoomBroadcast.actorResponseLines result.State En result.Messages

        Assert.Contains("gather <item>, collect <item>", lines)
        Assert.Contains("Gather renewable resources in the forest, such as wood or berries. Yields recover over time.", lines)

    [<Fact>]
    let ``Help with a topic explains that command in German`` () =
        let lines =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId De "hilfe sammeln" ObjectDatabase.initialState
            |> fun result -> RoomBroadcast.actorResponseLines result.State De result.Messages

        Assert.Contains("sammle <item>, <item> sammeln", lines)
        Assert.Contains("Sammle erneuerbare Ressourcen im Wald, z. B. Holz oder Beeren. Vorräte erholen sich mit der Zeit.", lines)

    [<Fact>]
    let ``Help with an unknown topic reports missing help`` () =
        let lines =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "help juggle" ObjectDatabase.initialState
            |> fun result -> RoomBroadcast.actorResponseLines result.State En result.Messages

        Assert.Equal("No help topic for 'juggle'. Type help alone for the full command list.", lines |> List.exactlyOne)