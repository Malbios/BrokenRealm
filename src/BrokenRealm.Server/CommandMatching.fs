namespace BrokenRealm.Server

open System

module CommandMatching =
    let private normalize (raw: string) =
        raw.Trim().ToLowerInvariant()

    let private tryArgument culture name value =
        let aliases =
            match name with
            | "item" -> Localizer.itemAliases culture
            | "direction" -> Localizer.directionAliases culture
            | _ -> Map.empty

        aliases |> Map.tryFind (normalize value)

    let private matchPattern culture rawInput pattern =
        let input = normalize rawInput
        let pattern = normalize pattern

        let placeholderStart = pattern.IndexOf('{')
        let placeholderEnd = pattern.IndexOf('}', placeholderStart + 1)

        if placeholderStart >= 0 && placeholderEnd > placeholderStart then
            let placeholder = pattern.Substring(placeholderStart, placeholderEnd - placeholderStart + 1)
            let argumentName = placeholder.Trim('{', '}')
            let parts = pattern.Split(placeholder, StringSplitOptions.None)
            let prefix = parts[0]
            let suffix = parts[1]

            if input.StartsWith(prefix) && input.EndsWith(suffix) && input.Length >= prefix.Length + suffix.Length then
                let itemText = input.Substring(prefix.Length, input.Length - prefix.Length - suffix.Length)
                tryArgument culture argumentName itemText
                |> Option.map (fun value -> Map.ofList [ argumentName, value ])
            else
                None
        elif input = pattern then
            Some Map.empty
        else
            None

    let tryMatch culture rawInput (state: GameState) =
        let location = state.Objects[state.Player.LocationId]
        let behaviorModule = state.BehaviorModules[location.BehaviorModuleId]
        let behaviorClass = behaviorModule.Classes[location.BehaviorClassName]

        behaviorClass.Commands
        |> List.tryPick (fun command ->
            command.Patterns
            |> List.filter (fun pattern -> pattern.Culture = culture)
            |> List.tryPick (fun pattern ->
                match matchPattern culture rawInput pattern.Pattern with
                | Some args ->
                    Some
                        { ObjectId = location.Id
                          BehaviorModuleId = behaviorModule.Id
                          BehaviorClassName = behaviorClass.ClassName
                          MethodName = command.MethodName
                          CompiledSource = behaviorModule.CompiledSource
                          Args = args }
                | None -> None))
