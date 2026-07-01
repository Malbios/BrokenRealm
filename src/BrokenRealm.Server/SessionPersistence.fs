namespace BrokenRealm.Server

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.RegularExpressions

module SessionPersistence =
    let private sessionIdRegex = Regex(@"^[0-9a-f]{32}$", RegexOptions.CultureInvariant ||| RegexOptions.IgnoreCase)

    let private tryParseSessionId (raw: string) =
        if String.IsNullOrWhiteSpace raw then
            None
        elif sessionIdRegex.IsMatch raw then
            Some(raw.Trim().ToLowerInvariant())
        else
            None
    type PersistedSessionDto =
        { id: string
          accountId: string
          selectedCharacterId: string
          authenticated: bool
          createdAt: string
          lastSeenAt: string }

    let private jsonOptions =
        JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let private toDto (session: GameSession) =
        { id = session.Id
          accountId = session.AccountId
          selectedCharacterId = session.SelectedCharacterId
          authenticated = session.Authenticated
          createdAt = session.CreatedAt.ToString("O")
          lastSeenAt = session.LastSeenAt.ToString("O") }

    let private tryParseTimestamp (value: string) =
        match DateTimeOffset.TryParse(value) with
        | true, parsed -> Some parsed
        | false, _ -> None

    let private fromDto (dto: PersistedSessionDto) =
        match tryParseSessionId dto.id with
        | None -> None
        | Some sessionId ->
            match tryParseTimestamp dto.createdAt, tryParseTimestamp dto.lastSeenAt with
            | Some createdAt, Some lastSeenAt ->
                Some
                    { Id = sessionId
                      AccountId = dto.accountId
                      SelectedCharacterId = dto.selectedCharacterId
                      Authenticated = dto.authenticated
                      CreatedAt = createdAt
                      LastSeenAt = lastSeenAt
                      PendingDisambiguation = None }
            | _ -> None

    let tryLoad (path: string) =
        if not (File.Exists path) then
            []
        else
            try
                let json = File.ReadAllText path

                JsonSerializer.Deserialize<PersistedSessionDto array>(json, jsonOptions)
                |> Option.ofObj
                |> Option.defaultValue Array.empty
                |> Array.choose fromDto
                |> Array.toList
            with _ ->
                []

    let write (path: string) (sessions: GameSession seq) =
        let directory = Path.GetDirectoryName path

        if not (String.IsNullOrWhiteSpace directory) && not (Directory.Exists directory) then
            Directory.CreateDirectory directory |> ignore

        let payload =
            sessions
            |> Seq.map toDto
            |> Seq.toArray

        let tempPath = path + ".tmp"
        File.WriteAllText(tempPath, JsonSerializer.Serialize(payload, jsonOptions))
        File.Move(tempPath, path, overwrite = true)

    let loadInto (path: string) (sessions: Dictionary<SessionId, GameSession>) =
        tryLoad path
        |> List.iter (fun session -> sessions[session.Id] <- session)