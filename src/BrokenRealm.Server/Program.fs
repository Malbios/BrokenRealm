namespace BrokenRealm.Server

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.SignalR
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

module Program =
    [<EntryPoint>]
    let main args =
        let stateLock = obj()
        let clock () = DateTimeOffset.UtcNow
        let builder = WebApplication.CreateBuilder(args)
        builder.Services.AddSignalR() |> ignore
        let app = builder.Build()
        let contentRoot = app.Environment.ContentRootPath
        let snapshotPath = GameStoreBootstrap.resolveSnapshotPath contentRoot
        let gameStore = GameStoreBootstrap.createGameStore contentRoot snapshotPath
        let sessionStore = SessionStore()
        let connectionRegistry = ConnectionRegistry()

        let hubContext = app.Services.GetRequiredService<IHubContext<GameHub>>()
        let startupSnapshot = gameStore.GetSnapshot()

        app.Logger.LogInformation(
            "BrokenRealm ready. Snapshot={SnapshotPath} Format=v{FormatVersion} WorldRevision={WorldRevision} Objects={ObjectCount}",
            snapshotPath,
            startupSnapshot.FormatVersion,
            startupSnapshot.World.Revision,
            startupSnapshot.World.Objects.Count)

        app.Lifetime.ApplicationStopping.Register(fun () ->
            lock stateLock (fun () ->
                gameStore.Flush()
                app.Logger.LogInformation("BrokenRealm snapshot flushed on shutdown. Snapshot={SnapshotPath}", snapshotPath)))
        |> ignore

        let sessionCookieOptions =
            let options = CookieOptions()
            options.HttpOnly <- true
            options.SameSite <- SameSiteMode.Lax
            options.Path <- "/"
            options

        let appendSessionCookieIfNeeded (ctx: HttpContext) sessionId =
            if not (ctx.Request.Headers.ContainsKey Sessions.HeaderName) then
                ctx.Response.Cookies.Append(Sessions.CookieName, sessionId, sessionCookieOptions)

        let resolveSession (ctx: HttpContext) =
            let usesTabHeader = ctx.Request.Headers.ContainsKey Sessions.HeaderName

            let session =
                match Sessions.tryReadSessionId ctx with
                | Some sessionId ->
                    match sessionStore.TryGet sessionId with
                    | Some existing -> sessionStore.Touch existing
                    | None -> sessionStore.GetOrCreate(sessionId = sessionId)
                | None -> sessionStore.GetOrCreate()

            if not usesTabHeader then
                appendSessionCookieIfNeeded ctx session.Id

            session

        let commit stored state =
            match gameStore.TryCommit(stored.WorldRevision, stored.CharacterRevisions, state) with
            | Ok committed -> committed
            | Error _ -> failwith "The process-local game state changed while its exclusive lock was held."

        let tryCommitLimbo (characterId: CharacterId) =
            let stored = gameStore.Read()

            match Limbo.enterLimbo stored.State characterId with
            | Ok updated ->
                commit stored updated |> ignore
            | Error _ -> ()

        let enterLimboIfDisconnected (characterId: CharacterId) =
            if not (connectionRegistry.IsCharacterConnected characterId) then
                tryCommitLimbo characterId

        RoomBroadcast.setConnectionFilter connectionRegistry.IsCharacterConnected

        GameHubServices.register
            { SessionStore = sessionStore
              Connections = connectionRegistry
              EnterLimboIfDisconnected = fun characterId ->
                  lock stateLock (fun () -> enterLimboIfDisconnected characterId) }

        let trySourceRevision moduleId =
            gameStore.GetSnapshot().World.BehaviorModules
            |> Map.tryFind moduleId
            |> Option.map _.SourceRevision

        let conflict moduleId expected current =
            Results.Conflict(
                { moduleId = moduleId
                  expectedSourceRevision = expected
                  currentSourceRevision = current
                  message = "The behavior module changed after it was loaded. Reload it before saving again." }
                : BehaviorModuleConflictResponse)

        let serverRoot = ScriptCompiler.tryFindServerRoot contentRoot

        let provenanceLabel provenance =
            match provenance with
            | SeedSynced -> "seedSynced"
            | AdminEdited -> "adminEdited"

        let seedDriftResponse snapshotModule =
            match serverRoot with
            | Some root ->
                let drift = BehaviorGraph.computeSeedDrift root snapshotModule

                { seedHashChanged = drift.SeedHashChanged
                  syncedSeedHash = drift.SyncedSeedHash
                  currentSeedHash = drift.CurrentSeedHash }
            | None ->
                { seedHashChanged = false
                  syncedSeedHash = snapshotModule.SyncedSeedHash
                  currentSeedHash = "" }

        let graphWarningsForModule (gameSnapshot: GameSnapshot) (gameState: GameState) moduleId =
            let graphReferences = BehaviorGraph.collectBehaviorGraphReferences gameSnapshot

            graphReferences
            |> Set.filter (fun (referencedModuleId, _) -> referencedModuleId = moduleId)
            |> Set.toList
            |> List.choose (fun (referencedModuleId, className) ->
                match gameState.BehaviorModules |> Map.tryFind referencedModuleId with
                | Some moduleDefinition when moduleDefinition.Classes.ContainsKey className -> None
                | _ -> Some $"Missing class '{className}' in module '{referencedModuleId}'.")

        let staticRoot = Path.Combine(contentRoot, "wwwroot")
        app.UseDefaultFiles(DefaultFilesOptions(FileProvider = new PhysicalFileProvider(staticRoot))) |> ignore
        app.UseStaticFiles(StaticFileOptions(FileProvider = new PhysicalFileProvider(staticRoot))) |> ignore

        let sessionCulture (ctx: HttpContext) =
            Cultures.parse (ctx.Request.Query["culture"].ToString())

        let authResponse (culture: Culture) (gameSession: GameSession) (state: GameState) =
            let account = state.Accounts[gameSession.AccountId]

            { sessionId = gameSession.Id
              accountId = gameSession.AccountId
              authenticated = gameSession.Authenticated
              displayName = account.DisplayName
              selectedCharacterId = gameSession.SelectedCharacterId
              characters = Sessions.ownedCharacters culture gameSession.AccountId state }
            : AuthResponse

        app.MapGet(
            "/game/session",
            Func<HttpContext, IResult>(fun ctx ->
                lock stateLock (fun () ->
                    let culture = sessionCulture ctx
                    let session = resolveSession ctx
                    let state = gameStore.Read().State
                    Sessions.toResponse culture session state |> Results.Json)))
        |> ignore

        app.MapPost(
            "/game/auth/login",
            Func<HttpContext, LoginRequest, IResult>(fun ctx request ->
                lock stateLock (fun () ->
                    let culture = sessionCulture ctx
                    let session = resolveSession ctx
                    let state = gameStore.Read().State

                    match sessionStore.Login(session.Id, request.accountId, request.password, state) with
                    | Error error -> Results.BadRequest({ lines = [ error ] } : CommandResponse)
                    | Ok updated ->
                        let stateAfterLogin = gameStore.Read().State
                        appendSessionCookieIfNeeded ctx updated.Id
                        Results.Json(authResponse culture updated stateAfterLogin))))
        |> ignore

        app.MapPost(
            "/game/auth/register",
            Func<HttpContext, RegisterRequest, IResult>(fun ctx request ->
                lock stateLock (fun () ->
                    let culture = sessionCulture ctx
                    let session = resolveSession ctx
                    let stored = gameStore.Read()

                    match Kernel.tryRegisterAccount request.accountId request.password request.displayName stored.State with
                    | Error error -> Results.BadRequest({ lines = [ error ] } : CommandResponse)
                    | Ok updatedState ->
                        let committed = commit stored updatedState

                        match sessionStore.BindRegisteredAccount(session.Id, request.accountId, committed.State) with
                        | Error error -> Results.BadRequest({ lines = [ error ] } : CommandResponse)
                        | Ok updated ->
                            let stateAfterRegister = gameStore.Read().State
                            appendSessionCookieIfNeeded ctx updated.Id
                            Results.Json(authResponse culture updated stateAfterRegister))))
        |> ignore

        app.MapPost(
            "/game/auth/logout",
            Func<HttpContext, IResult>(fun ctx ->
                lock stateLock (fun () ->
                    let session = resolveSession ctx
                    tryCommitLimbo session.SelectedCharacterId
                    sessionStore.Logout session.Id

                    if not (ctx.Request.Headers.ContainsKey Sessions.HeaderName) then
                        ctx.Response.Cookies.Delete(Sessions.CookieName, sessionCookieOptions)

                    Results.Json({ lines = [ "Logged out." ] } : CommandResponse))))
        |> ignore

        app.MapPost(
            "/game/session/character",
            Func<HttpContext, SelectCharacterRequest, IResult>(fun ctx request ->
                lock stateLock (fun () ->
                    let culture = sessionCulture ctx
                    let session = resolveSession ctx
                    let state = gameStore.Read().State

                    match sessionStore.SelectCharacter(session.Id, request.characterId, state) with
                    | Error error -> Results.BadRequest({ lines = [ error ] } : CommandResponse)
                    | Ok updated ->
                        appendSessionCookieIfNeeded ctx updated.Id
                        Results.Json(authResponse culture updated (gameStore.Read().State)))))
        |> ignore

        app.MapPost(
            "/game/session/enter",
            Func<HttpContext, IResult>(fun ctx ->
                let culture = sessionCulture ctx

                let result, roomDeliveries =
                    lock stateLock (fun () ->
                        let session = resolveSession ctx
                        let stored = gameStore.Read()

                        match Kernel.tryEnterPlayForCharacter session.SelectedCharacterId stored.State with
                        | Error error ->
                            let response = { lines = [ error ] } : CommandResponse
                            response, []
                        | Ok enterResult ->
                            let committed = commit stored enterResult.State
                            let finalResult = { enterResult with State = committed.State }

                            let deliveries =
                                RoomBroadcast.planRoomDelivery
                                    finalResult.State
                                    culture
                                    session.SelectedCharacterId
                                    enterResult.Messages

                            let lines =
                                RoomBroadcast.actorResponseLines finalResult.State culture enterResult.Messages

                            { lines = lines } : CommandResponse, deliveries)

                RoomPush.push hubContext roomDeliveries
                Results.Json(result)))
        |> ignore

        app.MapGet(
            "/game/map",
            Func<HttpContext, IResult>(fun ctx ->
                lock stateLock (fun () ->
                    let session = resolveSession ctx
                    let state = gameStore.Read().State

                    match RoomMap.toResponse state session.SelectedCharacterId with
                    | Some payload -> Results.Json(payload)
                    | None ->
                        Results.Json(
                            { region = "main"
                              minX = 0
                              maxX = 0
                              minY = 0
                              maxY = 0
                              currentRoomId = ""
                              cells = [] }
                            : GameMapResponse))))
        |> ignore

        app.MapPost(
            "/game/command",
            Func<HttpContext, GameCommandRequest, IResult>(fun ctx request ->
                let culture = Cultures.parse request.culture

                let result, roomDeliveries =
                    lock stateLock (fun () ->
                        let session = resolveSession ctx
                        let stored = gameStore.Read()
                        let result =
                            Kernel.submitCommandForCharacterWithPending
                                session.SelectedCharacterId
                                culture
                                request.text
                                stored.State
                                session.PendingDisambiguation

                        sessionStore.SetPendingDisambiguation(session.Id, result.PendingDisambiguation)

                        let committed = commit stored result.State
                        let finalResult = { result with State = committed.State }

                        let deliveries =
                            RoomBroadcast.planRoomDelivery
                                finalResult.State
                                culture
                                session.SelectedCharacterId
                                result.Messages

                        finalResult, deliveries)

                RoomPush.push hubContext roomDeliveries

                let lines = RoomBroadcast.actorResponseLines result.State culture result.Messages

                Results.Json({ lines = lines } : CommandResponse)))
        |> ignore

        app.MapHub<GameHub>("/game/hub") |> ignore

        let worldTickSeconds =
            match app.Configuration["WorldTickSeconds"] with
            | null
            | "" -> 30
            | value ->
                match System.Int32.TryParse value with
                | true, seconds when seconds >= 1 -> seconds
                | _ -> 30

        let mutable worldTickIndex = 0

        System.Threading.Tasks.Task.Run(
            System.Func<System.Threading.Tasks.Task>(fun () ->
                task {
                    while true do
                        do! System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(float worldTickSeconds))

                        lock stateLock (fun () ->
                            let stored = gameStore.Read()
                            worldTickIndex <- worldTickIndex + 1

                            match
                                Kernel.tickWorld
                                    stored.State
                                    worldTickIndex
                                    worldTickSeconds
                                    connectionRegistry.IsCharacterConnected
                            with
                            | Ok updated -> commit stored updated |> ignore
                            | Error _ -> ())
                }))
        |> ignore

        app.MapGet(
            "/admin/behaviors",
            Func<IResult>(fun () ->
                lock stateLock (fun () ->
                    let snapshot = gameStore.GetSnapshot()

                    Kernel.listAdminBehaviorModules (gameStore.Read().State) snapshot serverRoot
                    |> Results.Json)))
        |> ignore

        app.MapGet(
            "/admin/scripting/game-api.d.ts",
            Func<IResult>(fun () ->
                match ScriptCompiler.tryReadApiDeclarations app.Environment.ContentRootPath with
                | Some declarations -> Results.Text(declarations, "text/plain; charset=utf-8")
                | None -> Results.NotFound()))
        |> ignore

        app.MapGet(
            "/admin/behaviors/{moduleId}",
            Func<string, IResult>(fun moduleId ->
                lock stateLock (fun () ->
                    let stored = gameStore.Read()
                    let snapshot = gameStore.GetSnapshot()

                    match Kernel.tryGetBehaviorModule moduleId stored.State with
                    | Some behaviorModule ->
                        let snapshotModule = snapshot.World.BehaviorModules[moduleId]
                        let affectedModules, affectedObjects = Kernel.behaviorImpact moduleId stored.State

                        Results.Json(
                            { moduleId = behaviorModule.Id
                              sourceRevision = trySourceRevision moduleId |> Option.get
                              dependencies = behaviorModule.Dependencies
                              classes = behaviorModule.Classes |> Map.toList |> List.map fst
                              affectedModules = affectedModules
                              affectedObjects = affectedObjects
                              source = behaviorModule.Source
                              provenance = provenanceLabel snapshotModule.Provenance
                              seedDrift = seedDriftResponse snapshotModule
                              graphWarnings = graphWarningsForModule snapshot stored.State moduleId }
                            : BehaviorModuleResponse)
                    | None -> Results.NotFound())))
        |> ignore

        app.MapPut(
            "/admin/behaviors/{moduleId}",
            Func<string, BehaviorModuleUpdateRequest, IResult>(fun moduleId request ->
                lock stateLock (fun () ->
                    let stored = gameStore.Read()
                    match trySourceRevision moduleId with
                    | None -> Results.NotFound()
                    | Some currentRevision when request.expectedSourceRevision <> currentRevision ->
                        conflict moduleId request.expectedSourceRevision currentRevision
                    | Some _ ->
                        match
                            Kernel.tryUpdateBehaviorModule
                                (ScriptCompiler.compile app.Environment.ContentRootPath)
                                Scripting.inspectBehaviorModule
                                moduleId
                                request.source
                                stored.State
                        with
                        | Ok(Some updated) ->
                            let _ = commit stored updated.State
                            Results.Json(
                                { moduleId = moduleId
                                  sourceRevision = trySourceRevision moduleId |> Option.get
                                  source = request.source
                                  affectedModules = updated.AffectedModules
                                  affectedObjects = updated.AffectedObjects
                                  diagnostics = [] }
                                : BehaviorModuleUpdateResponse)
                        | Ok None -> Results.NotFound()
                        | Error diagnostics ->
                            Results.BadRequest({ diagnostics = diagnostics } : BehaviorModuleErrorResponse))))
        |> ignore

        app.MapPost(
            "/admin/behaviors/{moduleId}/merge-seed",
            Func<string, IResult>(fun moduleId ->
                lock stateLock (fun () ->
                    let stored = gameStore.Read()
                    let snapshot = gameStore.GetSnapshot()

                    match snapshot.World.BehaviorModules |> Map.tryFind moduleId with
                    | None -> Results.NotFound()
                    | Some snapshotModule ->
                        match snapshotModule.Provenance with
                        | SeedSynced ->
                            Results.BadRequest(
                                { lines = [ "Only admin-edited behavior modules can be merged from seed." ] }
                                : CommandResponse)
                        | AdminEdited ->
                            match serverRoot with
                            | None ->
                                Results.BadRequest(
                                    { lines = [ "Could not resolve server root for behavior seed files." ] }
                                    : CommandResponse)
                            | Some root ->
                                let seedSource =
                                    BehaviorSources.loadSeedModules root
                                    |> List.tryFind (fun seedModule -> seedModule.Id = moduleId)
                                    |> Option.map _.Source

                                match seedSource with
                                | None ->
                                    Results.NotFound()
                                | Some source ->
                                    match
                                        Kernel.tryUpdateBehaviorModule
                                            (ScriptCompiler.compile contentRoot)
                                            Scripting.inspectBehaviorModule
                                            moduleId
                                            source
                                            stored.State
                                    with
                                    | Ok(Some updated) ->
                                        let _ = commit stored updated.State

                                        Results.Json(
                                            { moduleId = moduleId
                                              sourceRevision = trySourceRevision moduleId |> Option.get
                                              source = source
                                              affectedModules = updated.AffectedModules
                                              affectedObjects = updated.AffectedObjects
                                              diagnostics = [] }
                                            : BehaviorModuleUpdateResponse)
                                    | Ok None -> Results.NotFound()
                                    | Error diagnostics ->
                                        Results.BadRequest({ diagnostics = diagnostics } : BehaviorModuleErrorResponse))))
        |> ignore

        app.MapPost(
            "/admin/behaviors/{moduleId}/validate",
            Func<string, BehaviorModuleUpdateRequest, IResult>(fun moduleId request ->
                lock stateLock (fun () ->
                    let state = gameStore.Read().State
                    match trySourceRevision moduleId with
                    | None -> Results.NotFound()
                    | Some currentRevision when request.expectedSourceRevision <> currentRevision ->
                        conflict moduleId request.expectedSourceRevision currentRevision
                    | Some currentRevision ->
                        match
                            Kernel.tryValidateBehaviorModule
                                (ScriptCompiler.compile app.Environment.ContentRootPath)
                                Scripting.inspectBehaviorModule
                                moduleId
                                request.source
                                state
                        with
                        | Ok(Some validated) ->
                            Results.Json(
                                { moduleId = moduleId
                                  sourceRevision = currentRevision
                                  source = request.source
                                  affectedModules = validated.AffectedModules
                                  affectedObjects = validated.AffectedObjects
                                  diagnostics = [] }
                                : BehaviorModuleUpdateResponse)
                        | Ok None -> Results.NotFound()
                        | Error diagnostics ->
                            Results.BadRequest({ diagnostics = diagnostics } : BehaviorModuleErrorResponse))))
        |> ignore

        app.MapPost(
            "/admin/snapshot/backup",
            Func<IResult>(fun () ->
                lock stateLock (fun () ->
                    match gameStore.CreateBackup clock with
                    | Error error -> Results.BadRequest({ lines = [ error ] } : CommandResponse)
                    | Ok fileName ->
                        let snapshot = gameStore.GetSnapshot()

                        Results.Json(
                            { fileName = fileName
                              formatVersion = snapshot.FormatVersion
                              worldRevision = snapshot.World.Revision }
                            : SnapshotBackupResponse))))
        |> ignore

        app.MapGet(
            "/admin/snapshots",
            Func<IResult>(fun () ->
                lock stateLock (fun () ->
                    let backups =
                        SnapshotBackup.list snapshotPath
                        |> List.map (fun backup ->
                            { fileName = backup.fileName
                              createdAt = backup.createdAt })

                    Results.Json({ backups = backups } : SnapshotBackupListResponse))))
        |> ignore

        app.MapPost(
            "/admin/snapshot/restore",
            Func<SnapshotRestoreRequest, IResult>(fun request ->
                lock stateLock (fun () ->
                    let _ =
                        match gameStore.CreateBackup clock with
                        | Ok _ -> ()
                        | Error _ -> ()

                    let backupFileName = request.fileName

                    match gameStore.TryRestore(contentRoot, backupFileName) with
                    | Error error -> Results.BadRequest({ lines = [ error ] } : CommandResponse)
                    | Ok snapshot ->
                        Results.Json(
                            { fileName = backupFileName
                              formatVersion = snapshot.FormatVersion
                              worldRevision = snapshot.World.Revision
                              objectCount = snapshot.World.Objects.Count }
                            : SnapshotRestoreResponse))))
        |> ignore

        app.Run()
        0
