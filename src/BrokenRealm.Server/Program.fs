namespace BrokenRealm.Server

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting

module Program =
    [<EntryPoint>]
    let main args =
        let stateLock = obj()
        let builder = WebApplication.CreateBuilder(args)
        let app = builder.Build()
        let contentRoot = app.Environment.ContentRootPath
        let snapshotPath = GameStoreBootstrap.resolveSnapshotPath contentRoot
        let gameStore = GameStoreBootstrap.createGameStore contentRoot snapshotPath
        let sessionStore = SessionStore()

        let sessionCookieOptions =
            let options = CookieOptions()
            options.HttpOnly <- true
            options.SameSite <- SameSiteMode.Lax
            options.Path <- "/"
            options

        let resolveSession (ctx: HttpContext) =
            let session =
                match ctx.Request.Cookies.TryGetValue Sessions.CookieName with
                | true, sessionId ->
                    match sessionStore.TryGet sessionId with
                    | Some existing -> sessionStore.Touch existing
                    | None -> sessionStore.CreateAnonymousPrototypeSession()
                | false, _ -> sessionStore.CreateAnonymousPrototypeSession()

            ctx.Response.Cookies.Append(Sessions.CookieName, session.Id, sessionCookieOptions)
            session

        let commit stored state =
            match gameStore.TryCommit(stored.WorldRevision, stored.CharacterRevisions, state) with
            | Ok committed -> committed
            | Error _ -> failwith "The process-local game state changed while its exclusive lock was held."

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

        let staticRoot = Path.Combine(contentRoot, "wwwroot")
        app.UseDefaultFiles(DefaultFilesOptions(FileProvider = new PhysicalFileProvider(staticRoot))) |> ignore
        app.UseStaticFiles(StaticFileOptions(FileProvider = new PhysicalFileProvider(staticRoot))) |> ignore

        app.MapGet(
            "/game/session",
            Func<HttpContext, IResult>(fun ctx ->
                lock stateLock (fun () ->
                    let session = resolveSession ctx
                    let state = gameStore.Read().State
                    Sessions.toResponse session state |> Results.Json)))
        |> ignore

        app.MapPost(
            "/game/session/character",
            Func<HttpContext, SelectCharacterRequest, IResult>(fun ctx request ->
                lock stateLock (fun () ->
                    let session = resolveSession ctx
                    let state = gameStore.Read().State

                    match sessionStore.SelectCharacter(session.Id, request.characterId, state) with
                    | Error error -> Results.BadRequest({ lines = [ error ] } : CommandResponse)
                    | Ok updated ->
                        ctx.Response.Cookies.Append(Sessions.CookieName, updated.Id, sessionCookieOptions)

                        Results.Json(
                            { selectedCharacterId = updated.SelectedCharacterId
                              characters = Sessions.ownedCharacters updated.AccountId state }
                            : SelectCharacterResponse))))
        |> ignore

        app.MapPost(
            "/game/command",
            Func<HttpContext, GameCommandRequest, IResult>(fun ctx request ->
                let culture = Cultures.parse request.culture

                let result =
                    lock stateLock (fun () ->
                        let session = resolveSession ctx
                        let stored = gameStore.Read()
                        let result =
                            Kernel.submitCommandForCharacter session.SelectedCharacterId culture request.text stored.State
                        let committed = commit stored result.State
                        { result with State = committed.State })

                let lines = result.Messages |> List.map (ResponseFormatting.localizeMessage result.State culture)
                Results.Json({ lines = lines } : CommandResponse)))
        |> ignore

        app.MapGet(
            "/admin/behaviors",
            Func<IResult>(fun () ->
                lock stateLock (fun () ->
                    Kernel.listAdminBehaviorModules (gameStore.Read().State) |> Results.Json)))
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
                    let state = gameStore.Read().State
                    match Kernel.tryGetBehaviorModule moduleId state with
                    | Some behaviorModule ->
                        let affectedModules, affectedObjects = Kernel.behaviorImpact moduleId state

                        Results.Json(
                            { moduleId = behaviorModule.Id
                              sourceRevision = trySourceRevision moduleId |> Option.get
                              dependencies = behaviorModule.Dependencies
                              classes = behaviorModule.Classes |> Map.toList |> List.map fst
                              affectedModules = affectedModules
                              affectedObjects = affectedObjects
                              source = behaviorModule.Source }
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

        app.Run()
        0
