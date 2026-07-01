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
        Assert.Contains("help, h, ? — Show this command reference.", lines)

    [<Fact>]
    let ``Help command lists localized command reference in German`` () =
        let lines = helpLines De ObjectDatabase.initialState

        Assert.Contains("Verfügbare Befehle (spitze Klammern zeigen Platzhalter):", lines)
        Assert.Contains("schau, l — Schau dich im aktuellen Raum um.", lines)
        Assert.Contains("hilfe, h, ? — Zeige diese Befehlsübersicht.", lines)

    [<Fact>]
    let ``Question mark alias shows help`` () =
        let lines = helpLines En ObjectDatabase.initialState

        let result =
            Kernel.submitCommandForCharacter GameSnapshots.PrototypeCharacterId En "?" ObjectDatabase.initialState

        let questionLines =
            result.Messages
            |> RoomBroadcast.actorResponseLines result.State En

        Assert.Equal<string list>(lines, questionLines)