using System.Text.RegularExpressions;

namespace Api.Infrastructure.Persistence.Migrations;

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
        if (applied.Count > Migrations.Count)
        {
            throw new InvalidOperationException(
                $"Database migration history contains unknown migration '{applied[Migrations.Count]}'.");
        }

        for (var index = 0; index < applied.Count; index++)
        {
            if (!string.Equals(applied[index], Migrations[index].Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Database migration history '{applied[index]}' does not match expected ordered migration '{Migrations[index].Id}'.");
            }
        }
    }
}
