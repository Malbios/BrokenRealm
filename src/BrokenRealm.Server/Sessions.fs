namespace BrokenRealm.Server

open System
open System.Collections.Generic

module Sessions =
    [<Literal>]
    let CookieName = "brokenrealm_session"

    let ownedCharacters culture (accountId: AccountId) (state: GameState) =
        PlayerObjects.playersByAccount state accountId
        |> List.map (fun player ->
            { id = player.Id
              locationId = PlayerObjects.locationId player
              displayName = Localizer.objectName state culture player.Id })

    let toResponse culture (session: GameSession) (state: GameState) =
        let account = state.Accounts[session.AccountId]

        { accountId = session.AccountId
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

type SessionStore(?clock: unit -> DateTimeOffset) =
    let clock = defaultArg clock (fun () -> DateTimeOffset.UtcNow)
    let gate = obj()
    let sessions = Dictionary<SessionId, GameSession>()

    member _.TryGet(sessionId: SessionId) =
        lock gate (fun () ->
            match sessions.TryGetValue sessionId with
            | true, session -> Some session
            | false, _ -> None)

    member _.CreateAnonymousPrototypeSession() =
        lock gate (fun () ->
            let now = clock ()
            let sessionId = Guid.CreateVersion7().ToString("N")

            let session =
                { Id = sessionId
                  AccountId = GameSnapshots.PrototypeAccountId
                  SelectedCharacterId = GameSnapshots.PrototypeCharacterId
                  Authenticated = false
                  CreatedAt = now
                  LastSeenAt = now }

            sessions[sessionId] <- session
            session)

    member _.Touch(session: GameSession) =
        lock gate (fun () ->
            let updated = { session with LastSeenAt = clock () }
            sessions[session.Id] <- updated
            updated)

    member _.Logout(sessionId: SessionId) =
        lock gate (fun () -> sessions.Remove sessionId |> ignore)

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
                            LastSeenAt = clock () }

                    sessions[sessionId] <- updated
                    Ok updated)