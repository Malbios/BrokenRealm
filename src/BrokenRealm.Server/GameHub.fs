namespace BrokenRealm.Server

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.SignalR

type ConnectionRegistry() =
    let gate = obj()
    let connections = Dictionary<string, string>()

    member _.Set(connectionId: string, characterId: string) =
        lock gate (fun () ->
            match connections.TryGetValue connectionId with
            | true, previous when previous = characterId -> None
            | true, previous ->
                connections[connectionId] <- characterId
                Some previous
            | false, _ ->
                connections[connectionId] <- characterId
                None)

    member _.Remove(connectionId: string) =
        lock gate (fun () ->
            match connections.TryGetValue connectionId with
            | true, characterId ->
                connections.Remove connectionId |> ignore
                Some characterId
            | false, _ -> None)

    member _.IsCharacterConnected(characterId: string) =
        lock gate (fun () -> connections.Values |> Seq.exists ((=) characterId))

module GameHubServices =
    type Host =
        { SessionStore: SessionStore
          Connections: ConnectionRegistry
          EnterLimboIfDisconnected: CharacterId -> unit }

    let mutable private host = None

    let register value = host <- Some value

    let current () =
        match host with
        | Some value -> value
        | None -> failwith "Game hub services are not initialized."

    let tryResolveSession (httpContext: HttpContext) =
        let services = current ()

        match Sessions.tryReadSessionId httpContext with
        | Some sessionId ->
            match services.SessionStore.TryGet sessionId with
            | Some existing -> Some(services.SessionStore.Touch existing)
            | None -> Some(services.SessionStore.GetOrCreate(sessionId = sessionId))
        | None -> None

type GameHub() =
    inherit Hub()

    override this.OnConnectedAsync() =
        match this.Context.GetHttpContext() |> GameHubServices.tryResolveSession with
        | Some session ->
            let connectionId = this.Context.ConnectionId
            let groupId = RoomBroadcast.characterGroup session.SelectedCharacterId

            match GameHubServices.current().Connections.Set(connectionId, session.SelectedCharacterId) with
            | Some oldCharacterId ->
                this.Groups.RemoveFromGroupAsync(connectionId, RoomBroadcast.characterGroup oldCharacterId)
                    .ContinueWith(fun _ -> this.Groups.AddToGroupAsync(connectionId, groupId))
                    .Unwrap()
            | None ->
                this.Groups.AddToGroupAsync(connectionId, groupId)
        | None ->
            Task.CompletedTask

    override this.OnDisconnectedAsync(_exn) =
        let services = GameHubServices.current()
        let connectionId = this.Context.ConnectionId

        match services.Connections.Remove connectionId with
        | Some characterId ->
            Task.Run(fun () ->
                Thread.Sleep 300

                if not (services.Connections.IsCharacterConnected characterId) then
                    services.EnterLimboIfDisconnected characterId)
        | None -> Task.CompletedTask

    member this.SyncCharacter() =
        match this.Context.GetHttpContext() |> GameHubServices.tryResolveSession with
        | Some session ->
            let connectionId = this.Context.ConnectionId
            let groupId = RoomBroadcast.characterGroup session.SelectedCharacterId

            match GameHubServices.current().Connections.Set(connectionId, session.SelectedCharacterId) with
            | Some oldCharacterId ->
                this.Groups.RemoveFromGroupAsync(connectionId, RoomBroadcast.characterGroup oldCharacterId)
                    .ContinueWith(fun _ -> this.Groups.AddToGroupAsync(connectionId, groupId))
                    .Unwrap()
            | None ->
                this.Groups.AddToGroupAsync(connectionId, groupId)
        | None ->
            Task.CompletedTask

module RoomPush =
    let push (hubContext: IHubContext<GameHub>) (deliveries: (CharacterId * string) list) =
        deliveries
        |> List.iter (fun (characterId, line) ->
            hubContext.Clients.Group(RoomBroadcast.characterGroup characterId).SendAsync("roomLine", line)
            |> ignore)