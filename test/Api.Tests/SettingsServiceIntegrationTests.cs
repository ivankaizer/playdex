using System.Net;
using Api.Infrastructure.Http;
using Api.Modules.Notifications;
using Api.Modules.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class SettingsServiceIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ConcurrentFirstSavesForTheSameUserBothSucceed()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var userId = await CreateUser();
        try
        {
            await using var first = CreateDb();
            await using var second = CreateDb();

            await Task.WhenAll(
                new SettingsRepository(first).UpsertSettings(userId, 5, "1001", CancellationToken.None),
                new SettingsRepository(second).UpsertSettings(userId, 7, "1002", CancellationToken.None));

            await using var db = CreateDb();
            Assert.Equal(1, await db.UserSettings.CountAsync(row => row.UserId == userId));
            Assert.Equal(1, await db.NotificationChannels.CountAsync(row => row.UserId == userId));
        }
        finally
        {
            await DeleteUser(userId);
        }
    }

    [Fact]
    public async Task ClearingTheChatIdRemovesTheChannel()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var userId = await CreateUser();
        try
        {
            await using var db = CreateDb();
            var repository = new SettingsRepository(db);
            await repository.UpsertSettings(userId, 5, "1001", CancellationToken.None);

            var saved = await repository.UpsertSettings(userId, 10, null, CancellationToken.None);

            Assert.Equal(new UserSettingsResponseDto(10, null), saved);
            Assert.Equal(new UserSettingsResponseDto(10, null),
                await repository.LoadUserSettings(userId, CancellationToken.None));
        }
        finally
        {
            await DeleteUser(userId);
        }
    }

    [Fact]
    public async Task SettingsAreSavedBeforeAppriseAndAFailureIsAGatewayProblem()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var userId = await CreateUser();
        try
        {
            await using var db = CreateDb();
            var appriseHandler = new StatusHandler(HttpStatusCode.InternalServerError);
            var service = new SettingsService(new SettingsRepository(db), CreateApprise(appriseHandler));

            var exception = await Assert.ThrowsAsync<AppProblemException>(() => service.UpdateSettings(
                userId, new UpdateSettingsRequest(15, "4242"), CancellationToken.None));

            Assert.Equal(StatusCodes.Status502BadGateway, exception.StatusCode);
            Assert.StartsWith("Settings were saved", exception.Message);
            Assert.Equal(1, appriseHandler.Calls);
            Assert.Equal(new UserSettingsResponseDto(15, "4242"),
                await service.GetSettings(userId, CancellationToken.None));
        }
        finally
        {
            await DeleteUser(userId);
        }
    }

    [Fact]
    public async Task FailedTestNotificationIsAGatewayProblem()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var userId = await CreateUser();
        try
        {
            await using var db = CreateDb();
            await new SettingsRepository(db).UpsertSettings(userId, 0, "4242", CancellationToken.None);
            var service = new SettingsService(
                new SettingsRepository(db), CreateApprise(new StatusHandler(HttpStatusCode.FailedDependency)));

            var exception = await Assert.ThrowsAsync<AppProblemException>(() =>
                service.SendTestNotification(userId, CancellationToken.None));

            Assert.Equal(StatusCodes.Status502BadGateway, exception.StatusCode);
            Assert.Equal("Notification delivery failed. Check the Telegram chat ID and try again.", exception.Message);
        }
        finally
        {
            await DeleteUser(userId);
        }
    }

    private static AppriseClient CreateApprise(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://apprise.test/") },
        new AppriseOptions { BaseUrl = "https://apprise.test/", TelegramBotToken = "123456:test-token" });

    private async Task<long> CreateUser()
    {
        await using var db = CreateDb();
        var user = new User { Email = $"settings-{Guid.NewGuid():n}@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task DeleteUser(long userId)
    {
        await using var db = CreateDb();
        await db.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!)
            .Options);

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
