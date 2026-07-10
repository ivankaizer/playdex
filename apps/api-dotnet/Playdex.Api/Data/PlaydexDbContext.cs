using Microsoft.EntityFrameworkCore;

namespace Playdex.Api.Data;

public sealed class PlaydexDbContext(DbContextOptions<PlaydexDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<PlatformCredential> PlatformCredentials => Set<PlatformCredential>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<PlaySession> PlaySessions => Set<PlaySession>();
    public DbSet<PollResult> PollResults => Set<PollResult>();
    public DbSet<ManualGameInput> ManualGameInputs => Set<ManualGameInput>();
    public DbSet<IgnoredGame> IgnoredGames => Set<IgnoredGame>();
    public DbSet<GameRating> GameRatings => Set<GameRating>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<GameConnection> GameConnections => Set<GameConnection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUsers(modelBuilder);
        ConfigurePlatforms(modelBuilder);
        ConfigurePlatformCredentials(modelBuilder);
        ConfigureGames(modelBuilder);
        ConfigurePlaySessions(modelBuilder);
        ConfigurePollResults(modelBuilder);
        ConfigureManualGameInputs(modelBuilder);
        ConfigureIgnoredGames(modelBuilder);
        ConfigureGameRatings(modelBuilder);
        ConfigureUserSettings(modelBuilder);
        ConfigureGameConnections(modelBuilder);
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
        entity.Property(row => row.ChatId).HasColumnName("chat_id");
        entity.Property(row => row.IsAdmin).HasColumnName("is_admin");
        entity.HasIndex(row => row.Email).IsUnique();
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

    private static void ConfigurePlatformCredentials(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlatformCredential>();
        entity.ToTable("platform_credentials");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.ApiKey).HasColumnName("api_key");
        entity.Property(row => row.AccountId).HasColumnName("account_id");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.Property(row => row.Active).HasColumnName("active");
        entity.Property(row => row.PlatformId).HasColumnName("platform_id");
        entity.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Platform>().WithMany().HasForeignKey(row => row.PlatformId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureGames(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Game>();
        entity.ToTable("games");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.Title).HasColumnName("title");
        entity.Property(row => row.ExternalId).HasColumnName("external_id");
        entity.Property(row => row.CanonicalKey).HasColumnName("canonical_key");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.Property(row => row.CapsuleUrl).HasColumnName("capsule_url");
        entity.Property(row => row.PlatformId).HasColumnName("platform_id");
        entity.Property(row => row.CapsuleUrlLocked).HasColumnName("capsule_url_locked");
        entity.Property(row => row.TitleLocked).HasColumnName("title_locked");
        entity.HasIndex(row => row.CanonicalKey).IsUnique();
        entity.HasOne<Platform>().WithMany().HasForeignKey(row => row.PlatformId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigurePlaySessions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlaySession>();
        entity.ToTable("play_sessions");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.PlatformCredentialId).HasColumnName("platform_credential_id");
        entity.Property(row => row.GameId).HasColumnName("game_id");
        entity.Property(row => row.StartedAt).HasColumnName("started_at");
        entity.Property(row => row.EndedAt).HasColumnName("ended_at");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasOne<PlatformCredential>().WithMany().HasForeignKey(row => row.PlatformCredentialId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(row => row.GameId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigurePollResults(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PollResult>();
        entity.ToTable("poll_results");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.PlatformCredentialId).HasColumnName("platform_credential_id");
        entity.Property(row => row.GameId).HasColumnName("game_id");
        entity.Property(row => row.PolledAt).HasColumnName("polled_at");
        entity.Property(row => row.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.HasOne<PlatformCredential>().WithMany().HasForeignKey(row => row.PlatformCredentialId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(row => row.GameId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureManualGameInputs(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ManualGameInput>();
        entity.ToTable("manual_game_inputs");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(row => row.UserId).HasColumnName("user_id");
        entity.Property(row => row.RawTitle).HasColumnName("raw_title");
        entity.Property(row => row.CanonicalTitle).HasColumnName("canonical_title");
        entity.Property(row => row.MappedGameId).HasColumnName("mapped_game_id");
        entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(row => new { row.UserId, row.CanonicalTitle }).IsUnique();
        entity.HasOne<User>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(row => row.MappedGameId).OnDelete(DeleteBehavior.SetNull);
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
