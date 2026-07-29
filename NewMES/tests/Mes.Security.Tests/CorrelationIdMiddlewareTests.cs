using Mes.Api.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Mes.Security.Tests;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task RequestCorrelationIdIsEchoedWithoutLoggingAuthorization()
    {
        const string correlationId = "factory-request-001";
        const string bearerToken = "Bearer token-must-not-be-logged";
        var logger = new CaptureLogger();
        var accessor = new CorrelationContextAccessor();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/orders";
        context.Request.Headers.Authorization = bearerToken;
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = correlationId;
        var middleware = new CorrelationIdMiddleware(
            _ =>
            {
                Assert.Equal(correlationId, accessor.CorrelationId);
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context, accessor);

        Assert.Equal(correlationId, context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
        Assert.Contains(logger.Messages, message => message.Contains(
            correlationId,
            StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(
            bearerToken,
            StringComparison.Ordinal));
    }

    private sealed class CaptureLogger : ILogger<CorrelationIdMiddleware>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            Messages.Add(string.Join(
                ",",
                ((IEnumerable<KeyValuePair<string, object>>)state)
                    .Select(item => $"{item.Key}={item.Value}")));
            return EmptyScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class EmptyScope : IDisposable
        {
            public static EmptyScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
