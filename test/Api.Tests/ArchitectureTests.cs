using System.Reflection;
using System.Text.RegularExpressions;
using Api.Modules.Accounts;
using Api.Modules.Games;
using Api.Modules.Notifications;
using Api.Modules.Polling;
using Api.Modules.Posters;
using Api.Modules.Recap;
using Api.Modules.Sessions;
using NetArchTest.Rules;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Guards the dependency rules the API follows today, so they cannot erode silently:
/// Persistence knows nothing about the API, Infrastructure knows nothing about modules, and a module reaches into
/// another module only through that module's public contracts or an exception listed below.
/// A failing test here means a new cross-boundary dependency: prefer a public contract over a new exception.
/// </summary>
public sealed class ArchitectureTests
{
    private const string InfrastructureArea = "Infrastructure";

    private static readonly Assembly _apiAssembly = typeof(Program).Assembly;
    private static readonly Assembly _persistenceAssembly = typeof(PlaydexDbContext).Assembly;

    // Types a module publishes for any other module to use. Everything else in a module is internal to it.
    private static readonly Type[] _publicContracts =
    [
        typeof(IGames),
        typeof(IGameResolver),
        typeof(GameResolutionRequest),
        typeof(GameResolution),
        typeof(GameExternalIdentityTypes),
        // Polling, API capture, manual timers and provider settings start and end sessions only through these.
        typeof(SessionLifecycle),
        typeof(CaptureOutcome),
    ];

    // Existing direct uses of another module's types. Accepted as they are; a new one should usually be a contract.
    private static readonly CrossAreaDependency[] _allowedDependencies =
    [
        // Accounts computes the per-game stats that GET /games/{id}/stats returns in Games' DTOs.
        new("Accounts", typeof(GameStatsResponseDto)),
        new("Accounts", typeof(GameYearStatDto)),
        new("Accounts", typeof(GameMonthStatDto)),
        // Both capture paths build Steam capsule URLs with the Posters URL builder.
        new("ApiCapture", typeof(PosterUrls)),
        new("Polling", typeof(PosterUrls)),
        // GET /launcher/recap returns the web's recap unchanged.
        new("Launcher", typeof(RecapService)),
        new("Launcher", typeof(RecapResponseDto)),
        // The IGDB client is registered by Polling, but only Manual's game search uses it.
        new("Manual", typeof(IgdbClient)),
        new("Manual", typeof(IgdbOptions)),
        new("Manual", typeof(IgdbSearchResult)),
        // Settings configures and tests Telegram delivery through the Apprise client.
        new("Settings", typeof(AppriseClient)),
    ];

    // Dependencies that break the layering. They are exempted only so the rules keep guarding everything else;
    // remove an entry once the dependency is gone (ExceptionListsNameOnlyDependenciesThatStillExist enforces it).
    private static readonly CrossAreaDependency[] _knownLayeringViolations =
    [
        // GET /games/{id}/stats injects AccountsService while Accounts already depends on Games (IGames and the
        // stats DTOs), which makes Games <-> Accounts a cycle.
        new("Games", typeof(AccountsService)),
        // SessionLifecycle measures the polling gap in poll intervals, so it reads PollingOptions while Polling already
        // depends on Sessions (SessionLifecycle), which makes Polling <-> Sessions a cycle.
        new("Sessions", typeof(PollingOptions)),
    ];

    private static readonly Type[] _apiTypes = _apiAssembly.GetTypes().Where(type => !IsCompilerGenerated(type)).ToArray();

    public static TheoryData<string> Modules()
    {
        var modules = new TheoryData<string>();
        foreach (var module in _apiTypes
                     .Select(AreaOf)
                     .OfType<string>()
                     .Where(area => area != InfrastructureArea)
                     .Distinct()
                     .Order(StringComparer.Ordinal))
        {
            modules.Add(module);
        }

        return modules;
    }

    [Fact]
    public void PersistenceDoesNotDependOnApi()
    {
        Assert.DoesNotContain(
            _persistenceAssembly.GetReferencedAssemblies(),
            reference => reference.Name is "Api" or "ServiceDefaults");

        AssertNoDependencies(
            Types.InAssembly(_persistenceAssembly).That().ResideInNamespaceMatching(NamespacePattern("Playdex.Persistence")),
            ["Api"],
            "Persistence must not depend on the API.");
    }

    [Fact]
    public void InfrastructureDoesNotDependOnModules()
    {
        var exempt = ExemptionsFor(InfrastructureArea);
        var moduleTypes = _apiTypes
            .Where(type => AreaOf(type) is { } area && area != InfrastructureArea && !exempt.Contains(type));

        AssertNoDependencies(
            ApiTypesIn("Api.Infrastructure"),
            moduleTypes.Select(DependencyName),
            "Infrastructure must not depend on modules.");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void ModuleUsesOtherModulesOnlyThroughPublicContracts(string module)
    {
        var allowed = _publicContracts.Concat(ExemptionsFor(module)).ToHashSet();
        var otherModulesInternals = _apiTypes.Where(type =>
            AreaOf(type) is { } area && area != module && area != InfrastructureArea && !allowed.Contains(type));

        AssertNoDependencies(
            ApiTypesIn($"Api.Modules.{module}"),
            otherModulesInternals.Select(DependencyName),
            $"Module {module} may use another module only through its public contracts or a listed exception.");
    }

    [Fact]
    public void RepositoriesArePrivateToTheirOwningNamespace()
    {
        var repositories = _apiTypes
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal) && OwnerNamespace(type) is not null)
            .ToArray();
        Assert.NotEmpty(repositories);

