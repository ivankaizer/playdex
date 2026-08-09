using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

var server = WireMockServer.Start(8888);

SetupAdminEndpoint("steam");
SetupAdminEndpoint("psn");
SetupProviderEndpoint("steam", "/ISteamUser/GetPlayerSummaries/v0002/");
SetupProviderEndpoint("psn", "/api/userProfile/v1/internal/users/psn-account-id/basicPresences");
SetupPsnOAuth();

server.SetScenarioState("steam", "not_playing");
server.SetScenarioState("psn", "not_playing");

Console.WriteLine("WireMock running on http://localhost:8888");
Console.WriteLine("  POST /__admin/steam/playing   POST /__admin/steam/not_playing");
Console.WriteLine("  POST /__admin/psn/playing     POST /__admin/psn/not_playing");
await Task.Delay(Timeout.Infinite);
return;

void SetupAdminEndpoint(string name)
{
    foreach (var state in new[] { "playing", "not_playing" })
    {
        server
            .Given(Request.Create().WithPath($"/__admin/{name}/{state}").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithCallback(_ =>
                {
                    server.SetScenarioState(name, state);
                    return new ResponseMessage();
                }));
    }
}

void SetupProviderEndpoint(string name, string path)
{
    foreach (var state in new[] { "playing", "not_playing" })
    {
        server
            .Given(Request.Create().WithPath(path).UsingGet())
            .InScenario(name)
            .WhenStateIs(state)
            .WillSetStateTo(state)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyFromFile($"mocks/{name}.{state}.json"));
    }
}

void SetupPsnOAuth()
{
    server
        .Given(Request.Create().WithPath("/api/authz/v3/oauth/authorize").UsingGet())
        .RespondWith(Response.Create()
            .WithStatusCode(302)
            .WithHeader(
                "Location",
                "com.playstation.PlayStationApp://redirect/?code=mock-psn-code&cid=mock-client-id"));

    server
        .Given(Request.Create().WithPath("/api/authz/v3/oauth/token").UsingPost())
        .RespondWith(Response.Create()
            .WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody("{\"access_token\":\"eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJhY2NvdW50X2lkIjoicHNuLWFjY291bnQtaWQifQ.fakesig\",\"expires_in\":3600}"));
}
