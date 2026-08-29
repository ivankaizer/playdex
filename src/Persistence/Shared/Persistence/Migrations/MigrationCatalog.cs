using System.Text.RegularExpressions;

namespace Shared.Persistence.Migrations;

public sealed class MigrationCatalog
{
    private static readonly Regex IdentifierPattern = new(
        "^[0-9]{3}_[a-z0-9]+(?:_[a-z0-9]+)*$",
        RegexOptions.CultureInvariant);

    public MigrationCatalog(IEnumerable<IPlaydexMigration> migrations)
    {
        Migrations = migrations.OrderBy(migration => migration.Id, StringComparer.Ordinal).ToArray();

        foreach (var migration in Migrations)
        {
            if (!IdentifierPattern.IsMatch(migration.Id))
            {
                throw new InvalidOperationException(
                    $"Migration identifier '{migration.Id}' must match NNN_slug.");
            }
        }

        var duplicate = Migrations.GroupBy(migration => migration.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate migration identifier '{duplicate.Key}'.");
        }
    }

    public IReadOnlyList<IPlaydexMigration> Migrations { get; }

    public void ValidateApplied(IReadOnlyList<string> applied)
    {
        var indexes = Migrations
            .Select((migration, index) => (migration.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        var appliedIds = new HashSet<string>(StringComparer.Ordinal);
        var appliedStages = new HashSet<string>(StringComparer.Ordinal);
        var previousIndex = -1;

        foreach (var migrationId in applied)
        {
            if (!indexes.TryGetValue(migrationId, out var index))
            {
                throw new InvalidOperationException(
                    $"Database migration history contains unknown migration '{migrationId}'.");
            }

            if (index <= previousIndex)
            {
                throw new InvalidOperationException(
                    $"Database migration history '{migrationId}' is out of order.");
            }

            previousIndex = index;
            appliedIds.Add(migrationId);
            appliedStages.Add(GetStage(migrationId));
        }

        for (var index = 0; index < previousIndex; index++)
        {
            var migrationId = Migrations[index].Id;
            if (!appliedIds.Contains(migrationId) && !appliedStages.Contains(GetStage(migrationId)))
            {
                throw new InvalidOperationException(
                    $"Database migration history is missing required migration '{migrationId}'.");
            }
        }
    }

    public IReadOnlyList<IPlaydexMigration> GetPendingMigrations(IReadOnlyList<string> applied)
    {
        ValidateApplied(applied);
        var appliedIds = applied.ToHashSet(StringComparer.Ordinal);
        return Migrations.Where(migration => !appliedIds.Contains(migration.Id)).ToArray();
    }

    private static string GetStage(string migrationId) => migrationId[..3];
}
