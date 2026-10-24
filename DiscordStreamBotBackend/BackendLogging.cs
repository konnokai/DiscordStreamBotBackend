using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Sinks.Grafana.Loki;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend
{
    /// <summary>
    /// 後端的 Serilog 設定。console 格式與 Loki labels 都跟 Bot 的 Log facade 對齊，
    /// 這樣 Bot 的 Loki dashboard 選 service=backend 就能直接查。
    /// </summary>
    internal static class BackendLogging
    {
        internal const string LevelProperty = "level";
        private const string Role = "backend";
        private const string LokiPushPath = "/loki/api/v1/push";

        internal static readonly ITextFormatter ConsoleFormatter = new MessageTemplateTextFormatter(
            "[{Timestamp:yyyy/MM/dd HH:mm:ss}] [" + Role + "] [{" + LevelProperty + "}] | {Message:lj}{NewLine}{Exception}");

        public static LoggerConfiguration Configure(LoggerConfiguration configuration, string lokiUrl)
        {
            configuration
                .MinimumLevel.Information()
                // 沿用原本 nlog.config 的規則：框架與 IHttpClientFactory 的 Info 太吵，只留 Warn 以上
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .Enrich.With<LevelEnricher>()
                .WriteTo.Console(ConsoleFormatter, standardErrorFromLevel: LogEventLevel.Error);

            if (!string.IsNullOrWhiteSpace(lokiUrl) && !TryGetLokiBaseUrl(lokiUrl, out _))
                WriteDiagnostic("LokiUrl 無效，略過 Loki 推送");

            if (TryGetLokiBaseUrl(lokiUrl, out var lokiBaseUrl))
            {
                configuration.WriteTo.GrafanaLoki(
                    lokiBaseUrl,
                    labels: new[]
                    {
                        new LokiLabel { Key = "app", Value = "discord-stream-notify-bot" },
                        new LokiLabel { Key = "service", Value = Role },
                        new LokiLabel { Key = "role", Value = Role }
                    },
                    propertiesAsLabels: new[] { LevelProperty },
                    handleLogLevelAsLabel: false,
                    batchSizeLimit: 100,
                    queueLimit: 10_000,
                    period: TimeSpan.FromSeconds(1),
                    retryTimeLimit: TimeSpan.FromMinutes(10),
                    httpMessageHandler: new LokiHttpMessageHandler(TimeSpan.FromSeconds(5)));
            }

            return configuration;
        }

        /// <summary>Loki 連不到時 flush 會一直重試；設上限，避免容器停止時卡到被 SIGKILL。</summary>
        public static void CloseAndFlush(TimeSpan timeout)
        {
            if (!Task.Run(Log.CloseAndFlush).Wait(timeout))
                WriteDiagnostic($"關閉前送出 Loki log 逾時（{timeout.TotalSeconds:0.###} 秒），剩下的 log 只在 console");
        }

        internal static void WriteDiagnostic(string text)
        {
            try { Console.Error.WriteLine($"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] [{Role}] [WARN] | {text}"); }
            catch { }
        }

        /// <summary>接受 base URL 或完整 push endpoint；sink 會自己補上 push path，所以這裡要去掉。</summary>
        internal static bool TryGetLokiBaseUrl(string url, out string baseUrl)
        {
            baseUrl = null;
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment))
            {
                return false;
            }

            var builder = new UriBuilder(endpoint);
            var path = builder.Path.TrimEnd('/');
            if (path.EndsWith(LokiPushPath, StringComparison.OrdinalIgnoreCase))
                path = path[..^LokiPushPath.Length];
            builder.Path = path;

            baseUrl = builder.Uri.AbsoluteUri.TrimEnd('/');
            return true;
        }

        /// <summary>
        /// Serilog 會一直重試失敗的批次，期間新 log 只能排隊。
        /// 400、413 這類 4xx 重試也不會成功，直接丟掉這批；408、429、5xx 與逾時才交給 Serilog 重試。
        /// </summary>
        internal sealed class LokiHttpMessageHandler : DelegatingHandler
        {
            private readonly TimeSpan _requestTimeout;
            private int _failureReported;

            public LokiHttpMessageHandler(TimeSpan requestTimeout, HttpMessageHandler innerHandler = null)
                : base(innerHandler ?? new HttpClientHandler())
            {
                _requestTimeout = requestTimeout;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_requestTimeout);

                HttpResponseMessage response;
                try
                {
                    response = await base.SendAsync(request, timeoutCts.Token);
                }
                catch (Exception ex)
                {
                    ReportFailure($"Loki 推送失敗，背景重試中：{ex.Message}");
                    throw;
                }

                var statusCode = (int)response.StatusCode;
                if (statusCode >= 400 && statusCode < 500 &&
                    response.StatusCode is not HttpStatusCode.RequestTimeout and not HttpStatusCode.TooManyRequests)
                {
                    response.Dispose();
                    ReportFailure($"Loki 拒絕寫入（HTTP {statusCode}），已丟棄此批");
                    return new HttpResponseMessage(HttpStatusCode.NoContent) { RequestMessage = request };
                }

                if (!response.IsSuccessStatusCode)
                    ReportFailure($"Loki 推送失敗（HTTP {statusCode}），背景重試中");
                else if (Interlocked.Exchange(ref _failureReported, 0) != 0)
                    WriteDiagnostic("Loki 推送已恢復");

                return response;
            }

            // 重試期間每批都會失敗，只在第一次報，恢復時再報一次，避免洗 stderr。
            private void ReportFailure(string message)
            {
                if (Interlocked.Exchange(ref _failureReported, 1) == 0)
                    WriteDiagnostic(message);
            }
        }

        private sealed class LevelEnricher : ILogEventEnricher
        {
            public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
            {
                var level = logEvent.Level switch
                {
                    LogEventLevel.Verbose => "TRACE",
                    LogEventLevel.Debug => "DEBUG",
                    LogEventLevel.Information => "INFO",
                    LogEventLevel.Warning => "WARN",
                    LogEventLevel.Error => "ERROR",
                    LogEventLevel.Fatal => "CRITICAL",
                    _ => "INFO"
                };
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(LevelProperty, level));
            }
        }
    }
}
