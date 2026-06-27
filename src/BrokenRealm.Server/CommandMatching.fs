namespace BrokenRealm.Server

open System

module CommandMatching =
    let private normalize (raw: string) =
        raw.Trim().ToLowerInvariant()

    let private tryItem culture value =
        Localizer.itemAliases culture |> Map.tryFind (normalize value)

    let private matchPattern culture rawInput pattern =
        let input = normalize rawInput
        let pattern = normalize pattern

        if pattern = "{item}" then
            tryItem culture input |> Option.map (fun itemId -> Map.ofList [ "item", itemId ])
        elif pattern.Contains("{item}") then
            let parts = pattern.Split("{item}", StringSplitOptions.None)
            let prefix = parts[0]
            let suffix = parts[1]

            if input.StartsWith(prefix) && input.EndsWith(suffix) && input.Length >= prefix.Length + suffix.Length then
                let itemText = input.Substring(prefix.Length, input.Length - prefix.Length - suffix.Length)
                tryItem culture itemText |> Option.map (fun itemId -> Map.ofList [ "item", itemId ])
            else
                None
        elif input = pattern then
            Some Map.empty
        else
            None

    let tryMatch culture rawInput (state: GameState) =
        let location = state.Objects[state.Player.LocationId]

        location.Verbs
        |> Map.toList
        |> List.tryPick (fun (_, verb) ->
            verb.Patterns
            |> List.filter (fun pattern -> pattern.Culture = culture)
            |> List.tryPick (fun pattern ->
                match matchPattern culture rawInput pattern.Pattern with
                | Some args ->
                    Some
                        { ObjectId = location.Id
                          Verb = verb
                          Args = args }
                | None -> None))
