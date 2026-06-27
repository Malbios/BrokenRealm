namespace BrokenRealm.Server

open System.Collections.Generic
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
        lock gate (fun () -> connections.Remove connectionId |> ignore)

module GameHubServices =
    type Host =
        { SessionStore: SessionStore
          Connections: ConnectionRegistry }

    let mutable private host = None

    let register value = host <- Some value

    let current () =
        match host with
        | Some value -> value
        | None -> failwith "Game hub services are not initialized."

    let tryResolveSession (httpContext: HttpContext) =
        let services = current ()

        match httpContext.Request.Cookies.TryGetValue Sessions.CookieName with
        | true, sessionId ->
            services.SessionStore.TryGet sessionId
            |> Option.map (services.SessionStore.Touch)
        | false, _ -> None

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
        GameHubServices.current().Connections.Remove(this.Context.ConnectionId)
        Task.CompletedTask

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