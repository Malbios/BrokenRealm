namespace BrokenRealm.Server

open System

module CommandMatching =
    let private normalize (raw: string) =
        raw.Trim().ToLowerInvariant()

    let private tryArgument culture (target: GameObject) name value =
        let aliases =
            match name with
            | "item" -> Localizer.itemAliases culture
            | "direction" -> Localizer.directionAliases culture
            | "object" ->
                target.Aliases
                |> Map.tryFind culture
                |> Option.defaultValue []
                |> List.map (fun alias -> normalize alias, target.Id)
                |> Map.ofList
            | _ -> Map.empty

        aliases |> Map.tryFind (normalize value)

    let private matchPattern culture target rawInput pattern =
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
                tryArgument culture target argumentName itemText
                |> Option.map (fun value -> Map.ofList [ argumentName, value ])
            else
                None
        elif input = pattern then
            Some Map.empty
        else
            None

    let tryMatch culture rawInput (state: GameState) =
        let location = state.Objects[state.Player.LocationId]
        let visibleContents =
            state.Objects
            |> Map.toList
            |> List.map snd
            |> List.filter (fun object -> object.LocationId = Some location.Id)
            |> List.sortBy _.Id

        location :: visibleContents
        |> List.tryPick (fun target ->
            let behaviorModule = state.BehaviorModules[target.BehaviorModuleId]
            let behaviorClass = behaviorModule.Classes[target.BehaviorClassName]

            behaviorClass.Commands
            |> List.tryPick (fun command ->
                command.Patterns
                |> List.filter (fun pattern -> pattern.Culture = culture)
                |> List.tryPick (fun pattern ->
                    match matchPattern culture target rawInput pattern.Pattern with
                    | Some args ->
                        Some
                            { ObjectId = target.Id
                              BehaviorModuleId = behaviorModule.Id
                              BehaviorClassName = behaviorClass.ClassName
                              MethodName = command.MethodName
                              CompiledSource = behaviorModule.CompiledSource
                              Args = args }
                    | None -> None)))
