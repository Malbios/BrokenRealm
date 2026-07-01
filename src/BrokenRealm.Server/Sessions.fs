namespace BrokenRealm.Server

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions

module Sessions =
    [<Literal>]
    let CookieName = "brokenrealm_session"

    [<Literal>]
    let HeaderName = "X-BrokenRealm-Session"

    let private sessionIdRegex = Regex(@"^[0-9a-f]{32}$", RegexOptions.CultureInvariant ||| RegexOptions.IgnoreCase)

    let tryParseSessionId (raw: string) =
        if String.IsNullOrWhiteSpace raw then
            None
        elif sessionIdRegex.IsMatch raw then
            Some(raw.Trim().ToLowerInvariant())
        else
            None

    let tryReadSessionId (httpContext: Microsoft.AspNetCore.Http.HttpContext) =
        match httpContext.Request.Headers.TryGetValue HeaderName with
        | true, values when values.Count > 0 ->
            values
            |> Seq.tryHead
            |> Option.bind (fun value -> tryParseSessionId(value.ToString()))
        | _ ->
            match httpContext.Request.Cookies.TryGetValue CookieName with
            | true, cookieId -> tryParseSessionId cookieId
            | false, _ -> None

    let tryReadHubSessionId (httpContext: Microsoft.AspNetCore.Http.HttpContext) =
        match httpContext.Request.Query.TryGetValue "access_token" with
        | true, values when values.Count > 0 ->
            values
            |> Seq.tryHead
            |> Option.bind (fun value -> tryParseSessionId(value.ToString()))
            |> Option.orElseWith (fun () -> tryReadSessionId httpContext)
        | _ -> tryReadSessionId httpContext

    let ownedCharacters culture (accountId: AccountId) (state: GameState) =
        PlayerObjects.playersByAccount state accountId
        |> List.map (fun player ->
            { id = player.Id
              displayName = Localizer.objectName state culture player.Id
              inPlay = not (PlayerObjects.isInLimbo player)
              locationId = PlayerObjects.tryLocationId player
              lastSafeLocationId = PlayerObjects.lastSafeLocationId player
              hunger = PlayerObjects.hunger player
              inventory = PlayerObjects.inventory state player.Id })

    let toResponse culture (session: GameSession) (state: GameState) =
        let account = state.Accounts[session.AccountId]

        { sessionId = session.Id
          accountId = session.AccountId
          authenticated = session.Authenticated
          displayName = account.DisplayName
          selectedCharacterId = session.SelectedCharacterId
          characters = ownedCharacters culture session.AccountId state }

    let validateCharacterSelection accountId characterId (state: GameState) =
        match PlayerObjects.tryGet state characterId with
        | None -> Error $"Unknown character id: {characterId}"
        | Some player when PlayerObjects.accountId player <> accountId ->
            Error $"Character {characterId} is not owned by account {accountId}."
        | Some _ -> Ok characterId

    let private defaultCharacterId accountId (state: GameState) =
        PlayerObjects.playersByAccount state accountId
        |> List.tryHead
        |> Option.map _.Id
        |> Option.defaultValue GameSnapshots.PrototypeCharacterId

