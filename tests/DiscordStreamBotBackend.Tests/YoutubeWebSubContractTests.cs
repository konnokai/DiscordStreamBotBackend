using DiscordStreamBotBackend.YoutubeWebSub;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DiscordStreamBotBackend.Tests;

/// <summary>
/// WebSub 契約與 callback 決策測試（計畫 §12.2）。Bot 有另一份等價契約測試，
/// 兩邊以相同測試向量鎖住衍生結果與 JSON 欄位；需要 Redis／MySQL 的整合行為列在計畫手動驗證。
/// </summary>
public sealed class YoutubeWebSubContractTests
{
    private const string ChannelId = "UCabcdefghijklmnopqrstuv";
    private const string OtherChannelId = "UCzyxwvutsrqponmlkjihgfe";

    [Fact]
    public void CallbackTokenMatchesSharedVectorAcrossRepositories()
    {
        // 與 Bot 的 DiscordStreamNotifyBot.Tests.YoutubeWebSubContractTests 相同的固定向量。
        Assert.Equal("EjDM6cKhgaOC03v0EVmyG7elV6uPVNqTM29h4wyfMT0", YoutubeWebSubContract.DeriveCallbackToken("test-secret"));
    }

    [Theory]
    [InlineData("UCabcdefghijklmnopqrstuv", true)]
    [InlineData("UCabcdefghijklmnopqrstu-", true)]
    [InlineData("UCabcdefghijklmnopqrstu_", true)]
    [InlineData("", false)]
    [InlineData("UCabcdefghijklmnopqrstu", false)]
    [InlineData("UCabcdefghijklmnopqrstuvw", false)]
    [InlineData("XXabcdefghijklmnopqrstuv", false)]
    [InlineData("UCabcdefghijklmnopqrstu:", false)]
    public void ChannelIdValidationIsStrict(string channelId, bool expected)
        => Assert.Equal(expected, YoutubeWebSubContract.IsValidChannelId(channelId));

    [Fact]
    public void TopicExtractionRequiresCanonicalUrl()
    {
        Assert.Equal(ChannelId, YoutubeWebSubContract.ExtractChannelIdFromTopic(
            "https://www.youtube.com/feeds/videos.xml?channel_id=" + ChannelId));
        Assert.Equal(ChannelId, YoutubeWebSubContract.ExtractChannelIdFromTopic(
            "https://www.youtube.com/feeds/videos.xml?channel_id=" + ChannelId + "&x=1"));
        Assert.Null(YoutubeWebSubContract.ExtractChannelIdFromTopic(
            "https://www.youtube.com/xml/feeds/videos.xml?channel_id=" + ChannelId));
        Assert.Null(YoutubeWebSubContract.ExtractChannelIdFromTopic("https://example.com/?channel_id=" + ChannelId));
        Assert.Null(YoutubeWebSubContract.ExtractChannelIdFromTopic(null!));
    }

    [Theory]
    [InlineData("application/atom+xml", true)]
    [InlineData("application/atom+xml; charset=utf-8", true)]
    [InlineData("Application/Atom+XML", true)]
    [InlineData("application/json", false)]
    [InlineData("text/plain; application/atom+xml", false)]
    [InlineData("", false)]
    public void ContentTypeUsesMediaTypeComparison(string contentType, bool expected)
        => Assert.Equal(expected, YoutubeWebSubContract.IsAtomContentType(contentType));

