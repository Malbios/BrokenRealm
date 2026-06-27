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

    let private roomObjectAliases culture (state: GameState) (locationId: ObjectId) (actorId: ObjectId) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter (fun gameObject ->
            gameObject.LocationId = Some locationId
            && gameObject.Id <> actorId
            && not (CarriedItems.isCarriedStack gameObject))
        |> List.collect (fun gameObject ->
            (normalize gameObject.Id, gameObject.Id)
            :: (gameObject.Aliases
                |> Map.tryFind culture
                |> Option.defaultValue []
                |> List.map (fun alias -> normalize alias, gameObject.Id)))
        |> Map.ofList

    let private roomDestinationAliases culture (state: GameState) =
        state.Objects
        |> Map.toList
        |> List.collect (fun (_, gameObject) ->
            if
                gameObject.LocationId.IsNone
                && not (PlayerObjects.isPlayer gameObject)
                && not (CarriedItems.isCarriedStack gameObject)
            then
                (normalize gameObject.Id, gameObject.Id)
                :: (gameObject.Aliases
                    |> Map.tryFind culture
                    |> Option.defaultValue []
                    |> List.map (fun alias -> normalize alias, gameObject.Id))
            else
                [])
        |> Map.ofList

    let private isFreeTextPlaceholder name =
        name = "label" || name = "text"

    let private tryArgument culture (state: GameState) locationId target (name: string) (value: string) =
        if isFreeTextPlaceholder name then
            Some(value.Trim() : string)
        else
            let aliases =
                match name with
                | "amount" ->
                    match System.Int32.TryParse(value) with
                    | true, amount when amount >= 1 && amount <= 100 -> Map.ofList [ string amount, string amount ]
                    | _ -> Map.empty
                | "item" -> Localizer.itemAliases culture
                | "direction" -> Localizer.directionAliases culture
                | "object" ->
                    if PlayerObjects.isPlayer target then
                        roomObjectAliases culture state locationId target.Id
                    else
                        target.Aliases
                        |> Map.tryFind culture
                        |> Option.defaultValue []
                        |> List.map (fun alias -> normalize alias, target.Id)
                        |> Map.ofList
                | "destination" -> roomDestinationAliases culture state
                | "player" ->
                    allPlayers state
                    |> List.collect (fun player ->
                        (normalize player.Id, player.Id)
                        :: (player.Aliases
                            |> Map.tryFind culture
                            |> Option.defaultValue []
                            |> List.map (fun alias -> normalize alias, player.Id)))
                    |> Map.ofList
                | "recipe" ->
                    match culture with
                    | De ->
                        Map.ofList
                            [ "hocker", "stool"
                              "holzhocker", "stool"
                              "bank", "bench"
                              "holzbank", "bench" ]
                    | _ ->
                        Map.ofList
                            [ "stool", "stool"
                              "wooden stool", "stool"
                              "bench", "bench"
                              "wooden bench", "bench" ]
                | _ -> Map.empty

            aliases |> Map.tryFind (normalize value)

    let private matchPattern culture (state: GameState) locationId target rawInput pattern =
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
                |> List.filter (fun gameObject -> gameObject.LocationId = Some locationId && gameObject.Id <> actor.Id)
                |> List.sortBy _.Id

            actor :: location :: visibleContents
            |> List.tryPick (fun target ->
                match state.BehaviorModules |> Map.tryFind target.BehaviorModuleId with
                | None -> None
                | Some behaviorModule ->
                    match behaviorModule.Classes |> Map.tryFind target.BehaviorClassName with
                    | None -> None
                    | Some behaviorClass ->
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