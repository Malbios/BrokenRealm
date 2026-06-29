namespace BrokenRealm.Server

open System
open System.Text.RegularExpressions

module CommandMatching =
    type ArgumentResolution =
        | Resolved of string
        | MultipleMatches of ObjectId list
        | AllTargets of ObjectId list
        | Unresolved

    type PatternMatchOutcome =
        | PatternMatched of Map<string, string>
        | PatternMatchedBatch of placeholder: string * targetIds: ObjectId list * partialArgs: Map<string, string>
        | PatternAmbiguous of placeholder: string * candidates: ObjectId list * partialArgs: Map<string, string>

    type AmbiguousCommandMatch =
        { ObjectId: ObjectId
          BehaviorModuleId: string
          BehaviorClassName: string
          MethodName: string
          CompiledSource: string
          ResolvedArgs: Map<string, string>
          Placeholder: string
          Candidates: ObjectId list }

    type CommandMatchResult =
        | NoMatch
        | Matched of MatchedBehaviorMethod
        | MatchedSequence of MatchedBehaviorMethod list
        | Ambiguous of AmbiguousCommandMatch

    type private TargetMatchAttempt =
        | TargetMatched of MatchedBehaviorMethod
        | TargetMatchedSequence of MatchedBehaviorMethod list
        | TargetAmbiguous of AmbiguousCommandMatch

    let private normalize (raw: string) =
        raw.Trim().ToLowerInvariant()

    let private placeholderRegex = Regex(@"\{[^}]+\}", RegexOptions.CultureInvariant)

    let private selectionRegex = Regex(@"^\d+$", RegexOptions.CultureInvariant)

    let tryParseSelection (raw: string) =
        let trimmed = raw.Trim()

        if selectionRegex.IsMatch trimmed then
            match Int32.TryParse trimmed with
            | true, value when value >= 1 -> Some value
            | _ -> None
        else
            None

    let private allPlayers (state: GameState) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter PlayerObjects.isPlayer

    let private objectsInRoom (state: GameState) (locationId: ObjectId) (actorId: ObjectId) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter (fun gameObject ->
            gameObject.LocationId = Some locationId
            && gameObject.Id <> actorId
            && not (CarriedItems.isCarriedStack gameObject))

    let private objectMatchesAlias culture (normalized: string) (gameObject: GameObject) =
        normalize gameObject.Id = normalized
        || (gameObject.Aliases
            |> Map.tryFind culture
            |> Option.defaultValue []
            |> List.exists (fun alias -> normalize alias = normalized))

    let private matchingRoomObjects culture (state: GameState) (locationId: ObjectId) (actorId: ObjectId) (normalized: string) =
        objectsInRoom state locationId actorId
        |> List.filter (objectMatchesAlias culture normalized)
        |> List.map _.Id
        |> List.distinct
        |> List.sort

    let private matchingTargetObjects culture (target: GameObject) (normalized: string) =
        if objectMatchesAlias culture normalized target then
            [ target.Id ]
        else
            []

    let private matchingDestinations culture (state: GameState) (normalized: string) =
        state.Objects
        |> Map.toList
        |> List.map snd
        |> List.filter (fun gameObject ->
            gameObject.LocationId.IsNone
            && not (PlayerObjects.isPlayer gameObject)
            && not (CarriedItems.isCarriedStack gameObject))
        |> List.filter (objectMatchesAlias culture normalized)
        |> List.map _.Id
        |> List.distinct
        |> List.sort

    let private matchingPlayers culture (state: GameState) (normalized: string) =
        allPlayers state
        |> List.filter (objectMatchesAlias culture normalized)
        |> List.map _.Id
        |> List.distinct
        |> List.sort

    let private resolveAliasMap (aliases: Map<string, string>) (normalized: string) =
        aliases
        |> Map.toList
        |> List.choose (fun (alias, value) -> if normalize alias = normalized then Some value else None)
        |> function
            | [ value ] -> Resolved value
            | _ -> Unresolved

    let private allKeywords culture =
        match culture with
        | De -> Set.ofList [ "all"; "alle" ]
        | _ -> Set.ofList [ "all" ]

    type private QualifiedReference =
        | PlainAlias of string
        | IndexedAlias of int * string
        | AllAlias of string

    let private parseQualifiedReference culture (normalized: string) =
        let allKeys = allKeywords culture

        if normalized.Contains '.' then
            let dotIndex = normalized.IndexOf('.')
            let prefix = normalized.Substring(0, dotIndex)
            let alias = normalized.Substring(dotIndex + 1).Trim()

            if not (String.IsNullOrWhiteSpace alias) then
                if allKeys.Contains prefix then
                    AllAlias alias
                else
                    match Int32.TryParse prefix with
                    | true, index when index >= 1 -> IndexedAlias(index, alias)
                    | _ -> PlainAlias normalized
            else
                PlainAlias normalized
        else
            let parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)

            if parts.Length >= 2 then
                let head = parts[0]
                let alias = String.Join(" ", parts[1..])

                if allKeys.Contains head then
                    AllAlias alias
                else
                    match Int32.TryParse head with
                    | true, index when index >= 1 -> IndexedAlias(index, alias)
                    | _ -> PlainAlias normalized
            else
                PlainAlias normalized

    let private resolveTargetMatches culture (state: GameState) locationId target name normalized =
        match name with
        | "object" ->
            if PlayerObjects.isPlayer target then
                matchingRoomObjects culture state locationId target.Id normalized
            else
                matchingTargetObjects culture target normalized
        | "destination" -> matchingDestinations culture state normalized
        | "player" -> matchingPlayers culture state normalized
        | _ -> []

    let private resolveQualifiedTarget culture (state: GameState) locationId target name (reference: QualifiedReference) =
        match reference with
        | PlainAlias alias ->
            match resolveTargetMatches culture state locationId target name alias with
            | [ objectId ] -> Resolved objectId
            | [] -> Unresolved
            | objectIds -> MultipleMatches objectIds
        | IndexedAlias(index, alias) ->
            match resolveTargetMatches culture state locationId target name alias with
            | objectIds when index >= 1 && index <= objectIds.Length -> Resolved objectIds[index - 1]
            | _ -> Unresolved
        | AllAlias alias ->
            match resolveTargetMatches culture state locationId target name alias with
            | [] -> Unresolved
            | objectIds -> AllTargets objectIds

    let private isFreeTextPlaceholder name =
        name = "label" || name = "text"

    let private isTargetPlaceholder name =
        name = "object" || name = "destination" || name = "player"

    let private captureTargetPhrase culture placeholderName nextPlaceholderName nextSegment (remainder: string) (defaultCapture: string) =
        if
            isTargetPlaceholder placeholderName
            && nextPlaceholderName = "direction"
            && nextSegment = " "
            && not (String.IsNullOrWhiteSpace remainder)
        then
            Localizer.directionAliases culture
            |> Map.toList
            |> List.sortByDescending (fun (alias, _) -> alias.Length)
            |> List.tryPick (fun (alias, _) ->
                let aliasNorm = normalize alias
                let remainderNorm = normalize remainder

                if remainderNorm.EndsWith(aliasNorm) && remainderNorm.Length > aliasNorm.Length then
                    let objectPart = remainder.Substring(0, remainder.Length - alias.Length).Trim()

                    if String.IsNullOrWhiteSpace objectPart then
                        None
                    else
                        Some objectPart
                else
                    None)
            |> Option.defaultValue defaultCapture
        else
            defaultCapture

    let private tryArgument culture (state: GameState) locationId target (name: string) (value: string) =
        if isFreeTextPlaceholder name then
            Resolved(value.Trim() : string)
        else
            let normalized = normalize value

            match name with
            | "amount" ->
                match Int32.TryParse value with
                | true, amount when amount >= 1 && amount <= 100 -> Resolved(string amount)
                | _ -> Unresolved
            | "item" -> resolveAliasMap (Localizer.itemAliases culture) normalized
            | "direction" -> resolveAliasMap (Localizer.directionAliases culture) normalized
            | "object" | "destination" | "player" ->
                resolveQualifiedTarget culture state locationId target name (parseQualifiedReference culture normalized)
            | "recipe" ->
                let aliases =
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

                resolveAliasMap aliases normalized
            | "structure" ->
                let aliases =
                    match culture with
                    | De -> Map.ofList [ "lichtung", "clearing" ]
                    | _ -> Map.ofList [ "clearing", "clearing" ]

                resolveAliasMap aliases normalized
            | _ -> Unresolved

    let private matchPattern culture (state: GameState) locationId target rawInput pattern =
        let input = normalize rawInput
        let normalizedPattern = normalize pattern
        let parts = placeholderRegex.Split normalizedPattern
        let placeholderNames =
            [ for placeholder in placeholderRegex.Matches normalizedPattern -> placeholder.Value.Trim('{', '}') ]

        if placeholderNames.IsEmpty then
            if input = normalizedPattern then
                PatternMatched Map.empty |> Some
            else
                None
        elif parts.Length <> placeholderNames.Length + 1 then
            None
        else
            let rec consume partIndex inputPos args ambiguous pendingAll =
                if partIndex >= parts.Length then
                    match pendingAll with
                    | Some(placeholder, targetIds) -> Some(PatternMatchedBatch(placeholder, targetIds, args))
                    | None ->
                        match ambiguous with
                        | Some(placeholder, candidates) -> Some(PatternAmbiguous(placeholder, candidates, args))
                        | None -> Some(PatternMatched args)
                else
                    let segment = parts[partIndex]

                    if not (input.Substring(inputPos).StartsWith segment) then
                        None
                    else
                        let afterSegment = inputPos + segment.Length

                        if partIndex = parts.Length - 1 then
                            if afterSegment = input.Length then
                                match pendingAll with
                                | Some(placeholder, targetIds) -> Some(PatternMatchedBatch(placeholder, targetIds, args))
                                | None ->
                                    match ambiguous with
                                    | Some(placeholder, candidates) -> Some(PatternAmbiguous(placeholder, candidates, args))
                                    | None -> Some(PatternMatched args)
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
                                let defaultCapture =
                                    if isFreeTextPlaceholder placeholderName then
                                        rawInput.Substring(afterSegment, endPos - afterSegment).Trim()
                                    else
                                        input.Substring(afterSegment, endPos - afterSegment).Trim()

                                let nextPlaceholderName =
                                    if partIndex + 1 < placeholderNames.Length then
                                        placeholderNames[partIndex + 1]
                                    else
                                        ""

                                let captured =
                                    if isFreeTextPlaceholder placeholderName then
                                        defaultCapture
                                    else
                                        captureTargetPhrase
                                            culture
                                            placeholderName
                                            nextPlaceholderName
                                            nextSegment
                                            (input.Substring(afterSegment))
                                            defaultCapture

                                match tryArgument culture state locationId target placeholderName captured with
                                | Unresolved -> None
                                | Resolved resolved ->
                                    let nextAmbiguous =
                                        match ambiguous with
                                        | Some _ -> ambiguous
                                        | None -> None

                                    let nextPos =
                                        if nextPlaceholderName = "direction" && nextSegment = " " then
                                            afterSegment + captured.Length
                                        else
                                            endPos

                                    consume (partIndex + 1) nextPos (Map.add placeholderName resolved args) nextAmbiguous pendingAll
                                | MultipleMatches candidates ->
                                    let nextAmbiguous =
                                        match ambiguous, pendingAll with
                                        | Some _, _ | _, Some _ -> ambiguous
                                        | None, None -> Some(placeholderName, candidates)
                                        | _ -> ambiguous

                                    let nextPos =
                                        if nextPlaceholderName = "direction" && nextSegment = " " then
                                            afterSegment + captured.Length
                                        else
                                            endPos

                                    consume (partIndex + 1) nextPos args nextAmbiguous pendingAll
                                | AllTargets targetIds when isTargetPlaceholder placeholderName ->
                                    match ambiguous, pendingAll with
                                    | Some _, _ | _, Some _ -> None
                                    | None, None ->
                                        let directionEndPos =
                                            if nextPlaceholderName = "direction" && nextSegment = " " then
                                                afterSegment + captured.Length
                                            else
                                                endPos

                                        consume (partIndex + 1) directionEndPos args ambiguous (Some(placeholderName, targetIds))
                                    | _ -> None

            consume 0 0 Map.empty None None

    let private tryMatchTarget culture (state: GameState) locationId (target: GameObject) rawInput =
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
                        | Some(PatternMatched args) ->
                            Some(
                                TargetMatched
                                    { ObjectId = target.Id
                                      BehaviorModuleId = behaviorModule.Id
                                      BehaviorClassName = behaviorClass.ClassName
                                      MethodName = command.MethodName
                                      CompiledSource = behaviorModule.CompiledSource
                                      Args = args })
                        | Some(PatternMatchedBatch(placeholder, targetIds, partialArgs)) ->
                            let matches =
                                if PlayerObjects.isPlayer target then
                                    targetIds
                                    |> List.map (fun selectedId ->
                                        { ObjectId = target.Id
                                          BehaviorModuleId = behaviorModule.Id
                                          BehaviorClassName = behaviorClass.ClassName
                                          MethodName = command.MethodName
                                          CompiledSource = behaviorModule.CompiledSource
                                          Args = Map.add placeholder selectedId partialArgs })
                                else
                                    targetIds
                                    |> List.filter (fun selectedId -> selectedId = target.Id)
                                    |> List.map (fun selectedId ->
                                        { ObjectId = target.Id
                                          BehaviorModuleId = behaviorModule.Id
                                          BehaviorClassName = behaviorClass.ClassName
                                          MethodName = command.MethodName
                                          CompiledSource = behaviorModule.CompiledSource
                                          Args = Map.add placeholder selectedId partialArgs })

                            match matches with
                            | [ single ] -> Some(TargetMatched single)
                            | multiple -> Some(TargetMatchedSequence multiple)
                            | [] -> None
                        | Some(PatternAmbiguous(placeholder, candidates, partialArgs)) ->
                            Some(
                                TargetAmbiguous
                                    { ObjectId = target.Id
                                      BehaviorModuleId = behaviorModule.Id
                                      BehaviorClassName = behaviorClass.ClassName
                                      MethodName = command.MethodName
                                      CompiledSource = behaviorModule.CompiledSource
                                      ResolvedArgs = partialArgs
                                      Placeholder = placeholder
                                      Candidates = candidates })
                        | None -> None))

    let tryMatchForCharacter characterId culture rawInput (state: GameState) =
        match PlayerObjects.tryGet state characterId with
        | None -> NoMatch
        | Some actor ->
            let locationId = PlayerObjects.locationId actor
            let location = state.Objects[locationId]
            let visibleContents =
                state.Objects
                |> Map.toList
                |> List.map snd
                |> List.filter (fun gameObject ->
                    gameObject.LocationId = Some locationId
                    && gameObject.Id <> actor.Id
                    && not (PlayerObjects.isPlayer gameObject))
                |> List.sortBy _.Id

            let attempts =
                actor :: location :: visibleContents
                |> List.choose (fun target -> tryMatchTarget culture state locationId target rawInput)

            let ambiguousMatch =
                attempts
                |> List.tryPick (function TargetAmbiguous ambiguous -> Some ambiguous | _ -> None)

            let expanded =
                attempts
                |> List.collect (function
                    | TargetMatched matched -> [ matched ]
                    | TargetMatchedSequence matches -> matches
                    | _ -> [])

            match ambiguousMatch, expanded with
            | Some ambiguous, [ single ] -> Matched single
            | Some ambiguous, _ -> Ambiguous ambiguous
            | None, [ single ] -> Matched single
            | None, multiple when not (List.isEmpty multiple) -> MatchedSequence multiple
            | None, [] -> NoMatch

    let tryMatch culture rawInput state =
        tryMatchForCharacter GameSnapshots.PrototypeCharacterId culture rawInput state