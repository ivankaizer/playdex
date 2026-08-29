using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

public sealed class MigrationCatalogTests
{
    [Fact]
    public void MergedSiblingMigrationDoesNotInvalidateAppliedLaterStages()
    {
        var catalog = CreateCatalog();
        var applied = new[]
        {
            "001_current_schema",
            "002_credential_api_key_lock",
            "003_openxbl_platform",
            "004_game_external_identities",
            "005_integrations",
            "006_play_session_target_fields",
            "007_polling_target_model",
            "008_manual_target_model",
            "009_drop_legacy_model",
            "010_correct_legacy_api_session_platforms",
            "011_pc_platform_hierarchy",
        };

        var pending = catalog.GetPendingMigrations(applied);

        var migration = Assert.Single(pending);
        Assert.Equal("004_notification_channels", migration.Id);
    }

    [Fact]
    public void MainHistoryCanApplyMissingSiblingAndFollowingStages()
    {
        var catalog = CreateCatalog();
        var applied = new[]
        {
            "001_current_schema",
            "002_credential_api_key_lock",
            "003_openxbl_platform",
            "004_notification_channels",
        };

        var pending = catalog.GetPendingMigrations(applied);

        Assert.Equal(
            [
                "004_game_external_identities",
                "005_integrations",
                "006_play_session_target_fields",
                "007_polling_target_model",
                "008_manual_target_model",
                "009_drop_legacy_model",
                "010_correct_legacy_api_session_platforms",
                "011_pc_platform_hierarchy",
            ],
            pending.Select(migration => migration.Id));
    }

    [Fact]
    public void MissingUnrelatedStageStillInvalidatesHistory()
    {
        var catalog = CreateCatalog();
        var applied = new[]
        {
            "001_current_schema",
            "003_openxbl_platform",
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => catalog.GetPendingMigrations(applied));

        Assert.Contains("002_credential_api_key_lock", exception.Message);
    }

    private static MigrationCatalog CreateCatalog() => new(
    [
        new CurrentSchemaMigration(),
        new CredentialApiKeyLockMigration(),
        new OpenXblPlatformMigration(),
        new NotificationChannelsMigration(),
        new GameExternalIdentitiesMigration(),
        new IntegrationsMigration(),
        new PlaySessionTargetFieldsMigration(),
        new PollingTargetModelMigration(),
        new ManualTargetModelMigration(),
        new DropLegacyModelMigration(),
        new CorrectLegacyApiSessionPlatformsMigration(),
        new PcPlatformHierarchyMigration(),
    ]);
}
