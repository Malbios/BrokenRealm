namespace BrokenRealm.Server

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions

module ScriptCompiler =
    let private tryFindServerRoot contentRoot =
        [ contentRoot
          Path.Combine(contentRoot, "src", "BrokenRealm.Server") ]
        |> List.map Path.GetFullPath
        |> List.tryFind (fun path -> File.Exists(Path.Combine(path, "Scripting", "game-api.d.ts")))

    let private tryFindClientRoot contentRoot serverRoot =
        [ Path.GetFullPath(Path.Combine(contentRoot, "..", "BrokenRealm.Client"))
          Path.GetFullPath(Path.Combine(serverRoot, "..", "BrokenRealm.Client"))
          Path.GetFullPath(Path.Combine(contentRoot, "src", "BrokenRealm.Client")) ]
        |> List.tryFind Directory.Exists

    let private compilerCommand clientRoot =
        let executable = if OperatingSystem.IsWindows() then "tsc.cmd" else "tsc"
        Path.Combine(clientRoot, "node_modules", ".bin", executable)

    let private normalizePath (path: string) =
        path.Replace("\\", "/")

    let private normalizeDiagnostic (tempRoot: string) (inputPath: string) (apiPath: string) (diagnostic: string) =
        let tempRoot = normalizePath tempRoot
        let inputPath = normalizePath inputPath
        let apiPath = normalizePath apiPath

        let normalized =
            diagnostic
                .Replace(inputPath, "verb.ts")
                .Replace(apiPath, "game-api.d.ts")
                .Replace(tempRoot + "/", "")

        let normalized = Regex.Replace(normalized, @"^.*[/\\]verb\.ts\(", "verb.ts(")
        let matched = Regex.Match(normalized, @"^verb\.ts\((\d+),(\d+)\):\s*(?:error\s+TS\d+:\s*)?(.*)$")

        if matched.Success then
            { message = matched.Groups[3].Value
              line = max 1 (Int32.Parse(matched.Groups[1].Value) - 1)
              column = Int32.Parse(matched.Groups[2].Value) }
        else
            { message = normalized
              line = 0
              column = 0 }

    let compile contentRoot source =
        match tryFindServerRoot contentRoot with
        | None -> Error [ { message = "Could not find src/BrokenRealm.Server/Scripting/game-api.d.ts."; line = 0; column = 0 } ]
        | Some serverRoot ->
            match tryFindClientRoot contentRoot serverRoot with
            | None -> Error [ { message = "Could not find src/BrokenRealm.Client for the TypeScript compiler."; line = 0; column = 0 } ]
            | Some clientRoot ->
                let tempRoot = Path.Combine(Path.GetTempPath(), "BrokenRealm", "verb-compile-" + Guid.NewGuid().ToString("N"))
                let inputPath = Path.Combine(tempRoot, "verb.ts")
                let apiPath = Path.GetFullPath(Path.Combine(serverRoot, "Scripting", "game-api.d.ts"))
                Directory.CreateDirectory(tempRoot) |> ignore

                let result =
                    try
                        let referencePath = apiPath.Replace("\\", "/")
                        File.WriteAllText(inputPath, "/// <reference path=\"" + referencePath + "\" />" + Environment.NewLine + source)

                        let startInfo = ProcessStartInfo()
                        startInfo.FileName <- compilerCommand clientRoot
                        startInfo.WorkingDirectory <- clientRoot
                        startInfo.RedirectStandardOutput <- true
                        startInfo.RedirectStandardError <- true
                        startInfo.UseShellExecute <- false

                        [ "--ignoreConfig"
                          "--pretty"
                          "false"
                          "--ignoreDeprecations"
                          "6.0"
                          "--target"
                          "ES2022"
                          "--module"
                          "none"
                          "--strict"
                          "--skipLibCheck"
                          "--noEmitOnError"
                          "--outDir"
                          tempRoot
                          inputPath ]
                        |> List.iter (fun argument -> startInfo.ArgumentList.Add(argument))

                        use compilerProcess = Process.Start(startInfo)
                        let stdout = compilerProcess.StandardOutput.ReadToEnd()
                        let stderr = compilerProcess.StandardError.ReadToEnd()

                        if not (compilerProcess.WaitForExit(10000)) then
                            try
                                compilerProcess.Kill(true)
                            with _ ->
                                ()

                            Error [ { message = "TypeScript compilation timed out."; line = 0; column = 0 } ]
                        elif compilerProcess.ExitCode <> 0 then
                            let diagnostics =
                                (stdout + Environment.NewLine + stderr).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                                |> Array.toList
                                |> List.map (normalizeDiagnostic tempRoot inputPath apiPath)

                            Error(
                                if List.isEmpty diagnostics then
                                    [ { message = "TypeScript compilation failed."; line = 0; column = 0 } ]
                                else
                                    diagnostics)
                        else
                            let outputPath = Path.Combine(tempRoot, "verb.js")

                            if File.Exists(outputPath) then
                                Ok(File.ReadAllText(outputPath))
                            else
                                Error [ { message = "TypeScript compiler did not produce JavaScript output."; line = 0; column = 0 } ]
                    with ex ->
                        Error [ { message = ex.Message; line = 0; column = 0 } ]

                try
                    if Directory.Exists(tempRoot) then
                        Directory.Delete(tempRoot, true)
                with _ ->
                    ()

                result
