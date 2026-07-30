namespace Mes.Infrastructure.Security;

/// <summary>生产环境只接受非回环 HTTPS 来源，且任何环境都拒绝通配符来源。</summary>
public static class ProductionCorsPolicy
{
    public static bool IsAllowedOrigin(string origin, bool isProduction)
    {
        if (string.Equals(origin, "*", StringComparison.Ordinal)
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return !isProduction
            || (string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                && !uri.IsLoopback);
    }
}
