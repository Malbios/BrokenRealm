namespace BrokenRealm.Tests

open System
open System.IO
open System.Net.Http
open System.Net.Http.Json
open System.Threading.Tasks
open BrokenRealm.Server
open Microsoft.AspNetCore.Mvc.Testing
open Xunit

module HttpSmokeTests =
    type LegacySnapshotWebApplicationFactory(snapshotPath: string) =
        inherit WebApplicationFactory<BrokenRealmApplication>()

        override _.ConfigureWebHost(builder) =
            Environment.SetEnvironmentVariable("BROKENREALM_SNAPSHOT_PATH", snapshotPath)
            base.ConfigureWebHost(builder)

    let private fixturePath =
        Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", "legacy-missing-player-behaviors.snapshot.json")

    let private copyFixtureToTemp () =
        let directory = Path.Combine(Path.GetTempPath(), "brokenrealm-http-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directory) |> ignore
        let snapshotPath = Path.Combine(directory, "game-snapshot.json")
        File.Copy(fixturePath, snapshotPath)
        directory, snapshotPath

    let private postCommand (client: HttpClient) (text: string) =
        task {
            let! response =
                client.PostAsJsonAsync(
                    "/game/command",
                    { text = text
                      culture = "en" })

            let! payload = response.Content.ReadFromJsonAsync<CommandResponse>()
            return response, payload
        }

    [<Fact>]
    let ``HTTP session enter and look succeeds against a legacy hydrated snapshot`` () : Task =
        task {
            Assert.True(File.Exists fixturePath, $"Missing fixture: {fixturePath}")

            let directory, snapshotPath = copyFixtureToTemp()

            try
                use factory = new LegacySnapshotWebApplicationFactory(snapshotPath)

                use client =
                    factory.CreateClient(
                        new WebApplicationFactoryClientOptions(
                            HandleCookies = true,
                            AllowAutoRedirect = false))

                let! sessionResponse = client.GetAsync("/game/session?culture=en")
                Assert.True(sessionResponse.IsSuccessStatusCode)

                let! enterResponse = client.PostAsync("/game/session/enter?culture=en", null)
                Assert.True(enterResponse.IsSuccessStatusCode)

                let! enterPayload = enterResponse.Content.ReadFromJsonAsync<CommandResponse>()
                Assert.NotNull(enterPayload)
                Assert.NotEmpty(enterPayload.lines)

                let! commandResponse, commandPayload = postCommand client "look"
                Assert.True(commandResponse.IsSuccessStatusCode)
                Assert.NotNull(commandPayload)
                Assert.NotEmpty(commandPayload.lines)
                Assert.Contains(commandPayload.lines, fun line -> line.Contains("forest"))
            finally
                Environment.SetEnvironmentVariable("BROKENREALM_SNAPSHOT_PATH", null)

                if Directory.Exists directory then
                    Directory.Delete(directory, true)
        }