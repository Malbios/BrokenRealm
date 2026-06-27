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
        let mutable gameState = ObjectDatabase.initialState

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
                        let result = Kernel.submitCommand culture request.text gameState
                        gameState <- result.State
                        result)

                let lines = result.Messages |> List.map (ResponseFormatting.localizeMessage result.State culture)
                Results.Json({ lines = lines } : CommandResponse)))
        |> ignore

        app.MapGet(
            "/admin/behaviors",
            Func<IResult>(fun () ->
                lock stateLock (fun () ->
                    Kernel.listAdminBehaviorModules gameState |> Results.Json)))
        |> ignore

        app.MapGet(
            "/admin/behaviors/{moduleId}",
            Func<string, IResult>(fun moduleId ->
                lock stateLock (fun () ->
                    match Kernel.tryGetBehaviorModule moduleId gameState with
                    | Some behaviorModule ->
                        let affectedModules, affectedObjects = Kernel.behaviorImpact moduleId gameState

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
                    match
                        Kernel.tryUpdateBehaviorModule
                            (ScriptCompiler.compile app.Environment.ContentRootPath)
                            Scripting.inspectBehaviorModule
                            moduleId
                            request.source
                            gameState
                    with
                    | Ok(Some updated) ->
                        gameState <- updated.State
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

        app.Run()
        0
