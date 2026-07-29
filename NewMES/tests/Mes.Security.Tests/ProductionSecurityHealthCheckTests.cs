using Mes.Api.Readiness;
using Mes.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mes.Security.Tests;

public sealed class ProductionSecurityHealthCheckTests
{
    [Fact]
    public async Task UnsafeConfigurationBlocksReadinessBeforeDatabasePrivilegeProbe()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MesDatabase"] =
                "Server=sqlserver;Database=NewMes;User Id=sa;Password=private",
            ["Security:DeploymentMode"] = "Production",
            ["Security:ExternalHttpsOnly"] = "false",
            ["Security:SecretsSource"] = "None",
            ["Security:JwtSigningKey"] = "demo-secret",
            ["Security:DemoInitializationEnabled"] = "true",
            ["Security:AllowedCorsOrigins:0"] = "*",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var provider = new ProductionSecurityContextProvider(
            configuration,
            new TestHostEnvironment("Development"),
            Path.Combine(Path.GetTempPath(), $"missing-newmes-secrets-{Guid.NewGuid():N}"));
        var privilegeProbe = new FailIfCalledPrivilegeProbe();
        var healthCheck = new ProductionSecurityHealthCheck(
            provider,
            privilegeProbe,
            NullLogger<ProductionSecurityHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.False(privilegeProbe.WasCalled);
        var codes = Assert.IsAssignableFrom<IEnumerable<string>>(result.Data["codes"]);
        Assert.Contains("SEC_ENVIRONMENT_NOT_PRODUCTION", codes);
        Assert.DoesNotContain("private", result.Description, StringComparison.Ordinal);
    }

    private sealed class FailIfCalledPrivilegeProbe : IRuntimeDatabasePrivilegeProbe
    {
        public bool WasCalled { get; private set; }

        public Task<DatabasePrivilegeResult> CheckAsync(
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("Privilege probe must not run.");
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Mes.Security.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
