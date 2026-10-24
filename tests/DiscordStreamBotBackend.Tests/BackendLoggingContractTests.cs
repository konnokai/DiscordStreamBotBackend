using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Net;
using System.Text.RegularExpressions;

namespace DiscordStreamBotBackend.Tests;

public sealed class BackendLoggingContractTests
{
    // 與 Bot 的 Log.LogLinePattern 相同；兩邊格式一致，Docker log 與既有解析工具才能共用。
    private const string BotLogLinePattern = @"^\[(?<timestamp>\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2})\] (?:\[(?<role>[^\]]+)\] )?\[(?<level>TRACE|DEBUG|INFO|WARN|ERROR|CRITICAL)\] \| (?<message>.*)$";

    [Fact]
    public void ConsoleLineMatchesBotLogLineFormat()
    {
        var events = Capture(logger => logger.Information("測試 {Value}", "abc"));

        var match = Regex.Match(FirstConsoleLine(Assert.Single(events)), BotLogLinePattern);

        Assert.True(match.Success);
        Assert.Equal("backend", match.Groups["role"].Value);
        Assert.Equal("INFO", match.Groups["level"].Value);
        Assert.Equal("測試 abc", match.Groups["message"].Value);
    }

    [Fact]
    public void ExceptionIsWrittenAfterTheFirstLine()
    {
        var events = Capture(logger => logger.Error(new InvalidOperationException("壞掉了"), "處理失敗"));

        var output = Format(Assert.Single(events));

        Assert.Matches(BotLogLinePattern, FirstConsoleLine(events[0]));
        Assert.Contains("[ERROR] | 處理失敗", output);
        Assert.Contains("InvalidOperationException: 壞掉了", output);
    }

    [Theory]
    [InlineData(LogEventLevel.Information, "INFO")]
    [InlineData(LogEventLevel.Warning, "WARN")]
    [InlineData(LogEventLevel.Error, "ERROR")]
    [InlineData(LogEventLevel.Fatal, "CRITICAL")]
    public void LokiLevelLabelUsesBotLevelNames(LogEventLevel level, string expected)
    {
        var events = Capture(logger => logger.ForContext("SourceContext", "DiscordStreamBotBackend.Test").Write(level, "x"));

        var value = Assert.IsType<ScalarValue>(Assert.Single(events).Properties[BackendLogging.LevelProperty]);
        Assert.Equal(expected, value.Value);
    }

    [Fact]
    public void FrameworkInfoIsDroppedButWarningsAreKept()
    {
        var events = Capture(logger =>
        {
            logger.ForContext("SourceContext", "Microsoft.AspNetCore.Hosting.Diagnostics").Information("request");
            logger.ForContext("SourceContext", "System.Net.Http.HttpClient.Default.LogicalHandler").Information("http");
            logger.ForContext("SourceContext", "Microsoft.AspNetCore.Server.Kestrel").Warning("kestrel");
        });

        Assert.Equal("kestrel", Assert.Single(events).RenderMessage());
    }

    [Theory]
    [InlineData("http://loki:3100", "http://loki:3100")]
    [InlineData("http://loki:3100/", "http://loki:3100")]
    [InlineData("http://loki:3100/loki/api/v1/push", "http://loki:3100")]
    [InlineData("https://example.com/proxy/loki/api/v1/push/", "https://example.com/proxy")]
    public void LokiUrlAcceptsBaseAndPushEndpoint(string url, string expected)
    {
        Assert.True(BackendLogging.TryGetLokiBaseUrl(url, out var baseUrl));
        Assert.Equal(expected, baseUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("loki:3100")]
    [InlineData("ftp://loki:3100")]
    [InlineData("http://loki:3100/?tenant=a")]
    public void InvalidLokiUrlDisablesLoki(string? url)
    {
        Assert.False(BackendLogging.TryGetLokiBaseUrl(url!, out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    public async Task LokiRejectionIsDroppedSoLaterBatchesContinue(HttpStatusCode status)
    {
        using var invoker = new HttpMessageInvoker(new BackendLogging.LokiHttpMessageHandler(TimeSpan.FromSeconds(5), new StubHandler(status)));

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "http://loki/loki/api/v1/push"), CancellationToken.None);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task LokiRetryableFailureIsKeptForRetry(HttpStatusCode status)
    {
        using var invoker = new HttpMessageInvoker(new BackendLogging.LokiHttpMessageHandler(TimeSpan.FromSeconds(5), new StubHandler(status)));

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "http://loki/loki/api/v1/push"), CancellationToken.None);

        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task LokiRequestTimesOut()
    {
        using var invoker = new HttpMessageInvoker(new BackendLogging.LokiHttpMessageHandler(TimeSpan.FromMilliseconds(50), new StubHandler(HttpStatusCode.NoContent, TimeSpan.FromSeconds(30))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "http://loki/loki/api/v1/push"), CancellationToken.None));
    }

    private sealed class StubHandler(HttpStatusCode status, TimeSpan delay = default) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(status);
        }
    }

    private static List<LogEvent> Capture(Action<ILogger> write)
    {
        var sink = new CaptureSink();
        using (var logger = BackendLogging.Configure(new LoggerConfiguration(), lokiUrl: null).WriteTo.Sink(sink).CreateLogger())
            write(logger);
        return sink.Events;
    }

    private static string Format(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        BackendLogging.ConsoleFormatter.Format(logEvent, writer);
        return writer.ToString();
    }

    private static string FirstConsoleLine(LogEvent logEvent)
        => Format(logEvent).Split(Environment.NewLine)[0];

    private sealed class CaptureSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
