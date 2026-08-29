using Xunit;
using Shared.Persistence.Migrations;

namespace Admin.Tests;

public sealed class AdminProjectContractTests
{
    [Fact]
    public void Admin_project_is_a_server_application()
    {
        Assert.Equal("Admin", typeof(Program).Assembly.GetName().Name);
    }

    [Fact]
    public void Admin_does_not_reference_or_register_migrations()
    {
        var project = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Admin/Admin.csproj")));
        Assert.DoesNotContain("Api.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Web.csproj", project, StringComparison.Ordinal);
        Assert.NotEqual(typeof(MigrationRunner).Assembly, typeof(Program).Assembly);
    }
}
