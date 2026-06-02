using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Schleusenwerk.Forwarding;
using Xunit;

namespace Schleusenwerk.Tests.Forwarding;

public sealed class AccessLogMiddlewareSpec
{
    [Fact(Timeout = 5000)]
    public async Task AccessLog_should_log_request_details()
    {
        var logger = new CapturingLogger();
        var middleware = new AccessLogMiddleware(logger);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/test";
        context.Request.Host = new HostString("example.com");
        context.Request.QueryString = new QueryString("?q=1");
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.1");
        context.Request.Headers.UserAgent = "TestAgent/1.0";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength = 42;
            return Task.CompletedTask;
        });

        Assert.Single(logger.Entries);
        var entry = logger.Entries[0];
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("GET", entry.Message);
        Assert.Contains("/api/test?q=1", entry.Message);
        Assert.Contains("200", entry.Message);
        Assert.Contains("example.com", entry.Message);
        Assert.Contains("192.168.1.1", entry.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task AccessLog_should_log_upstream_from_context_items()
    {
        var logger = new CapturingLogger();
        var middleware = new AccessLogMiddleware(logger);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/data";
        context.Request.Host = new HostString("test.com");
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, ctx =>
        {
            ctx.Items["Upstream"] = "http://backend:8080";
            ctx.Response.StatusCode = 201;
            return Task.CompletedTask;
        });

        Assert.Single(logger.Entries);
        Assert.Contains("http://backend:8080", logger.Entries[0].Message);
    }

    [Fact(Timeout = 5000)]
    public async Task AccessLog_should_log_even_when_next_throws()
    {
        var logger = new CapturingLogger();
        var middleware = new AccessLogMiddleware(logger);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/fail";
        context.Request.Host = new HostString("error.com");
        context.Response.Body = new MemoryStream();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context, _ => throw new InvalidOperationException("boom")));

        Assert.Single(logger.Entries);
        Assert.Contains("500", logger.Entries[0].Message);
    }

    internal sealed class CapturingLogger : ILogger<AccessLogMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
