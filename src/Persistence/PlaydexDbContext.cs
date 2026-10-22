using Microsoft.EntityFrameworkCore;

namespace Playdex.Persistence;

public sealed class PlaydexDbContext(DbContextOptions<PlaydexDbContext> options) : DbContext(options)
{
    /// <summary>Unique (user_id, type, account_id) of polling integrations; see IndexesAndConstraintsMigration.</summary>
    public const string UniqueAccountIndex = "integrations_user_type_account_unique";

    public DbSet<User> Users => Set<User>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameExternalIdentity> GameExternalIdentities => Set<GameExternalIdentity>();
    public DbSet<PlaySession> PlaySessions => Set<PlaySession>();
    public DbSet<PollResult> PollResults => Set<PollResult>();
    public DbSet<IgnoredGame> IgnoredGames => Set<IgnoredGame>();
    public DbSet<GameRating> GameRatings => Set<GameRating>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<GameConnection> GameConnections => Set<GameConnection>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();

    public DbSet<SessionDraftBatch> SessionDraftBatches => Set<SessionDraftBatch>();
    public DbSet<SessionDraftRow> SessionDraftRows => Set<SessionDraftRow>();
    public DbSet<AiGeneration> AiGenerations => Set<AiGeneration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureSessionDrafts(modelBuilder);
        ConfigureAiGenerations(modelBuilder);
        ConfigureUsers(modelBuilder);
        ConfigurePlatforms(modelBuilder);
        ConfigureIntegrations(modelBuilder);
        ConfigureGames(modelBuilder);
        ConfigureGameExternalIdentities(modelBuilder);
        ConfigurePlaySessions(modelBuilder);
        ConfigurePollResults(modelBuilder);
        ConfigureIgnoredGames(modelBuilder);
        ConfigureGameRatings(modelBuilder);
        ConfigureUserSettings(modelBuilder);
        ConfigureGameConnections(modelBuilder);
        ConfigureNotificationChannels(modelBuilder);
    }

    private static void ConfigureAiGenerations(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AiGeneration>();
        entity.ToTable("ai_generations");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.RequestId).HasColumnName("request_id");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.CompletedAt).HasColumnName("completed_at");
        entity.Property(row => row.Provider).HasColumnName("provider");
        entity.Property(row => row.Model).HasColumnName("model");
        entity.Property(row => row.Timezone).HasColumnName("timezone");
        entity.Property(row => row.ReferenceDate).HasColumnName("reference_date");
        entity.Property(row => row.InputText).HasColumnName("input_text");
        entity.Property(row => row.Image).HasColumnName("image");
        entity.Property(row => row.ImageContentType).HasColumnName("image_content_type");
        entity.Property(row => row.Status).HasColumnName("status");
        entity.Property(row => row.Error).HasColumnName("error");
        entity.Property(row => row.ResultJson).HasColumnName("result_json").HasColumnType("jsonb");
        entity.Property(row => row.ResponsesJson).HasColumnName("responses_json").HasColumnType("jsonb");
        entity.HasIndex(row => row.CreatedAt);
        entity.HasIndex(row => row.UserId);
        entity.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSessionDrafts(ModelBuilder modelBuilder)
    {
        var batch = modelBuilder.Entity<SessionDraftBatch>();
        batch.ToTable("session_draft_batches");
        batch.HasKey(row => row.Id);
        batch.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        batch.Property(row => row.UserId).HasColumnName("user_id");
        batch.Property(row => row.RequestId).HasColumnName("request_id");
        batch.Property(row => row.CreatedAt).HasColumnName("created_at");
        batch.Property(row => row.Timezone).HasColumnName("timezone");
        batch.Property(row => row.ReferenceDate).HasColumnName("reference_date");
        batch.HasIndex(row => new { row.UserId, row.RequestId }).IsUnique();
        batch.HasIndex(row => new { row.UserId, row.CreatedAt });
        batch.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        batch.HasMany(row => row.Rows).WithOne().HasForeignKey(row => row.BatchId).OnDelete(DeleteBehavior.Cascade);

        var entity = modelBuilder.Entity<SessionDraftRow>();
        entity.ToTable("session_draft_rows", table =>
        {
            table.HasCheckConstraint("session_draft_rows_revision_check", "revision >= 1");
            table.HasCheckConstraint("session_draft_rows_status_check", "status in ('pending', 'accepted', 'deleted')");
            table.HasCheckConstraint("session_draft_rows_accepted_link_check", "(status = 'accepted' and accepted_session_id is not null) or (status <> 'accepted' and accepted_session_id is null)");
        });
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.BatchId).HasColumnName("batch_id");
        entity.Property(row => row.Position).HasColumnName("position");
        entity.Property(row => row.Title).HasColumnName("title");
        entity.Property(row => row.Date).HasColumnName("date");
        entity.Property(row => row.DurationMinutes).HasColumnName("duration_minutes");
        entity.Property(row => row.PlatformKey).HasColumnName("platform_key");
        entity.Property(row => row.GameId).HasColumnName("game_id");
        entity.Property(row => row.IgdbId).HasColumnName("igdb_id");
        entity.Property(row => row.Warnings).HasColumnName("warnings");
        entity.Property(row => row.Revision).HasColumnName("revision");
        entity.Property(row => row.Status).HasColumnName("status");
        entity.Property(row => row.AcceptedSessionId).HasColumnName("accepted_session_id");
        entity.Property(row => row.SelectionJson).HasColumnName("selection_json").HasColumnType("jsonb");
        entity.HasIndex(row => new { row.BatchId, row.Position }).IsUnique();
        entity.HasIndex(row => row.AcceptedSessionId).IsUnique().HasFilter("accepted_session_id is not null");
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<User>();
        entity.ToTable("users");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.Email).HasColumnName("email");
        entity.Property(row => row.Password).HasColumnName("password");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.Property(row => row.IsAdmin).HasColumnName("is_admin");
        entity.Property(row => row.TokenVersion).HasColumnName("token_version");
        entity.HasIndex(row => row.Email).IsUnique();
    }

    private static void ConfigureNotificationChannels(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NotificationChannel>();
        entity.ToTable("notification_channels");
        entity.HasKey(row => new { row.UserId, row.Transport });
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.Transport).HasColumnName("transport");
        entity.Property(row => row.Destination).HasColumnName("destination");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigurePlatforms(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Platform>();
        entity.ToTable("platforms");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.Key).HasColumnName("key");
        entity.Property(row => row.Name).HasColumnName("name");
        entity.Property(row => row.IgdbPlatformId).HasColumnName("igdb_platform_id");
        entity.Property(row => row.ParentId).HasColumnName("parent_id");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(row => row.Key).IsUnique();
        entity.HasOne<Platform>().WithMany().HasForeignKey(row => row.ParentId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureGames(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Game>();
        entity.ToTable("games");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.Title).HasColumnName("title");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.Property(row => row.CapsuleUrl).HasColumnName("capsule_url");
        entity.Property(row => row.CapsuleUrlLocked).HasColumnName("capsule_url_locked");
        entity.Property(row => row.TitleLocked).HasColumnName("title_locked");
    }

    private static void ConfigureIntegrations(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Integration>();
        entity.ToTable(
            "integrations",
            table => table.HasCheckConstraint(
                "integrations_platform_ownership_check",
                "(type = 'api' AND platform_id IS NULL) OR (type <> 'api' AND platform_id IS NOT NULL)"));
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.Type).HasColumnName("type").HasColumnType("integration_type");
        entity.Property(row => row.PlatformId).HasColumnName("platform_id");
        entity.Property(row => row.SessionPlatformId).HasColumnName("session_platform_id");
        entity.Property(row => row.ApiKey).HasColumnName("api_key");
        entity.Property(row => row.AccountId).HasColumnName("account_id");
        entity.Property(row => row.ApiKeyLocked).HasColumnName("api_key_locked");
        entity.Property(row => row.Active).HasColumnName("active");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(row => row.UserId);
        entity.HasIndex(row => row.PlatformId);
        entity.HasIndex(row => row.SessionPlatformId);
        entity.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(row => new { row.UserId, row.Type, row.AccountId })
            .HasDatabaseName(UniqueAccountIndex)
            .IsUnique()
            .HasFilter("type <> 'api'");
        entity.HasOne<Platform>().WithMany().HasForeignKey(row => row.PlatformId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Platform>().WithMany().HasForeignKey(row => row.SessionPlatformId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureGameExternalIdentities(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<GameExternalIdentity>();
        entity.ToTable(
            "game_external_identities",
            table => table.HasCheckConstraint(
                "game_external_identities_external_id_check",
                "external_id = btrim(external_id) AND external_id <> ''"));
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.GameId).HasColumnName("game_id");
        entity.Property(row => row.Type).HasColumnName("type").HasColumnType("game_external_identity_type");
        entity.Property(row => row.ExternalId).HasColumnName("external_id");
        entity.Property(row => row.Title).HasColumnName("title");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(row => new { row.Type, row.ExternalId }).IsUnique();
        entity.HasIndex(row => row.GameId);
        entity.HasOne<Game>().WithMany().HasForeignKey(row => row.GameId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePlaySessions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlaySession>();
        entity.ToTable("play_sessions");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.GameId).HasColumnName("game_id");
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.PlatformId).HasColumnName("platform_id");
        entity.Property(row => row.CaptureMethod).HasColumnName("capture_method").HasColumnType("capture_method");
        entity.Property(row => row.IntegrationId).HasColumnName("integration_id");
        entity.Property(row => row.StartedAt).HasColumnName("started_at");
        entity.Property(row => row.EndedAt).HasColumnName("ended_at");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(row => row.GameId);
        entity.HasIndex(row => row.StartedAt);
        entity.HasOne<Game>().WithMany().HasForeignKey(row => row.GameId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Platform>().WithMany().HasForeignKey(row => row.PlatformId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Integration>().WithMany().HasForeignKey(row => row.IntegrationId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePollResults(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PollResult>();
        entity.ToTable("poll_results");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.IntegrationId).HasColumnName("integration_id");
        entity.Property(row => row.GameExternalIdentityId).HasColumnName("game_external_identity_id");
        entity.Property(row => row.PolledAt).HasColumnName("polled_at");
        entity.Property(row => row.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.HasIndex(row => new { row.IntegrationId, row.PolledAt }).IsDescending(false, true);
        entity.HasOne<Integration>().WithMany().HasForeignKey(row => row.IntegrationId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<GameExternalIdentity>().WithMany().HasForeignKey(row => row.GameExternalIdentityId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureIgnoredGames(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<IgnoredGame>();
        entity.ToTable("ignored_games");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.GameId).HasColumnName("game_id");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(row => new { row.UserId, row.GameId }).IsUnique();
        entity.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(row => row.GameId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureGameRatings(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<GameRating>();
        entity.ToTable("game_ratings", table => table.HasCheckConstraint("game_ratings_rating_check", "rating >= 1 and rating <= 5"));
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.GameId).HasColumnName("game_id");
        entity.Property(row => row.Rating).HasColumnName("rating");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(row => new { row.UserId, row.GameId }).IsUnique();
        entity.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(row => row.GameId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureUserSettings(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserSettings>();
        entity.ToTable("user_settings");
        entity.HasKey(row => row.UserId);
        entity.Property(row => row.UserId).HasColumnName("user_id").ValueGeneratedNever();
        entity.Property(row => row.IgnoreShortSessionsMinutes).HasColumnName("ignore_short_sessions_minutes");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasOne<User>().WithOne().HasForeignKey<UserSettings>(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureGameConnections(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<GameConnection>();
        entity.ToTable("game_connections", table => table.HasCheckConstraint("game_connections_check", "game_id < connected_game_id"));
        entity.HasKey(row => new { row.UserId, row.GameId, row.ConnectedGameId });
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.GameId).HasColumnName("game_id");
        entity.Property(row => row.ConnectedGameId).HasColumnName("connected_game_id");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(row => row.GameId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(row => row.ConnectedGameId).OnDelete(DeleteBehavior.Cascade);
    }
}