type SessionStore(?clock: unit -> DateTimeOffset, ?persistPath: string) =
    let clock = defaultArg clock (fun () -> DateTimeOffset.UtcNow)
    let persistPath = persistPath |> Option.map Path.GetFullPath
    let gate = obj()
    let sessions = Dictionary<SessionId, GameSession>()

    let persist () =
        match persistPath with
        | None -> ()
        | Some path ->
            sessions.Values
            |> Seq.filter _.Authenticated
            |> SessionPersistence.write path

    do
        match persistPath with
        | None -> ()
        | Some path -> SessionPersistence.loadInto path sessions

    member _.Flush() =
        lock gate persist

    member _.TryGet(sessionId: SessionId) =
        lock gate (fun () ->
            match sessions.TryGetValue sessionId with
            | true, session -> Some session
            | false, _ -> None)

    member _.GetOrCreate(?sessionId: SessionId) =
        lock gate (fun () ->
            let now = clock ()

            let resolvedSessionId =
                match sessionId |> Option.bind Sessions.tryParseSessionId with
                | Some requested -> requested
                | None -> Guid.CreateVersion7().ToString("N")

            match sessions.TryGetValue resolvedSessionId with
            | true, existing -> existing
            | false, _ ->
                let session =
                    { Id = resolvedSessionId
                      AccountId = GameSnapshots.PrototypeAccountId
                      SelectedCharacterId = GameSnapshots.PrototypeCharacterId
                      Authenticated = false
                      CreatedAt = now
                      LastSeenAt = now
                      PendingDisambiguation = None }

                sessions[resolvedSessionId] <- session
                session)

    member _.Touch(session: GameSession) =
        lock gate (fun () ->
            let updated = { session with LastSeenAt = clock () }
            sessions[session.Id] <- updated
            updated)

    member _.Logout(sessionId: SessionId) =
        lock gate (fun () ->
            sessions.Remove sessionId |> ignore
            persist ())

    member _.Login(sessionId: SessionId, accountId: AccountId, password: string, state: GameState) =
        lock gate (fun () ->
            match state.Accounts |> Map.tryFind accountId with
            | None -> Error "Unknown account."
            | Some account ->
                match account.PasswordHash with
                | None -> Error "This account does not support password login."
                | Some hash when not (Auth.verifyPassword password hash) -> Error "Invalid password."
                | Some _ ->
                    match sessions.TryGetValue sessionId with
                    | false, _ -> Error "Session not found."
                    | true, session ->
                        let characters = PlayerObjects.playersByAccount state accountId

                        if List.isEmpty characters then
                            Error "Account has no playable characters."
                        else
                            let selectedCharacterId =
                                if characters |> List.exists (fun player -> player.Id = session.SelectedCharacterId) then
                                    session.SelectedCharacterId
                                else
                                    (List.head characters).Id

                            let updated =
                                { session with
                                    AccountId = accountId
                                    SelectedCharacterId = selectedCharacterId
                                    Authenticated = true
                                    LastSeenAt = clock () }

                            sessions[sessionId] <- updated
                            persist ()
                            Ok updated)

    member _.BindRegisteredAccount(sessionId: SessionId, accountId: AccountId, state: GameState) =
        lock gate (fun () ->
            match sessions.TryGetValue sessionId with
            | false, _ -> Error "Session not found."
            | true, session ->
                let characters = PlayerObjects.playersByAccount state accountId

                if List.isEmpty characters then
                    Error "Account has no playable characters."
                else
                    let updated =
                        { session with
                            AccountId = accountId
                            SelectedCharacterId = (List.head characters).Id
                            Authenticated = true
                            LastSeenAt = clock () }

                    sessions[sessionId] <- updated
                    persist ()
                    Ok updated)

    member _.SelectCharacter(sessionId: SessionId, characterId: CharacterId, state: GameState) =
        lock gate (fun () ->
            match sessions.TryGetValue sessionId with
            | false, _ -> Error "Session not found."
            | true, session ->
                match Sessions.validateCharacterSelection session.AccountId characterId state with
                | Error error -> Error error
                | Ok selectedCharacterId ->
                    let updated =
                        { session with
                            SelectedCharacterId = selectedCharacterId
                            LastSeenAt = clock ()
                            PendingDisambiguation = None }

                    sessions[sessionId] <- updated
                    persist ()
                    Ok updated)

    member _.SetPendingDisambiguation(sessionId: SessionId, pending: PendingDisambiguation option) =
        lock gate (fun () ->
            match sessions.TryGetValue sessionId with
            | false, _ -> ()
            | true, session ->
                sessions[sessionId] <-
                    { session with
                        PendingDisambiguation = pending
                        LastSeenAt = clock () })
