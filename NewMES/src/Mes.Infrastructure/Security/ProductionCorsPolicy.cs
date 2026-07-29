namespace Mes.Infrastructure.Security;

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
