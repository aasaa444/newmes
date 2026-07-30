using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Mes.Infrastructure.Security;

/// <summary>从实际宿主环境和配置源构造安全快照，不把读取配置与规则判断混在一起。</summary>
public sealed class ProductionSecurityContextProvider(
    IConfiguration configuration,
    IHostEnvironment environment,
    string secretsDirectory)
{
    public bool RequiresProductionBaseline =>
        environment.IsProduction()
        || string.Equals(
            configuration["Security:DeploymentMode"],
            "Production",
            StringComparison.Ordinal);

    public ProductionSecurityContext Read()
    {
        bool? demoInitialization = bool.TryParse(
            configuration["Security:DemoInitializationEnabled"],
            out var parsedDemoInitialization)
            ? parsedDemoInitialization
            : null;
        var corsOrigins = configuration
            .GetSection("Security:AllowedCorsOrigins")
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        return new ProductionSecurityContext(
            RuntimeEnvironment: environment.EnvironmentName,
            DeploymentMode: configuration["Security:DeploymentMode"] ?? string.Empty,
            ExternalHttpsOnly: bool.TryParse(
                configuration["Security:ExternalHttpsOnly"],
                out var externalHttpsOnly) && externalHttpsOnly,
            SecretsSource: configuration["Security:SecretsSource"],
            ExternalSecretsVerified: RequiredSecretFilesAreAvailable(),
            SigningKey: configuration["Security:JwtSigningKey"],
            DemoInitializationEnabled: demoInitialization,
            CorsOrigins: corsOrigins,
            DatabaseConnectionString:
                configuration.GetConnectionString("MesDatabase") ?? string.Empty);
    }

    private bool RequiredSecretFilesAreAvailable()
    {
        try
        {
            return ExternalSecretFile.ReadOptional(
                    secretsDirectory,
                    "ConnectionStrings__MesDatabase") is not null
                && ExternalSecretFile.ReadOptional(
                    secretsDirectory,
                    "Security__JwtSigningKey") is not null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
