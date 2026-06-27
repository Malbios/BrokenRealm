namespace BrokenRealm.Server

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting

module Program =
    [<EntryPoint>]
    let main args =
        let stateLock = obj()
        let gameStore = InMemoryGameStore(ObjectDatabase.initialState)

        let commit stored state =
            match gameStore.TryCommit(stored.WorldRevision, stored.CharacterRevision, state) with
            | Ok committed -> committed
            | Error _ -> failwith "The process-local game state changed while its exclusive lock was held."

        let builder = WebApplication.CreateBuilder(args)
        let app = builder.Build()

        let staticRoot = IO.Path.Combine(app.Environment.ContentRootPath, "wwwroot")
        app.UseDefaultFiles(DefaultFilesOptions(FileProvider = new PhysicalFileProvider(staticRoot))) |> ignore
        app.UseStaticFiles(StaticFileOptions(FileProvider = new PhysicalFileProvider(staticRoot))) |> ignore

        app.MapPost(
            "/game/command",
            Func<GameCommandRequest, IResult>(fun request ->
                let culture = Cultures.parse request.culture

                let result =
                    lock stateLock (fun () ->
                        let stored = gameStore.Read()
                        let result = Kernel.submitCommand culture request.text stored.State
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
