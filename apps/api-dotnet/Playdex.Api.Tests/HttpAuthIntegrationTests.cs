using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Playdex.Api.Shared.Persistence;
using Xunit;

namespace Playdex.Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HttpIntegrationCollection
{
    public const string Name = "HTTP integration";
}

[Collection(HttpIntegrationCollection.Name)]
public sealed class HttpAuthIntegrationTests
{
    private const string JwtSecret = "http-integration-secret-that-is-long-enough-for-hmac-sha256";

    [Fact]
    public async Task RootAndProtectedEndpoint_ReturnExpectedStatusCodes()
    {
        var databaseUrl = ConfigureIntegrationEnvironment();
        if (databaseUrl is null) return;

        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var root = await client.GetAsync("/api");
        var me = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Equal("application/json", root.Content.Headers.ContentType?.ToString());
        Assert.Equal("{\"data\":\"Ok\",\"error\":null}", await root.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(
            "{\"data\":null,\"error\":{\"error\":\"Missing token.\"}}",
            await me.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CorsPreflight_RemainsAnEmpty204Response()
    {
        var databaseUrl = ConfigureIntegrationEnvironment();
        if (databaseUrl is null) return;

        Environment.SetEnvironmentVariable("CORS_ORIGINS", "http://127.0.0.1:5175");
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/me");
        request.Headers.Add("Origin", "http://127.0.0.1:5175");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LoginAndMe_AuthenticateAnExistingBcryptUser()
    {
        var databaseUrl = ConfigureIntegrationEnvironment();
        if (databaseUrl is null) return;

        var email = $"dotnet-auth-{Guid.NewGuid():n}@example.com";
        var password = "correct horse battery staple";
        await using var dataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        var userId = await InsertUser(dataSource, email, password);

        try
        {
            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/login", new { email, password });
            var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            var loginData = loginJson.RootElement.GetProperty("data");
            var accessToken = loginData.GetProperty("accessToken").GetString();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            var me = await client.GetAsync("/api/me");
            var meJson = JsonDocument.Parse(await me.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            var meData = meJson.RootElement.GetProperty("data");
            Assert.Equal(userId, meData.GetProperty("userId").GetInt64());
            Assert.Equal(email, meData.GetProperty("email").GetString());
        }
        finally
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var cleanup = new NpgsqlCommand("delete from users where id = @id", connection);
            cleanup.Parameters.AddWithValue("id", userId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static string? ConfigureIntegrationEnvironment()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl)) return null;

        Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        Environment.SetEnvironmentVariable("JWT_SECRET", JwtSecret);
        return databaseUrl;
    }

    private static async Task<long> InsertUser(NpgsqlDataSource dataSource, string email, string password)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "insert into users (email, password) values (@email, @password) returning id",
            connection);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("password", BCrypt.Net.BCrypt.HashPassword(password));
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
