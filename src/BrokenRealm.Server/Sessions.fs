namespace BrokenRealm.Server

open System
open System.Collections.Generic

module Sessions =
    [<Literal>]
    let CookieName = "brokenrealm_session"

    let ownedCharacters (accountId: AccountId) (state: GameState) =
        state.Characters
        |> Map.toList
        |> List.choose (fun (_, character) ->
            if character.AccountId = accountId then
                Some
                    { id = character.Id
                      locationId = character.LocationId }
            else
                None)
        |> List.sortBy _.id

    let toResponse (session: GameSession) (state: GameState) =
        { accountId = session.AccountId
          selectedCharacterId = session.SelectedCharacterId
          characters = ownedCharacters session.AccountId state }

    let validateCharacterSelection accountId characterId (state: GameState) =
        match state.Characters |> Map.tryFind characterId with
        | None -> Error $"Unknown character id: {characterId}"
        | Some character when character.AccountId <> accountId ->
            Error $"Character {characterId} is not owned by account {accountId}."
        | Some _ -> Ok characterId

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
                  CreatedAt = now
                  LastSeenAt = now }

            sessions[sessionId] <- session
            session)

    member _.Touch(session: GameSession) =
        lock gate (fun () ->
            let updated = { session with LastSeenAt = clock () }
            sessions[session.Id] <- updated
            updated)

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