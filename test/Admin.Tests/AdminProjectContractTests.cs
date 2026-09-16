using Xunit;
using Playdex.Persistence.Migrations;

namespace Admin.Tests;

public sealed class AdminProjectContractTests
{
    [Fact]
    public void AdminProjectIsAServerApplication()
    {
        Assert.Equal("Admin", typeof(Program).Assembly.GetName().Name);
    }

    [Fact]
    public void AdminDoesNotReferenceOrRegisterMigrations()
    {
        var project = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Admin/Admin.csproj")));
        Assert.DoesNotContain("Api.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Web.csproj", project, StringComparison.Ordinal);
        Assert.NotEqual(typeof(MigrationRunner).Assembly, typeof(Program).Assembly);
    }
}
