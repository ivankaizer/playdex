using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Playdex.Api.Data;
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
    public async Task RootAndProtectedEndpoint_PreserveResponseEnvelopes()
    {
        var databaseUrl = ConfigureIntegrationEnvironment();
        if (databaseUrl is null)
        {
            return;
        }

        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var root = await client.GetAsync("/api");
        var me = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Equal("{\"data\":\"Ok\",\"error\":null}", await root.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(
            "{\"data\":null,\"error\":{\"error\":\"Missing token.\"}}",
            await me.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LoginAndMe_AuthenticateAnExistingBcryptUser()
    {
        var databaseUrl = ConfigureIntegrationEnvironment();
        if (databaseUrl is null)
        {
            return;
        }

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
            var accessToken = loginJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var me = await client.GetAsync("/api/me");
            var meJson = JsonDocument.Parse(await me.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.Equal(userId, meJson.RootElement.GetProperty("data").GetProperty("userId").GetInt64());
            Assert.Equal(email, meJson.RootElement.GetProperty("data").GetProperty("email").GetString());
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
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return null;
        }

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
