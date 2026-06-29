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
    type SmokeTestWebApplicationFactory(snapshotPath: string) =
        inherit WebApplicationFactory<BrokenRealmApplication>()

        override _.ConfigureWebHost(builder) =
            Environment.SetEnvironmentVariable("BROKENREALM_SNAPSHOT_PATH", snapshotPath)
            base.ConfigureWebHost(builder)

    let private createTempSnapshotDirectory () =
        let directory = Path.Combine(Path.GetTempPath(), "brokenrealm-http-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directory) |> ignore
        directory, Path.Combine(directory, "game-snapshot.json")

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
    let ``HTTP login enters play with a localized re-entry message`` () : Task =
        task {
            let directory, snapshotPath = createTempSnapshotDirectory()

            try
                use factory = new SmokeTestWebApplicationFactory(snapshotPath)

                use client =
                    factory.CreateClient(
                        new WebApplicationFactoryClientOptions(
                            HandleCookies = true,
                            AllowAutoRedirect = false))

                let! loginResponse =
                    client.PostAsJsonAsync(
                        "/game/auth/login?culture=en",
                        { accountId = GameSnapshots.PrototypeAccountId
                          password = "prototype" })

                Assert.True(loginResponse.IsSuccessStatusCode)

                let! sessionResponse = client.GetAsync("/game/session?culture=en")
                Assert.True(sessionResponse.IsSuccessStatusCode)

                let! session = sessionResponse.Content.ReadFromJsonAsync<AuthResponse>()
                Assert.NotNull(session)
                Assert.True(session.authenticated)

                let selected =
                    session.characters
                    |> List.find (fun character -> character.id = session.selectedCharacterId)
                Assert.False(selected.inPlay)

                let! enterResponse = client.PostAsync("/game/session/enter?culture=en", null)
                Assert.True(enterResponse.IsSuccessStatusCode)

                let! enterPayload = enterResponse.Content.ReadFromJsonAsync<CommandResponse>()
                Assert.NotNull(enterPayload)
                Assert.Contains(enterPayload.lines, fun line -> line = "You enter forest.")
            finally
                RoomBroadcast.setConnectionFilter (fun _ -> true)
                Environment.SetEnvironmentVariable("BROKENREALM_SNAPSHOT_PATH", null)

                if Directory.Exists directory then
                    Directory.Delete(directory, true)
        }

    [<Fact>]
    let ``HTTP session enter and look succeeds on a fresh snapshot bootstrap`` () : Task =
        task {
            let directory, snapshotPath = createTempSnapshotDirectory()

            try
                use factory = new SmokeTestWebApplicationFactory(snapshotPath)

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
                RoomBroadcast.setConnectionFilter (fun _ -> true)
                Environment.SetEnvironmentVariable("BROKENREALM_SNAPSHOT_PATH", null)

                if Directory.Exists directory then
                    Directory.Delete(directory, true)
        }