    [Fact]
    public void PendingActionJsonMatchesBotContract()
    {
        var action = new YoutubeWebSubPendingAction
        {
            ChannelId = ChannelId,
            Mode = YoutubeWebSubContract.ModeSubscribe,
            Topic = YoutubeWebSubContract.TopicPrefix + ChannelId,
            CallbackToken = "token-value",
            RequestedAtUtc = new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
        };
        string raw = Newtonsoft.Json.JsonConvert.SerializeObject(action);
        var json = JObject.Parse(raw);

        Assert.Equal(
            ["callbackToken", "channelId", "confirmedAtUtc", "deniedAtUtc", "mode", "requestedAtUtc", "topic", "version"],
            json.Properties().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(1, json.Value<int>("version"));
        Assert.Equal(JTokenType.Null, json["confirmedAtUtc"]!.Type);
        Assert.Equal(JTokenType.Null, json["deniedAtUtc"]!.Type);
        // JObject.Parse 會把 ISO 日期轉成 DateTime 後以目前文化格式化，因此直接比對原始字串。
        Assert.Contains("\"requestedAtUtc\":\"2026-09-19T00:00:00Z\"", raw, StringComparison.Ordinal);
        Assert.Equal(YoutubeWebSubContract.TopicPrefix + ChannelId, json.Value<string>("topic"));
    }

    [Fact]
    public void PendingActionRoundTripsThroughJson()
    {
        var action = new YoutubeWebSubPendingAction
        {
            ChannelId = ChannelId,
            Mode = YoutubeWebSubContract.ModeUnsubscribe,
            Topic = YoutubeWebSubContract.TopicPrefix + ChannelId,
            CallbackToken = "token-value",
            RequestedAtUtc = new DateTime(2026, 9, 19, 1, 2, 3, DateTimeKind.Utc),
        };

        Assert.True(YoutubeWebSubPendingAction.TryParse(Newtonsoft.Json.JsonConvert.SerializeObject(action), out var parsed, out var error));
        Assert.Null(error);
        Assert.Equal(ChannelId, parsed!.ChannelId);
        Assert.Equal(DateTimeKind.Utc, parsed.RequestedAtUtc.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{\"version\":9}")]
    [InlineData("{\"version\":1,\"channelId\":\"bad\",\"mode\":\"subscribe\",\"topic\":\"t\",\"callbackToken\":\"t\",\"requestedAtUtc\":\"2026-09-19T00:00:00Z\"}")]
    public void PendingActionRejectsUnusablePayload(string json)
        => Assert.False(YoutubeWebSubPendingAction.TryParse(json, out _, out _));

    [Fact]
    public void DiagnosticSummaryRedactsReflectedTokens()
    {
        const string token = "callback-token-value";
        string reason = $"denied for https://api.example.com/NotificationCallback?channelId={ChannelId}&token={token}";

        string summary = YoutubeWebSubContract.Summarize(reason, sensitiveValues: [token]);

        Assert.DoesNotContain(token, summary, StringComparison.Ordinal);
        Assert.Contains("[redacted]", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FixedTimeEqualsRejectsNullOrDifferentValues()
    {
        Assert.True(YoutubeWebSubContract.FixedTimeEquals("abc", "abc"));
        Assert.False(YoutubeWebSubContract.FixedTimeEquals("abc", "abd"));
        Assert.False(YoutubeWebSubContract.FixedTimeEquals("abc", "abcd"));
        Assert.False(YoutubeWebSubContract.FixedTimeEquals(null, "abc"));
        Assert.False(YoutubeWebSubContract.FixedTimeEquals("abc", null));
    }

    [Fact]
    public void SignatureHeaderRequiresKnownAlgorithmAndHex()
    {
        Assert.True(YoutubeWebSubCallbackPolicy.TryReadSignature("sha1=0123456789abcdef0123456789abcdef01234567", out var algorithm, out var signature));
        Assert.Equal("sha1", algorithm);
        Assert.Equal(20, signature!.Length);

        Assert.True(YoutubeWebSubCallbackPolicy.TryReadSignature("sha256=" + new string('a', 64), out algorithm, out signature));
        Assert.Equal("sha256", algorithm);
        Assert.Equal(32, signature!.Length);

        Assert.False(YoutubeWebSubCallbackPolicy.TryReadSignature("md5=0123456789abcdef0123456789abcdef", out _, out _));
        Assert.False(YoutubeWebSubCallbackPolicy.TryReadSignature("sha1=zzzz", out _, out _));
        Assert.False(YoutubeWebSubCallbackPolicy.TryReadSignature("sha1", out _, out _));
        Assert.False(YoutubeWebSubCallbackPolicy.TryReadSignature(null!, out _, out _));
    }

    [Fact]
    public void HmacIsComputedOverRawBytes()
    {
        byte[] body = Encoding.UTF8.GetBytes("<feed></feed>");
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes("secret"));
        byte[] signature = hmac.ComputeHash(body);

        Assert.True(YoutubeWebSubCallbackPolicy.VerifySignature(body, "secret", "sha1", signature));

        byte[] tampered = (byte[])body.Clone();
        tampered[0] = (byte)'<';
        tampered[1] = (byte)'F';
        Assert.False(YoutubeWebSubCallbackPolicy.VerifySignature(tampered, "secret", "sha1", signature));
        Assert.False(YoutubeWebSubCallbackPolicy.VerifySignature(body, "other-secret", "sha1", signature));
    }

    [Fact]
    public void AtomPayloadIsParsedInsideKnownNamespaces()
    {
        var notification = YoutubeWebSubCallbackPolicy.ParseAtom(Encoding.UTF8.GetBytes(CreateOrUpdatePayload()));

        Assert.NotNull(notification);
        Assert.Equal(YoutubePubSubNotification.YTNotificationType.CreateOrUpdated, notification!.NotificationType);
        Assert.Equal("dQw4w9WgXcQ", notification.VideoId);
        Assert.Equal(ChannelId, notification.ChannelId);
        Assert.Equal("Video title", notification.Title);
        Assert.Equal("http://www.youtube.com/watch?v=dQw4w9WgXcQ", notification.Link);
        Assert.Equal(new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc), notification.Published);
    }

    [Fact]
    public void DeletedPayloadIsParsedAsDeletedEntry()
    {
        var notification = YoutubeWebSubCallbackPolicy.ParseAtom(Encoding.UTF8.GetBytes(DeletedPayload()));

        Assert.NotNull(notification);
        Assert.Equal(YoutubePubSubNotification.YTNotificationType.Deleted, notification!.NotificationType);
        Assert.Equal("dQw4w9WgXcQ", notification.VideoId);
        Assert.Equal(ChannelId, notification.ChannelId);
        Assert.Equal(new DateTime(2026, 9, 19, 3, 0, 0, DateTimeKind.Utc), notification.Published);
    }

    [Fact]
    public void MinifiedPayloadWithoutWhitespaceIsParsed()
    {
        string payload = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<feed xmlns:yt=\"http://www.youtube.com/xml/schemas/2015\" xmlns=\"http://www.w3.org/2005/Atom\">"
            + "<entry><yt:videoId>dQw4w9WgXcQ</yt:videoId>"
            + $"<yt:channelId>{ChannelId}</yt:channelId>"
            + "<title>Video title</title>"
            + "<published>2026-09-18T12:00:00+00:00</published>"
            + "<updated>2026-09-19T03:00:00+00:00</updated></entry></feed>";

        var notification = YoutubeWebSubCallbackPolicy.ParseAtom(Encoding.UTF8.GetBytes(payload));

        Assert.NotNull(notification);
        Assert.Equal("dQw4w9WgXcQ", notification!.VideoId);
        Assert.Equal(ChannelId, notification.ChannelId);
        Assert.Equal("Video title", notification.Title);
    }

    [Fact]
    public void DtdAndUnknownContentAreNotParsed()
    {
        string xxe = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE feed [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <feed xmlns="http://www.w3.org/2005/Atom"><title>&xxe;</title></feed>
            """;

        Assert.Null(YoutubeWebSubCallbackPolicy.ParseAtom(Encoding.UTF8.GetBytes(xxe)));
        Assert.Null(YoutubeWebSubCallbackPolicy.ParseAtom(Encoding.UTF8.GetBytes("<html/>")));
        Assert.Null(YoutubeWebSubCallbackPolicy.ParseAtom([]));
    }

    [Fact]
    public async Task NotificationWithoutSecretRequestsResubscribeOnlyForKnownCrawlers()
    {
        byte[] body = Encoding.UTF8.GetBytes(CreateOrUpdatePayload());

        // 舊格式（無 query 資訊）缺 secret 時，只有確實是本系統 crawler 才要求重新訂閱。
        var known = await YoutubeWebSubCallbackPolicy.EvaluateNotificationAsync(
            body, "application/atom+xml", Signature(body, "secret"), null, null,
            _ => Task.FromResult<string>(null!), _ => true);
        Assert.Equal(YoutubeWebSubNotificationAction.NeedRegister, known.Action);
        Assert.Equal(ChannelId, known.ChannelId);

        var unknown = await YoutubeWebSubCallbackPolicy.EvaluateNotificationAsync(
            body, "application/atom+xml", Signature(body, "secret"), null, null,
            _ => Task.FromResult<string>(null!), _ => false);
        Assert.Equal(YoutubeWebSubNotificationAction.Ignore, unknown.Action);
    }

    [Fact]
    public async Task NewStyleCallbackWithMissingSecretRequestsResubscribeBeforeParsing()
    {
        byte[] body = Encoding.UTF8.GetBytes(CreateOrUpdatePayload());
        string token = YoutubeWebSubContract.DeriveCallbackToken("secret");

        var evaluation = await YoutubeWebSubCallbackPolicy.EvaluateNotificationAsync(
            body, "application/atom+xml", Signature(body, "secret"), ChannelId, token,
            _ => Task.FromResult<string>(null!), _ => true);

        Assert.Equal(YoutubeWebSubNotificationAction.NeedRegister, evaluation.Action);
        Assert.Equal(ChannelId, evaluation.ChannelId);
        Assert.Null(evaluation.Notification);
    }

    [Theory]
    [InlineData(ChannelId, null)]
    [InlineData(null, "some-token")]
    public async Task ChannelIdAndTokenMustBeProvidedTogether(string? queryChannelId, string? token)
    {
        byte[] body = Encoding.UTF8.GetBytes(CreateOrUpdatePayload());

        var evaluation = await YoutubeWebSubCallbackPolicy.EvaluateNotificationAsync(
            body, "application/atom+xml", Signature(body, "secret"), queryChannelId, token,
            _ => Task.FromResult("secret"), _ => true);

        Assert.Equal(YoutubeWebSubNotificationAction.Ignore, evaluation.Action);
        Assert.Contains("必須同時提供", evaluation.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidSignatureIsIgnoredAndNeverRequestsResubscribe()
    {
        byte[] body = Encoding.UTF8.GetBytes(CreateOrUpdatePayload());

        var evaluation = await YoutubeWebSubCallbackPolicy.EvaluateNotificationAsync(
            body, "application/atom+xml", Signature(body, "wrong-secret"), null, null,
            _ => Task.FromResult("secret"), _ => true);

        Assert.Equal(YoutubeWebSubNotificationAction.Ignore, evaluation.Action);
    }

    [Fact]
    public async Task ValidSignatureWithContentTypeParametersPublishes()
    {
        byte[] body = Encoding.UTF8.GetBytes(CreateOrUpdatePayload());

        var evaluation = await YoutubeWebSubCallbackPolicy.EvaluateNotificationAsync(
            body, "application/atom+xml; charset=utf-8", Signature(body, "secret"), ChannelId,
            YoutubeWebSubContract.DeriveCallbackToken("secret"),
            _ => Task.FromResult("secret"), _ => true);

        Assert.Equal(YoutubeWebSubNotificationAction.Publish, evaluation.Action);
        Assert.Equal("dQw4w9WgXcQ", evaluation.Notification!.VideoId);
    }

    [Fact]
    public async Task InvalidCallbackTokenIsIgnored()
    {
        byte[] body = Encoding.UTF8.GetBytes(CreateOrUpdatePayload());

        var evaluation = await YoutubeWebSubCallbackPolicy.EvaluateNotificationAsync(
            body, "application/atom+xml", Signature(body, "secret"), ChannelId, "not-the-token",
            _ => Task.FromResult("secret"), _ => true);

        Assert.Equal(YoutubeWebSubNotificationAction.Ignore, evaluation.Action);
    }

    [Fact]
    public async Task LegacyCallbackWithoutTokenStillNeedsValidHmac()
    {
        byte[] body = Encoding.UTF8.GetBytes(CreateOrUpdatePayload());

        var valid = await YoutubeWebSubCallbackPolicy.EvaluateNotificationAsync(
            body, "application/atom+xml", Signature(body, "secret"), null, null,
            _ => Task.FromResult("secret"), _ => true);
        Assert.Equal(YoutubeWebSubNotificationAction.Publish, valid.Action);

        var mismatchedChannel = await YoutubeWebSubCallbackPolicy.EvaluateNotificationAsync(
            body, "application/atom+xml", Signature(body, "secret"), OtherChannelId, null,
            _ => Task.FromResult("secret"), _ => true);
        Assert.Equal(YoutubeWebSubNotificationAction.Ignore, mismatchedChannel.Action);
    }

    [Fact]
    public async Task ChallengeAcceptsConfirmedPendingOnlyOnce()
    {
        var pending = CreatePending(confirmed: false);
        string token = YoutubeWebSubContract.DeriveCallbackToken("secret");

        var first = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "subscribe", pending.Topic, "challenge-value", "864000", ChannelId, token,
            pending, "secret", () => Task.FromResult(true));
        Assert.Equal(YoutubeWebSubChallengeAction.ConfirmSubscribe, first.Action);
        Assert.Equal(TimeSpan.FromSeconds(864000), first.LeaseTtl);

        pending.ConfirmedAtUtc = DateTime.UtcNow;
        var retry = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "subscribe", pending.Topic, "challenge-value", "864000", ChannelId, token,
            pending, "secret", () => Task.FromResult(true));
        Assert.Equal(YoutubeWebSubChallengeAction.AlreadyConfirmed, retry.Action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OtherChannelId)]
    public async Task ChallengeRejectsMissingOrUnknownPending(string? queryChannelId)
    {
        var pending = CreatePending(confirmed: false);
        string token = YoutubeWebSubContract.DeriveCallbackToken("secret");

        var evaluation = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "subscribe", pending.Topic, "challenge-value", "864000", queryChannelId, token,
            null, "secret", () => Task.FromResult(true));

        Assert.Equal(YoutubeWebSubChallengeAction.NotFound, evaluation.Action);
    }

    [Fact]
    public async Task ChallengeRequiresChannelIdQueryEvenWithValidPending()
    {
        var pending = CreatePending(confirmed: false);
        string token = YoutubeWebSubContract.DeriveCallbackToken("secret");

        var evaluation = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "subscribe", pending.Topic, "challenge-value", "864000", null, token,
            pending, "secret", () => Task.FromResult(true));

        Assert.Equal(YoutubeWebSubChallengeAction.NotFound, evaluation.Action);

        var denied = YoutubeWebSubCallbackPolicy.EvaluateDenied(pending.Topic, null, token, pending);
        Assert.Equal(YoutubeWebSubChallengeAction.NotFound, denied.Action);
    }

    [Fact]
    public async Task ChallengeRejectsModeTopicAndTokenMismatch()
    {
        var pending = CreatePending(confirmed: false);
        string token = YoutubeWebSubContract.DeriveCallbackToken("secret");

        var wrongMode = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "unsubscribe", pending.Topic, "challenge-value", null, ChannelId, token,
            pending, "secret", () => Task.FromResult(true));
        Assert.Equal(YoutubeWebSubChallengeAction.NotFound, wrongMode.Action);

        var wrongTopic = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "subscribe", YoutubeWebSubContract.TopicPrefix + OtherChannelId, "challenge-value", "864000", null, token,
            pending, "secret", () => Task.FromResult(true));
        Assert.Equal(YoutubeWebSubChallengeAction.NotFound, wrongTopic.Action);

        var wrongToken = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "subscribe", pending.Topic, "challenge-value", "864000", ChannelId, "wrong-token",
            pending, "secret", () => Task.FromResult(true));
        Assert.Equal(YoutubeWebSubChallengeAction.NotFound, wrongToken.Action);
    }

    [Fact]
    public async Task ChallengeKeepsPendingWhenSecretIsMissingOrCrawlerIsGone()
    {
        var pending = CreatePending(confirmed: false);
        string token = YoutubeWebSubContract.DeriveCallbackToken("secret");

        var missingSecret = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "subscribe", pending.Topic, "challenge-value", "864000", ChannelId, token,
            pending, null, () => Task.FromResult(true));
        Assert.Equal(YoutubeWebSubChallengeAction.ServerError, missingSecret.Action);

        var goneCrawler = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "subscribe", pending.Topic, "challenge-value", "864000", ChannelId, token,
            pending, "secret", () => Task.FromResult(false));
        Assert.Equal(YoutubeWebSubChallengeAction.NotFound, goneCrawler.Action);
    }

    [Fact]
    public async Task ChallengeWithoutLeaseSecondsIsRejected()
    {
        var pending = CreatePending(confirmed: false);
        string token = YoutubeWebSubContract.DeriveCallbackToken("secret");

        var evaluation = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "subscribe", pending.Topic, "challenge-value", null, ChannelId, token,
            pending, "secret", () => Task.FromResult(true));

        Assert.Equal(YoutubeWebSubChallengeAction.BadRequest, evaluation.Action);
    }

    [Fact]
    public async Task UnsubscribeChallengeDoesNotRequireSecretOrCrawler()
    {
        var pending = CreatePending(confirmed: false, mode: YoutubeWebSubContract.ModeUnsubscribe);
        string token = YoutubeWebSubContract.DeriveCallbackToken("secret");

        var evaluation = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
            "unsubscribe", pending.Topic, "challenge-value", null, ChannelId, token,
            pending, null, () => Task.FromResult(false));

        Assert.Equal(YoutubeWebSubChallengeAction.ConfirmUnsubscribe, evaluation.Action);
        Assert.Null(evaluation.LeaseTtl);
    }

    [Fact]
    public void DeniedCallbackMarksPendingWithoutSecret()
    {
        var pending = CreatePending(confirmed: false);
        string token = YoutubeWebSubContract.DeriveCallbackToken("secret");

        Assert.Equal(YoutubeWebSubChallengeAction.ConfirmDenied,
            YoutubeWebSubCallbackPolicy.EvaluateDenied(pending.Topic, ChannelId, token, pending).Action);

        pending.DeniedAtUtc = DateTime.UtcNow;
        Assert.Equal(YoutubeWebSubChallengeAction.AlreadyConfirmed,
            YoutubeWebSubCallbackPolicy.EvaluateDenied(pending.Topic, ChannelId, token, pending).Action);
    }

    [Fact]
    public void DeniedCallbackRejectsUnknownPendingOrToken()
    {
        var pending = CreatePending(confirmed: false);
        string token = YoutubeWebSubContract.DeriveCallbackToken("secret");

        Assert.Equal(YoutubeWebSubChallengeAction.NotFound,
            YoutubeWebSubCallbackPolicy.EvaluateDenied(pending.Topic, ChannelId, token, null).Action);
        Assert.Equal(YoutubeWebSubChallengeAction.NotFound,
            YoutubeWebSubCallbackPolicy.EvaluateDenied(pending.Topic, ChannelId, "wrong", pending).Action);
        Assert.Equal(YoutubeWebSubChallengeAction.NotFound,
            YoutubeWebSubCallbackPolicy.EvaluateDenied(YoutubeWebSubContract.TopicPrefix + OtherChannelId, null, token, pending).Action);
        Assert.Equal(YoutubeWebSubChallengeAction.BadRequest,
            YoutubeWebSubCallbackPolicy.EvaluateDenied(null!, ChannelId, token, pending).Action);
    }

    private static YoutubeWebSubPendingAction CreatePending(bool confirmed, string mode = YoutubeWebSubContract.ModeSubscribe)
        => new()
        {
            ChannelId = ChannelId,
            Mode = mode,
            Topic = YoutubeWebSubContract.TopicPrefix + ChannelId,
            CallbackToken = YoutubeWebSubContract.DeriveCallbackToken("secret"),
            RequestedAtUtc = DateTime.UtcNow,
            ConfirmedAtUtc = confirmed ? DateTime.UtcNow : null,
        };

    private static string Signature(byte[] body, string secret)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        return "sha1=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    private static string CreateOrUpdatePayload()
        => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns="http://www.w3.org/2005/Atom">
              <link rel="self" href="https://www.youtube.com/feeds/videos.xml?channel_id={ChannelId}"/>
              <title>YouTube video feed</title>
              <entry>
                <id>yt:video:dQw4w9WgXcQ</id>
                <yt:videoId>dQw4w9WgXcQ</yt:videoId>
                <yt:channelId>{ChannelId}</yt:channelId>
                <title>Video title</title>
                <link rel="alternate" href="http://www.youtube.com/watch?v=dQw4w9WgXcQ"/>
                <published>2026-09-18T12:00:00+00:00</published>
                <updated>2026-09-19T03:00:00+00:00</updated>
              </entry>
            </feed>
            """;

    private static string DeletedPayload()
        => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:at="http://purl.org/atompub/tombstones/1.0">
              <at:deleted-entry ref="yt:video:dQw4w9WgXcQ" when="2026-09-19T03:00:00+00:00">
                <at:by>
                  <name>Channel title</name>
                  <uri>https://www.youtube.com/channel/{ChannelId}</uri>
                </at:by>
              </at:deleted-entry>
            </feed>
            """;
}
