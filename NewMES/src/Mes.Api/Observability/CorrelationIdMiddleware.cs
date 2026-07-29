using System.Diagnostics;

namespace Mes.Api.Observability;

public sealed partial class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(
        HttpContext context,
        CorrelationContextAccessor correlationContext)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsSafeCorrelationId(supplied)
            ? supplied
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        correlationContext.CorrelationId = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
        });
        LogRequestStarted(logger, context.Request.Method, context.Request.Path);
        try
        {
            await next(context);
            LogRequestCompleted(logger, context.Response.StatusCode);
        }
        catch (Exception exception)
        {
            LogRequestFailed(logger, exception);
            throw;
        }
    }

    private static bool IsSafeCorrelationId(string value)
    {
        if (value.Length is < 1 or > 64)
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.');
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Request started. Method={Method}; Path={Path}")]
    private static partial void LogRequestStarted(
        ILogger logger,
        string method,
        PathString path);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Request completed. StatusCode={StatusCode}")]
    private static partial void LogRequestCompleted(ILogger logger, int statusCode);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Request failed.")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception);
}
