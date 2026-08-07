using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace Api.ContractTests;

public sealed class DifferentialFixture : IAsyncDisposable
{
    private readonly HttpClient? _bunClient;
    private readonly HttpClient? _dotnetClient;
    private readonly NpgsqlDataSource? _bunDatabase;
    private readonly NpgsqlDataSource? _dotnetDatabase;
    private string? _bunToken;
    private string? _dotnetToken;

    public DifferentialFixture()
    {
        _bunClient = CreateClient("BUN_API_BASE_URL");
        _dotnetClient = CreateClient("DOTNET_API_BASE_URL");
        _bunDatabase = CreateDataSource("BUN_CONTRACT_DATABASE_URL");
        _dotnetDatabase = CreateDataSource("DOTNET_CONTRACT_DATABASE_URL");
    }

    public bool HasHttpTargets => _bunClient is not null && _dotnetClient is not null;
    public bool HasDatabaseTargets => _bunDatabase is not null && _dotnetDatabase is not null;

    public async Task<(NormalizedResponse Bun, NormalizedResponse Dotnet)> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken = default)
    {
        Ensure(HasHttpTargets, "BUN_API_BASE_URL and DOTNET_API_BASE_URL are required.");
        using var bunRequest = requestFactory();
        using var dotnetRequest = requestFactory();
        using var bunResponse = await _bunClient!.SendAsync(bunRequest, cancellationToken);
        using var dotnetResponse = await _dotnetClient!.SendAsync(dotnetRequest, cancellationToken);
        return (
            await NormalizedResponse.CreateAsync(bunResponse, cancellationToken),
            await NormalizedResponse.CreateAsync(dotnetResponse, cancellationToken));
    }

    public async Task<(NormalizedResponse Bun, NormalizedResponse Dotnet)> SendAuthenticatedAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken = default)
    {
        Ensure(HasHttpTargets, "BUN_API_BASE_URL and DOTNET_API_BASE_URL are required.");
        if (_bunToken is null || _dotnetToken is null)
            throw new InvalidOperationException(
                "Call EnsureTestUserAsync before authenticated requests.");
        using var bunRequest = requestFactory();
        using var dotnetRequest = requestFactory();
        bunRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bunToken);
        dotnetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _dotnetToken);
        using var bunResponse = await _bunClient!.SendAsync(bunRequest, cancellationToken);
        using var dotnetResponse = await _dotnetClient!.SendAsync(dotnetRequest, cancellationToken);
        return (
            await NormalizedResponse.CreateAsync(bunResponse, cancellationToken),
            await NormalizedResponse.CreateAsync(dotnetResponse, cancellationToken));
    }

    public async Task<bool> EnsureTestUserAsync(CancellationToken ct = default)
    {
        if (!HasDatabaseTargets || !HasHttpTargets)
            return false;

        _bunToken = await LoginAndGetToken(_bunClient!, "bun-contract@test.example", ct);
        _dotnetToken = await LoginAndGetToken(_dotnetClient!, "bun-contract@test.example", ct);

        return _bunToken is not null && _dotnetToken is not null;
    }

    private async Task<string?> LoginAndGetToken(HttpClient client, string email, CancellationToken ct)
    {
        try
        {
            var response = await client.PostAsJsonAsync("/api/login",
                new { email, password = "test" }, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var json = JsonNode.Parse(body);
            return json?["data"]?["accessToken"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<(JsonArray Bun, JsonArray Dotnet)> QueryAsync(
        string sql,
        CancellationToken cancellationToken = default)
    {
        Ensure(HasDatabaseTargets,
            "BUN_CONTRACT_DATABASE_URL and DOTNET_CONTRACT_DATABASE_URL are required.");
        return (
            await Query(_bunDatabase!, sql, cancellationToken),
            await Query(_dotnetDatabase!, sql, cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        _bunClient?.Dispose();
        _dotnetClient?.Dispose();
        if (_bunDatabase is not null) await _bunDatabase.DisposeAsync();
        if (_dotnetDatabase is not null) await _dotnetDatabase.DisposeAsync();
    }

    private static HttpClient? CreateClient(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? new HttpClient { BaseAddress = uri }
            : null;
    }

    private static NpgsqlDataSource? CreateDataSource(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : new NpgsqlDataSourceBuilder(value).Build();
    }

    private static async Task<JsonArray> Query(
        NpgsqlDataSource dataSource,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new JsonArray();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new JsonObject();
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index)
                    ? null
                    : JsonValue.Create(reader.GetValue(index).ToString());
            }
            rows.Add(row);
        }
        return rows;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

public sealed record NormalizedResponse(HttpStatusCode Status, string? ContentType, JsonNode? Body)
{
    public static async Task<NormalizedResponse> CreateAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? body = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            body = JsonNode.Parse(text);
            Normalize(body);
        }
        return new(response.StatusCode, response.Content.Headers.ContentType?.MediaType, body);
    }

    internal static void Normalize(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Key is "accessToken" or "refreshToken" or "correlationId")
                {
                    obj[property.Key] = $"<{property.Key}>";
                }
                else
                {
                    Normalize(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array) Normalize(item);
        }
    }
}
