using System.Data.Common;

namespace Mes.Infrastructure.Security;

public sealed record ProductionSecurityContext(
    string RuntimeEnvironment,
    string DeploymentMode,
    bool ExternalHttpsOnly,
    string? SecretsSource,
    string? SigningKey,
    bool? DemoInitializationEnabled,
    IReadOnlyList<string> CorsOrigins,
    string DatabaseConnectionString);

public sealed record SecurityViolation(string Code, string Message);

public sealed record ProductionSecurityResult(
    bool IsReady,
    IReadOnlyList<SecurityViolation> Violations);

public static class ProductionSecurityBaseline
{
    private static readonly string[] SampleKeyMarkers =
    [
        "changeme",
        "demo",
        "example",
        "localonly",
        "placeholder",
        "sample",
        "secret",
    ];

    public static ProductionSecurityResult Evaluate(ProductionSecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var violations = new List<SecurityViolation>();

        AddIf(
            violations,
            !string.Equals(context.RuntimeEnvironment, "Production", StringComparison.Ordinal),
            "SEC_ENVIRONMENT_NOT_PRODUCTION",
            "The runtime environment must be Production for a production deployment.");
        AddIf(
            violations,
            !string.Equals(context.DeploymentMode, "Production", StringComparison.Ordinal),
            "SEC_DEPLOYMENT_MODE_NOT_PRODUCTION",
            "Security:DeploymentMode must explicitly select Production.");
        AddIf(
            violations,
            !context.ExternalHttpsOnly,
            "SEC_EXTERNAL_HTTPS_REQUIRED",
            "The production entry point must require HTTPS.");
        AddIf(
            violations,
            !string.Equals(context.SecretsSource, "ExternalFiles", StringComparison.Ordinal),
            "SEC_EXTERNAL_SECRETS_REQUIRED",
            "Production secrets must be loaded from external files.");

        EvaluateSigningKey(context.SigningKey, violations);

        AddIf(
            violations,
            context.DemoInitializationEnabled is not false,
            "SEC_DEMO_INITIALIZATION_ENABLED",
            "Demo initialization must be explicitly disabled in production.");

        foreach (var origin in context.CorsOrigins)
        {
            if (string.Equals(origin, "*", StringComparison.Ordinal))
            {
                Add(
                    violations,
                    "SEC_CORS_WILDCARD",
                    "Wildcard CORS is forbidden in production.");
                continue;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || uri.IsLoopback)
            {
                Add(
                    violations,
                    "SEC_CORS_INSECURE_ORIGIN",
                    "Production CORS origins must be non-loopback HTTPS origins.");
            }
        }

        EvaluateDatabaseLogin(context.DatabaseConnectionString, violations);

        return new ProductionSecurityResult(violations.Count == 0, violations);
    }

    private static void EvaluateSigningKey(
        string? signingKey,
        ICollection<SecurityViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
        {
            Add(
                violations,
                "SEC_SIGNING_KEY_MISSING_OR_WEAK",
                "The production signing key must contain at least 32 characters.");
            return;
        }

        if (SampleKeyMarkers.Any(marker => signingKey.Contains(
                marker,
                StringComparison.OrdinalIgnoreCase)))
        {
            Add(
                violations,
                "SEC_SAMPLE_SIGNING_KEY",
                "A sample signing key marker was detected.");
        }
    }

    private static void EvaluateDatabaseLogin(
        string connectionString,
        ICollection<SecurityViolation> violations)
    {
        try
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString,
            };
            var userId = FindValue(builder, "User Id", "UID", "UserID");
            if (string.Equals(userId, "sa", StringComparison.OrdinalIgnoreCase))
            {
                Add(
                    violations,
                    "SEC_DATABASE_ADMIN_LOGIN",
                    "The API database connection cannot use the SQL Server administrator login.");
            }
        }
        catch (ArgumentException)
        {
            Add(
                violations,
                "SEC_DATABASE_CONNECTION_INVALID",
                "The API database connection configuration is invalid.");
        }
    }

    private static string? FindValue(
        DbConnectionStringBuilder builder,
        params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (builder.TryGetValue(alias, out var value))
            {
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static void AddIf(
        ICollection<SecurityViolation> violations,
        bool condition,
        string code,
        string message)
    {
        if (condition)
        {
            Add(violations, code, message);
        }
    }

    private static void Add(
        ICollection<SecurityViolation> violations,
        string code,
        string message)
    {
        if (violations.All(violation => violation.Code != code))
        {
            violations.Add(new SecurityViolation(code, message));
        }
    }
}
