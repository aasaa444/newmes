using Mes.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Mes.Security.Tests;

/// <summary>验证宿主配置与文件型秘密被准确投影为生产安全检查上下文。</summary>
public sealed class ProductionSecurityContextProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"newmes-security-context-{Guid.NewGuid():N}");

    public ProductionSecurityContextProviderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void BothRequiredSecretFilesMustBePresentAndNonEmpty()
    {
        var configuration = new ConfigurationBuilder().Build();
        var provider = new ProductionSecurityContextProvider(
            configuration,
            new SecurityTestHostEnvironment(),
            _directory);
        File.WriteAllText(
            Path.Combine(_directory, "ConnectionStrings__MesDatabase"),
            "Server=sqlserver;Database=NewMes");

        Assert.False(provider.Read().ExternalSecretsVerified);

        File.WriteAllText(
            Path.Combine(_directory, "Security__JwtSigningKey"),
            "mR7fQ2xL9vN4cK8pT6wY3zB5dH1sJ0uG");

        Assert.True(provider.Read().ExternalSecretsVerified);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class SecurityTestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";

        public string ApplicationName { get; set; } = "Mes.Security.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
