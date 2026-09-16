using System.Security.Claims;
using Admin.Features;
using Admin.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playdex.Persistence;
using Xunit;

namespace Admin.Tests;

[Collection(AdminAuthenticationFixtureGroup.Name)]
public sealed class GenerationHistoryIntegrationTests(AdminAuthenticationFixture fixture)
{
    [Fact]
    public async Task ScreenshotEndpointRequiresCurrentAdminAndPreventsCaching()
    {
        if (fixture.Factory is null) return;
        await using var scope=fixture.Factory.Services.CreateAsyncScope();
        var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlaydexDbContext>>();
        await using var db=await factory.CreateDbContextAsync();
        var record=new AiGeneration {Id=Guid.NewGuid(),UserId=await fixture.GetAdminIdAsync(),RequestId=Guid.NewGuid(),
            CreatedAt=DateTimeOffset.UtcNow,Provider="test",Model="test",Timezone="UTC",Image=[1,2,3],ImageContentType="image/png"};
        db.AiGenerations.Add(record);
        await db.SaveChangesAsync();
        using var client=fixture.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {BaseAddress=new Uri("https://admin.playdex.test"),AllowAutoRedirect=false});
        var url=$"/ai-generations/{record.Id}/image";
        try
        {
            using var anonymous=await client.GetAsync(url);
            Assert.Equal(System.Net.HttpStatusCode.Redirect,anonymous.StatusCode);
            using var login=await client.PostAsync("/auth/login",new FormUrlEncodedContent(new Dictionary<string,string>
                {["email"]=AdminAuthenticationFixture.AdminEmail,["password"]=AdminAuthenticationFixture.Password}));
            var markup=await client.GetStringAsync("/ai-generations");
            var stylesheet=System.Text.RegularExpressions.Regex.Match(markup,"href=\"([^\"]*app[^\"]*\\.css)\"").Groups[1].Value;
            Assert.NotEqual("app.css",stylesheet);
            Assert.False(string.IsNullOrEmpty(stylesheet));
            Assert.Contains("generation-comparison",await client.GetStringAsync(stylesheet),StringComparison.Ordinal);
            using var image=await client.GetAsync(url);
            Assert.Equal(System.Net.HttpStatusCode.OK,image.StatusCode);
            Assert.Equal("image/png",image.Content.Headers.ContentType?.MediaType);
            Assert.True(image.Headers.CacheControl?.NoStore);
            Assert.Equal("nosniff",Assert.Single(image.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal(new byte[]{1,2,3},await image.Content.ReadAsByteArrayAsync());
            await fixture.SetAdminAsync(false);
            using var denied=await client.GetAsync(url);
            Assert.NotEqual(System.Net.HttpStatusCode.OK,denied.StatusCode);
        }
        finally
        {
            await fixture.SetAdminAsync(true);
            await db.AiGenerations.Where(row=>row.Id==record.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task HistoryPaginatesFiltersAndProtectsEvidenceWhenRoleIsRevoked()
    {
        if (fixture.Factory is null) return;
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlaydexDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var userId = await fixture.GetAdminIdAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString(System.Globalization.CultureInfo.InvariantCulture))], "test"));
        var operations = new GenerationHistoryOperations(factory, new AdminOperationGuard(factory));
        var marker = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        var records = Enumerable.Range(0, 27).Select(i => new AiGeneration
        {
            Id=Guid.NewGuid(), UserId=userId, RequestId=Guid.NewGuid(), CreatedAt=now.AddSeconds(i),
            Provider="test", Model="test", Timezone="UTC", ReferenceDate=DateOnly.FromDateTime(now.UtcDateTime),
            InputText=marker, Image=[1,2,3], ImageContentType="image/png", Status=i == 0 ? "failure" : "success",
            ResponsesJson="[{\"Body\":\"malformed\"}]", ResultJson="{\"Rows\":[]}",
        }).ToArray();
        db.AiGenerations.AddRange(records);
        await db.SaveChangesAsync();
        try
        {
            var first = await operations.Search(principal, marker, null, 0);
            Assert.Equal(27, first.Total);
            Assert.Equal(25, first.Items.Count);
            Assert.Equal(records[^1].Id, first.Items[0].Id);
            Assert.Equal(2, (await operations.Search(principal, marker, null, 1)).Items.Count);
            Assert.Single((await operations.Search(principal, marker, "failure", 0)).Items);
            Assert.Equal(marker, (await operations.GetDetailsAsync(principal, records[0].Id))?.Generation.InputText);
            Assert.Equal(new byte[] {1,2,3}, (await operations.GetImage(principal, records[0].Id))?.Bytes);

            await fixture.SetAdminAsync(false);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.Search(principal, marker, null, 0));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.GetDetailsAsync(principal, records[0].Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.GetImage(principal, records[0].Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.Delete(principal, records[0].Id));
            await fixture.SetAdminAsync(true);
            Assert.True(await operations.Delete(principal, records[0].Id));
            Assert.Null(await operations.GetImage(principal, records[0].Id));
        }
        finally
        {
            await fixture.SetAdminAsync(true);
            await db.AiGenerations.Where(row => records.Select(item => item.Id).Contains(row.Id)).ExecuteDeleteAsync();
        }
    }
}
