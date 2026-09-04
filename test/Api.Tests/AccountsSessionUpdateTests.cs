using Api.Modules.Accounts;
using Api.Infrastructure.Http;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

public sealed class AccountsSessionUpdateTests
{
    [Fact]
    public void CompletedOwnedSessionAcceptsNewStartAndEndInstants()
    {
        var session = Session(userId: 7, ended: true);
        var startedAt = new DateTimeOffset(2026, 8, 4, 18, 30, 15, TimeSpan.FromHours(2));
        var endedAt = startedAt.AddHours(2);

        var update = AccountsService.ValidateSessionUpdate(
            session,
            userId: 7,
            new AccountSessionUpdateRequest(startedAt, endedAt));

        Assert.Equal(startedAt, update.StartedAt);
        Assert.Equal(endedAt, update.EndedAt);
    }

    [Fact]
    public void EndMustBeLaterThanStart()
    {
        var instant = DateTimeOffset.UtcNow;

        var exception = Assert.Throws<AppProblemException>(() =>
            AccountsService.ValidateSessionUpdate(
                Session(userId: 7, ended: true),
                userId: 7,
                new AccountSessionUpdateRequest(instant, instant)));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("End time must be later than start time.", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothTimestampsAreRequired(bool missingStart)
    {
        var startedAt = missingStart ? default : DateTimeOffset.UtcNow.AddHours(-1);
        var endedAt = missingStart ? DateTimeOffset.UtcNow : default;

        var exception = Assert.Throws<AppProblemException>(() =>
            AccountsService.ValidateSessionUpdate(
                Session(userId: 7, ended: true),
                userId: 7,
                new AccountSessionUpdateRequest(startedAt, endedAt)));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("Start and end time are required.", exception.Message);
    }

    [Fact]
    public void ActiveSessionCannotBeEdited()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);

        var exception = Assert.Throws<AppProblemException>(() =>
            AccountsService.ValidateSessionUpdate(
                Session(userId: 7, ended: false),
                userId: 7,
                new AccountSessionUpdateRequest(startedAt, startedAt.AddMinutes(30))));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("Only finished sessions can be edited.", exception.Message);
    }

    [Fact]
    public void AnotherUsersSessionIsReportedAsNotFound()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);

        var exception = Assert.Throws<AppProblemException>(() =>
            AccountsService.ValidateSessionUpdate(
                Session(userId: 99, ended: true),
                userId: 7,
                new AccountSessionUpdateRequest(startedAt, startedAt.AddMinutes(30))));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("Session not found.", exception.Message);
    }

    [Fact]
    public void MissingSessionIsReportedAsNotFound()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);

        var exception = Assert.Throws<AppProblemException>(() =>
            AccountsService.ValidateSessionUpdate(
                session: null,
                userId: 7,
                new AccountSessionUpdateRequest(startedAt, startedAt.AddMinutes(30))));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("Session not found.", exception.Message);
    }

    [Fact]
    public void ApplyingUpdateChangesOnlyTimesAndUpdatedTimestamp()
    {
        var session = Session(userId: 7, ended: true);
        session.IntegrationId = 19;
        var startedAt = new DateTimeOffset(2026, 8, 4, 18, 30, 15, TimeSpan.Zero);
        var endedAt = startedAt.AddHours(2);
        var updatedAt = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

        var response = AccountsService.ApplySessionUpdate(
            session,
            userId: 7,
            new AccountSessionUpdateRequest(startedAt, endedAt),
            updatedAt);

        Assert.Equal(session.Id, response.SessionId);
        Assert.Equal(startedAt, session.StartedAt);
        Assert.Equal(endedAt, session.EndedAt);
        Assert.Equal(updatedAt, session.UpdatedAt);
        Assert.Equal(13, session.GameId);
        Assert.Equal(17, session.PlatformId);
        Assert.Equal(CaptureMethod.Manual, session.CaptureMethod);
        Assert.Equal(19, session.IntegrationId);
    }

    private static PlaySession Session(long userId, bool ended)
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        return new PlaySession
        {
            Id = 11,
            UserId = userId,
            GameId = 13,
            PlatformId = 17,
            CaptureMethod = CaptureMethod.Manual,
            StartedAt = startedAt,
            EndedAt = ended ? startedAt.AddHours(1) : null,
        };
    }
}
