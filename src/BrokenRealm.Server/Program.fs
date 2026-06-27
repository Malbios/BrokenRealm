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

                let lines = result.Messages |> List.map (ResponseFormatting.localizeMessage culture)
                Results.Json({ lines = lines } : CommandResponse)))
        |> ignore

        app.MapGet(
            "/admin/objects",
            Func<IResult>(fun () ->
                lock stateLock (fun () ->
                    Kernel.listAdminObjects gameState |> Results.Json)))
        |> ignore

        app.MapGet(
            "/admin/objects/{objectId}/verbs/{verbName}",
            Func<string, string, IResult>(fun objectId verbName ->
                lock stateLock (fun () ->
                    match Kernel.tryGetVerb objectId verbName gameState with
                    | Some verb -> Results.Json({ objectId = objectId; verb = verb.Name; source = verb.Source } : VerbResponse)
                    | None -> Results.NotFound())))
        |> ignore

        app.MapPut(
            "/admin/objects/{objectId}/verbs/{verbName}",
            Func<string, string, VerbUpdateRequest, IResult>(fun objectId verbName request ->
                lock stateLock (fun () ->
                    match Kernel.tryUpdateVerbSource (ScriptCompiler.compile app.Environment.ContentRootPath) objectId verbName request.source gameState with
                    | Ok(Some updated) ->
                        gameState <- updated
                        Results.Json({ objectId = objectId; verb = verbName; source = request.source; diagnostics = [] } : VerbUpdateResponse)
                    | Ok None -> Results.NotFound()
                    | Error diagnostics -> Results.BadRequest({ diagnostics = diagnostics } : VerbErrorResponse))))
        |> ignore

        app.Run()
        0
