using Wiremock;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

var server = WireMockServer.Start(8888);

UrlDefinition[] urls =
[
    new("psn", HttpMethod.Get,
        "/psn/userProfile/v1/internal/users/psn-account-id/basicPresence",
        ["playing", "not_playing"]),
    new("steam", HttpMethod.Get,
        "/steam/ISteamUser/GetPlayerSummaries/v0002/?key=steam-api-key&steamids=steam-account-id",
        ["playing", "not_playing"]),
];

foreach (var definition in urls)
{
    foreach (var state in definition.States)
    {
        server.Given(Request.Create().WithPath($"/__admin/{definition.Name}/{state}").UsingPost())
            .RespondWith(
                Response.Create().WithStatusCode(200)
                    .WithCallback(_ =>
                    {
                        server.SetScenarioState(definition.Name, state);
                        return new ResponseMessage();
                    })
            );

        server
            .Given(Request.Create().WithPath(definition.Path).UsingMethod(definition.Method.ToString()))
            .InScenario(definition.Name)
            .WhenStateIs(state)
            .RespondWith(Response.Create()
                .WithStatusCode(definition.StatusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBodyFromFile($"mocks/{definition.Name}.{state}.json"));
    }
}

server.SetScenarioState("psn", "not_playing");
server.SetScenarioState("steam", "not_playing");

Console.ReadKey();
