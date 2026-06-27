namespace BrokenRealm.Server

open System
open System.Text.RegularExpressions

module CommandMatching =
    let private normalize (raw: string) =
        raw.Trim().ToLowerInvariant()

    let private placeholderRegex = Regex(@"\{[^}]+\}", RegexOptions.CultureInvariant)

    let private allPlayers (state: GameState) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter PlayerObjects.isPlayer

    let private isFreeTextPlaceholder name =
        name = "label" || name = "text"

    let private tryArgument culture state _locationId target (name: string) (value: string) =
        if isFreeTextPlaceholder name then
            Some(value.Trim() : string)
        else
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
                | "player" ->
                    allPlayers state
                    |> List.collect (fun player ->
                        (normalize player.Id, player.Id)
                        :: (player.Aliases
                            |> Map.tryFind culture
                            |> Option.defaultValue []
                            |> List.map (fun alias -> normalize alias, player.Id)))
                    |> Map.ofList
                | _ -> Map.empty

            aliases |> Map.tryFind (normalize value)

    let private matchPattern culture state locationId target rawInput pattern =
        let input = normalize rawInput
        let normalizedPattern = normalize pattern
        let parts = placeholderRegex.Split normalizedPattern
        let placeholderNames =
            [ for placeholder in placeholderRegex.Matches normalizedPattern -> placeholder.Value.Trim('{', '}') ]

        if placeholderNames.IsEmpty then
            if input = normalizedPattern then
                Some Map.empty
            else
                None
        elif parts.Length <> placeholderNames.Length + 1 then
            None
        else
            let rec consume partIndex inputPos args =
                if partIndex >= parts.Length then
                    None
                else
                    let segment = parts[partIndex]

                    if not (input.Substring(inputPos).StartsWith segment) then
                        None
                    else
                        let afterSegment = inputPos + segment.Length

                        if partIndex = parts.Length - 1 then
                            if afterSegment = input.Length then
                                Some args
                            else
                                None
                        else
                            let nextSegment = parts[partIndex + 1]
                            let placeholderName = placeholderNames[partIndex]

                            let endPos =
                                if String.IsNullOrEmpty nextSegment then
                                    input.Length
                                else
                                    input.IndexOf(nextSegment, afterSegment, StringComparison.Ordinal)

                            if endPos < 0 then
                                None
                            else
                                let captured =
                                    if isFreeTextPlaceholder placeholderName then
                                        rawInput.Substring(afterSegment, endPos - afterSegment).Trim()
                                    else
                                        input.Substring(afterSegment, endPos - afterSegment).Trim()

                                match tryArgument culture state locationId target placeholderName captured with
                                | None -> None
                                | Some resolved ->
                                    consume (partIndex + 1) endPos (Map.add placeholderName resolved args)

            consume 0 0 Map.empty

    let tryMatchForCharacter characterId culture rawInput (state: GameState) =
        match PlayerObjects.tryGet state characterId with
        | None -> None
        | Some actor ->
            let locationId = PlayerObjects.locationId actor
            let location = state.Objects[locationId]
            let visibleContents =
                state.Objects
                |> Map.toList
                |> List.map snd
                |> List.filter (fun object -> object.LocationId = Some locationId && object.Id <> actor.Id)
                |> List.sortBy _.Id

            actor :: location :: visibleContents
            |> List.tryPick (fun target ->
                let behaviorModule = state.BehaviorModules[target.BehaviorModuleId]
                let behaviorClass = behaviorModule.Classes[target.BehaviorClassName]

                behaviorClass.Commands
                |> List.tryPick (fun command ->
                    command.Patterns
                    |> List.filter (fun pattern -> pattern.Culture = culture)
                    |> List.tryPick (fun pattern ->
                        match matchPattern culture state locationId target rawInput pattern.Pattern with
                        | Some args ->
                            Some
                                { ObjectId = target.Id
                                  BehaviorModuleId = behaviorModule.Id
                                  BehaviorClassName = behaviorClass.ClassName
                                  MethodName = command.MethodName
                                  CompiledSource = behaviorModule.CompiledSource
                                  Args = args }
                        | None -> None)))

    let tryMatch culture rawInput state =
        tryMatchForCharacter GameSnapshots.PrototypeCharacterId culture rawInput state