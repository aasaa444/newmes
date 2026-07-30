using Mes.Infrastructure.Security;

namespace Mes.Security.Tests;

/// <summary>以纯规则测试锁定生产部署安全基线的允许与拒绝条件。</summary>
public sealed class ProductionSecurityBaselineTests
{
    [Fact]
    public void SafeProductionConfigurationIsReady()
    {
        var context = new ProductionSecurityContext(
            RuntimeEnvironment: "Production",
            DeploymentMode: "Production",
            ExternalHttpsOnly: true,
            SecretsSource: "ExternalFiles",
            ExternalSecretsVerified: true,
            SigningKey: "mR7fQ2xL9vN4cK8pT6wY3zB5dH1sJ0uG",
            DemoInitializationEnabled: false,
            CorsOrigins: ["https://mes.factory.local"],
            DatabaseConnectionString:
                "Server=sqlserver;Database=NewMes;User Id=mes_app;Password=external;TrustServerCertificate=True");

        var result = ProductionSecurityBaseline.Evaluate(context);

        Assert.True(result.IsReady);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void UnsafeProductionConfigurationReportsEveryBlockingCodeWithoutSecrets()
    {
        const string sampleKey = "DemoSecret_ThisMustNeverAppearInDiagnostics";
        const string databasePassword = "DatabasePasswordMustStayPrivate";
        var context = new ProductionSecurityContext(
            RuntimeEnvironment: "Development",
            DeploymentMode: "Production",
            ExternalHttpsOnly: false,
            SecretsSource: "Environment",
            ExternalSecretsVerified: false,
            SigningKey: sampleKey,
            DemoInitializationEnabled: true,
            CorsOrigins: ["*", "http://mes.factory.local"],
            DatabaseConnectionString:
                $"Server=sqlserver;Database=NewMes;User Id=sa;Password={databasePassword};TrustServerCertificate=True");

        var result = ProductionSecurityBaseline.Evaluate(context);

        Assert.False(result.IsReady);
        Assert.Contains(result.Violations, violation =>
            violation.Code == "SEC_ENVIRONMENT_NOT_PRODUCTION");
        Assert.Contains(result.Violations, violation =>
            violation.Code == "SEC_EXTERNAL_HTTPS_REQUIRED");
        Assert.Contains(result.Violations, violation =>
            violation.Code == "SEC_EXTERNAL_SECRETS_REQUIRED");
        Assert.Contains(result.Violations, violation =>
            violation.Code == "SEC_EXTERNAL_SECRET_FILES_UNVERIFIED");
        Assert.Contains(result.Violations, violation =>
            violation.Code == "SEC_SAMPLE_SIGNING_KEY");
        Assert.Contains(result.Violations, violation =>
            violation.Code == "SEC_DEMO_INITIALIZATION_ENABLED");
        Assert.Contains(result.Violations, violation =>
            violation.Code == "SEC_CORS_WILDCARD");
        Assert.Contains(result.Violations, violation =>
            violation.Code == "SEC_CORS_INSECURE_ORIGIN");
        Assert.Contains(result.Violations, violation =>
            violation.Code == "SEC_DATABASE_ADMIN_LOGIN");
        Assert.All(result.Violations, violation =>
        {
            Assert.DoesNotContain(sampleKey, violation.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(databasePassword, violation.Message, StringComparison.Ordinal);
        });
    }
}
