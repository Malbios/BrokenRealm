namespace BrokenRealm.Client

open Microsoft.AspNetCore.Components.WebAssembly.Hosting
open Radzen
open Blazored.LocalStorage

module Program =

    [<EntryPoint>]
    let Main args =
        
        let builder = WebAssemblyHostBuilder.CreateDefault(args)
        
        builder.RootComponents.Add<Main.MyApp>("#main")
        
        builder.Services
            .AddRadzenComponents()
            .AddBlazoredLocalStorage()
        |> ignore
        
        builder.Build().RunAsync() |> ignore
        
        0