        foreach (var repository in repositories)
        {
            AssertNoDependencies(
                Types.InAssembly(_apiAssembly).That().DoNotResideInNamespaceMatching(NamespacePattern(OwnerNamespace(repository)!)),
                [DependencyName(repository)],
                $"{repository.Name} may only be used inside {OwnerNamespace(repository)}.");
        }
    }

    [Fact]
    public void OnlyTheHostTouchesMigrations()
    {
        AssertNoDependencies(
            ModulesAndInfrastructure(),
            ["Playdex.Persistence.Migrations"],
            "Only Program and Api.Observability may use Playdex.Persistence.Migrations.");
    }

    [Fact]
    public void ModulesAndInfrastructureDoNotDependOnTheHost()
    {
        AssertNoDependencies(
            ModulesAndInfrastructure(),
            [DependencyName(typeof(Program)), "Api.Observability"],
            "Modules and Infrastructure must not depend on Program or Api.Observability.");
    }

    [Fact]
    public void InfrastructureLayersDoNotDependUpwards()
    {
        // Http is the bottom of Infrastructure, Common builds on Http, Auth builds on both.
        AssertNoDependencies(
            ApiTypesIn("Api.Infrastructure.Http"),
            ["Api.Infrastructure.Common", "Api.Infrastructure.Auth"],
            "Api.Infrastructure.Http must not depend on other Infrastructure namespaces.");
        AssertNoDependencies(
            ApiTypesIn("Api.Infrastructure.Common"),
            ["Api.Infrastructure.Auth"],
            "Api.Infrastructure.Common must not depend on Api.Infrastructure.Auth.");
    }

    [Fact]
    public void ExceptionListsNameOnlyDependenciesThatStillExist()
    {
        var problems = new List<string>();
        foreach (var exemption in _allowedDependencies.Concat(_knownLayeringViolations))
        {
            var target = exemption.Target;
            if (AreaOf(target) is null or InfrastructureArea || AreaOf(target) == exemption.Area)
            {
                problems.Add($"{exemption}: the target must be a type of another module.");
            }
            else if (_publicContracts.Contains(target))
            {
                problems.Add($"{exemption}: {target.Name} is a public contract and needs no exception.");
            }
            else if (!ApiTypesIn(AreaNamespace(exemption.Area)).And().HaveDependencyOn(DependencyName(target)).GetTypes().Any())
            {
                problems.Add($"{exemption}: no longer used, remove it from the list.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static PredicateList ApiTypesIn(string @namespace) =>
        Types.InAssembly(_apiAssembly).That().ResideInNamespaceMatching(NamespacePattern(@namespace));

    private static PredicateList ModulesAndInfrastructure() =>
        Types.InAssembly(_apiAssembly).That().ResideInNamespaceMatching(@"^Api\.(Modules|Infrastructure)(\..+)?$");

    private static void AssertNoDependencies(PredicateList subjects, IEnumerable<string> forbidden, string rule)
    {
        var forbiddenNames = forbidden.ToArray();
        Assert.NotEmpty(subjects.GetTypes());
        if (forbiddenNames.Length == 0)
        {
            return;
        }

        var result = subjects.ShouldNot().HaveDependencyOnAny(forbiddenNames).GetResult();
        if (result.IsSuccessful)
        {
            return;
        }

        var violations = result.FailingTypes.Select(type =>
            $"  {type.FullName} -> {string.Join(", ", forbiddenNames.Where(name => DependsOn(type, name)))}");
        Assert.Fail($"{rule}{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static bool DependsOn(Type type, string dependency) =>
        Types.InAssembly(type.Assembly)
            .That().ResideInNamespaceMatching($"^{Regex.Escape(type.Namespace ?? "")}$")
            .And().HaveName(type.Name)
            .And().HaveDependencyOn(dependency)
            .GetTypes()
            .Contains(type);

    private static HashSet<Type> ExemptionsFor(string area) =>
        _allowedDependencies.Concat(_knownLayeringViolations)
            .Where(exemption => exemption.Area == area)
            .Select(exemption => exemption.Target)
            .ToHashSet();

    // A module is everything under Api.Modules.<Name>; Infrastructure is one area for module-boundary purposes.
    private static string? AreaOf(Type type) => type.Namespace switch
    {
        { } ns when ns.StartsWith("Api.Modules.", StringComparison.Ordinal) => ns.Split('.')[2],
        { } ns when ns == "Api.Infrastructure" || ns.StartsWith("Api.Infrastructure.", StringComparison.Ordinal) =>
            InfrastructureArea,
        _ => null,
    };

    private static string AreaNamespace(string area) =>
        area == InfrastructureArea ? "Api.Infrastructure" : $"Api.Modules.{area}";

    // Api.Modules.<Name> for module types, Api.Infrastructure.<Name> for infrastructure types.
    private static string? OwnerNamespace(Type type) =>
        type.Namespace?.Split('.') is ["Api", "Modules" or "Infrastructure", var name, ..] parts
            ? $"{parts[0]}.{parts[1]}.{name}"
            : null;

    private static string NamespacePattern(string @namespace) => $@"^{Regex.Escape(@namespace)}(\..+)?$";

    // NetArchTest names a type in the global namespace (Program) with a leading dot.
    private static string DependencyName(Type type) => type.Namespace is null ? $".{type.FullName}" : type.FullName!;

    private static bool IsCompilerGenerated(Type type) =>
        type.Name.Contains('<', StringComparison.Ordinal) ||
        (type.DeclaringType is { } declaringType && IsCompilerGenerated(declaringType));

    private sealed record CrossAreaDependency(string Area, Type Target)
    {
        public override string ToString() => $"{Area} -> {Target.FullName}";
    }
}
