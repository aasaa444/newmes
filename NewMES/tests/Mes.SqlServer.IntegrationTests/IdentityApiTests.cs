using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mes.Domain.Identity;
using Mes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Mes.SqlServer.IntegrationTests;

[Collection(SqlServerFixtureProvider.Name)]
public sealed class IdentityApiTests(SqlServerFixture server)
{
    [SqlServerFact]
    public async Task LoginAndCurrentIdentityExposeEffectiveDatabaseCapabilities()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var userId = Guid.NewGuid();
        await using (var context = CreateContext(connectionString))
        {
            await context.Database.MigrateAsync();
            var user = new UserAccount
            {
                Id = userId,
                Username = "combined.user",
                DisplayName = "Combined User",
                IsActive = true,
                PrimaryRole = BusinessRole.Operator,
            };
            user.PasswordHash = new PasswordHasher<UserAccount>().HashPassword(
                user,
                "IntegrationOnly-Password-03!");
            user.RoleAssignments.Add(new UserRoleAssignment
            {
                UserAccountId = userId,
                Role = BusinessRole.Operator,
                UserAccount = user,
            });
            user.RoleAssignments.Add(new UserRoleAssignment
            {
                UserAccountId = userId,
                Role = BusinessRole.QualityEngineer,
                UserAccount = user,
            });
            context.UserAccounts.Add(user);
            await context.SaveChangesAsync();
        }

        var previousConnection = Environment.GetEnvironmentVariable(
            "ConnectionStrings__MesDatabase");
        var previousSigningKey = Environment.GetEnvironmentVariable(
            "Security__JwtSigningKey");
        try
        {
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__MesDatabase",
                connectionString);
            Environment.SetEnvironmentVariable(
                "Security__JwtSigningKey",
                "IntegrationOnlySigningKey_03_AtLeast32Characters");
            await using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
            using var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { username = "combined.user", password = "IntegrationOnly-Password-03!" });
            login.EnsureSuccessStatusCode();
            var loginPayload = await login.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                loginPayload.GetProperty("accessToken").GetString());

            var response = await client.GetFromJsonAsync<JsonElement>("/api/identity/me");

            Assert.Equal("Operator", response.GetProperty("primaryRole").GetString());
            Assert.Contains(
                response.GetProperty("roles").EnumerateArray(),
                role => role.GetString() == "QualityEngineer");
            Assert.Contains(
                response.GetProperty("capabilities").EnumerateArray(),
                capability => capability.GetString() == "QualityDispositionApprove");
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__MesDatabase",
                previousConnection);
            Environment.SetEnvironmentVariable("Security__JwtSigningKey", previousSigningKey);
        }
    }

    private static MesDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new MesDbContext(options);
    }
}
