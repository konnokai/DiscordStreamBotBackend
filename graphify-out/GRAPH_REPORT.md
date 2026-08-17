# Graph Report - DiscordStreamBotBackend  (2026-08-17)

## Corpus Check
- 69 files · ~22,194 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 821 nodes · 1719 edges · 47 communities
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 95 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `20859de3`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- TwitchAuthorizationService
- RedisService
- .GetAuthorizationAsync
- GoogleOAuthController
- TwitchRefreshRotationLifecycle
- CancellationToken
- GoogleOAuthService
- MySqlDataStore
- BiliBiliGetLiveUserInfoJson.cs
- GoogleAccountLinkService.cs
- DiscordStreamBotBackend.csproj
- TwitchOAuthRefreshLockLease
- EventSubHostedService
- GoogleAccountLinksContractTests
- GoogleJson.cs
- GoogleAccountLinkServiceTests
- CancellationToken
- GoogleOAuthOperationLockLease
- BililiveRecorderWebHookController
- RedisConnection
- Discord Stream Bot Backend
- TwitchAuthorizationService.cs
- GoogleAccountOperationCoordinator
- DiscordStreamBotBackend.Services
- GoogleAccountLink
- AdminSettingsContractTests
- .SuccessfulRevokeTransitionsBeforePublish
- YouTubeNotificationsController
- MySqlDataStore.cs
- RandomVideoController
- YoutubePubSubNotification
- .AddPubMessageAsync
- StartupValidationHostedService
- TwitCastingWebHookController
- LogMiddleware
- .PublishAsync
- .GetManageableGuildsAsync
- TwitCastingWebHookJson.cs
- DiscordStreamBotBackend.DataBase.Table
- AdminSettingsRedisService
- AccountLinksController
- BearerTokenService
- RedisChannels
- Utility
- AdminSettingsModels.cs
- Program
- .SendAsync

## God Nodes (most connected - your core abstractions)
1. `TwitchAuthorizationService` - 62 edges
2. `RedisService` - 35 edges
3. `DiscordStreamBotBackend.Services` - 30 edges
4. `GoogleAccountLinkServiceTests` - 29 edges
5. `GoogleOAuthService` - 24 edges
6. `TwitchOAuthRefreshLockLease` - 22 edges
7. `DiscordStreamBotBackend.Model` - 20 edges
8. `TwitchRefreshRotationLifecycle` - 20 edges
9. `GoogleAccountLinkService` - 16 edges
10. `BearerTokenService` - 15 edges

## Surprising Connections (you probably didn't know these)
- `FakeGoogleAccountProvider` --implements--> `IGoogleAccountProvider`  [EXTRACTED]
  tests/DiscordStreamBotBackend.Tests/GoogleAccountLinksContractTests.cs → DiscordStreamBotBackend/Services/GoogleAccountLinkService.cs
- `FakeGoogleProviderRevoker` --implements--> `IGoogleProviderRevoker`  [EXTRACTED]
  tests/DiscordStreamBotBackend.Tests/GoogleAccountLinksContractTests.cs → DiscordStreamBotBackend/Services/GoogleAccountLinkService.cs
- `FakeGoogleAccountLinkStore` --implements--> `IGoogleAccountLinkStore`  [EXTRACTED]
  tests/DiscordStreamBotBackend.Tests/GoogleAccountLinksContractTests.cs → DiscordStreamBotBackend/Services/GoogleAccountLinkService.cs
- `FakeWakeupPublisher` --implements--> `IGoogleMemberCleanupWakeupPublisher`  [EXTRACTED]
  tests/DiscordStreamBotBackend.Tests/GoogleAccountLinksContractTests.cs → DiscordStreamBotBackend/Services/GoogleAccountLinkService.cs
- `FakeMetricsRefresher` --implements--> `IGoogleAccountLinkMetricsRefresher`  [EXTRACTED]
  tests/DiscordStreamBotBackend.Tests/GoogleAccountLinksContractTests.cs → DiscordStreamBotBackend/Services/GoogleAccountLinkService.cs

## Import Cycles
- None detected.

## Communities (47 total, 0 thin omitted)

### Community 0 - "TwitchAuthorizationService"
Cohesion: 0.08
Nodes (28): DbContext, DbSet, MainDbContext, DateTime, TwitchBroadcasterAuthorization, TwitchAccessTokenData, TwitchTokenErrorData, TwitchUserData (+20 more)

### Community 1 - "RedisService"
Cohesion: 0.15
Nodes (11): Channel, CancellationTokenSource, ConcurrentDictionary, ConnectionMultiplexer, IDatabase, ILogger, int, List (+3 more)

### Community 2 - ".GetAuthorizationAsync"
Cohesion: 0.21
Nodes (11): CancellationToken, HttpGet, HttpPost, IActionResult, JObject, List, Task, AdminGuildsController (+3 more)

### Community 3 - "GoogleOAuthController"
Cohesion: 0.06
Nodes (34): ControllerBase, CancellationToken, EnableCors, HttpClient, HttpPost, IActionResult, IConfiguration, ILogger (+26 more)

### Community 4 - "TwitchRefreshRotationLifecycle"
Cohesion: 0.08
Nodes (21): Action, bool, Dictionary, int, object, Task, Lease, TwitchRefreshRotationLifecycle (+13 more)

### Community 5 - "CancellationToken"
Cohesion: 0.15
Nodes (13): GoogleProviderRevokeOutcome, GoogleProviderRevokeResult, Func, bool, CancellationToken, Exception, IReadOnlyList, List (+5 more)

### Community 6 - "GoogleOAuthService"
Cohesion: 0.14
Nodes (15): BackgroundService, CancellationToken, IDbContextFactory, IHttpClientFactory, ILogger, string, Task, GoogleOAuthService (+7 more)

### Community 7 - "MySqlDataStore"
Cohesion: 0.10
Nodes (12): CancellationToken, IDbContextFactory, Logger, Task, MySqlDataStore, ProviderTokenLoadResult, TokenCrypto, string (+4 more)

### Community 8 - "BiliBiliGetLiveUserInfoJson.cs"
Cohesion: 0.18
Nodes (17): DiscordStreamBotBackend.Model.BiliBili, List, BiliBiliGetLiveUserInfoJson, Exp, GetLiveUserInfo, Info, MasterLevel, OfficialVerify (+9 more)

### Community 9 - "GoogleAccountLinkService.cs"
Cohesion: 0.18
Nodes (12): CancellationTokenSource, ILogger, TimeSpan, GoogleAccountLinkService, GoogleUnlinkOperationCancellationFactory, IGoogleAccountLinkMetricsRefresher, IGoogleAccountProvider, IGoogleMemberCleanupWakeupPublisher (+4 more)

### Community 10 - "DiscordStreamBotBackend.csproj"
Cohesion: 0.09
Nodes (20): net8.0, Discord.Net.Webhook (3.19.0), EFCore.NamingConventions (9.0.0), Google.Apis.Oauth2.v2 (1.68.0.1869), Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24), Microsoft.EntityFrameworkCore.Design (9.0.0), Microsoft.NET.Test.Sdk (17.8.0), Newtonsoft.Json (13.0.4) (+12 more)

### Community 11 - "TwitchOAuthRefreshLockLease"
Cohesion: 0.14
Nodes (18): CancellationToken, CancellationTokenSource, Exception, IDatabase, int, RedisKey, RedisValue, string (+10 more)

### Community 12 - "EventSubHostedService"
Cohesion: 0.19
Nodes (9): ChannelUpdateArgs, CancellationToken, ILogger, Task, EventSubHostedService, IEventSubWebhooks, OnErrorArgs, StreamOfflineArgs (+1 more)

### Community 13 - "GoogleAccountLinksContractTests"
Cohesion: 0.18
Nodes (4): ValueTask, GoogleMemberCleanupWakeupPublisher, IReadOnlyIndex, GoogleAccountLinksContractTests

### Community 14 - "GoogleJson.cs"
Cohesion: 0.31
Nodes (10): DateTime, List, Default, High, Item, Medium, PageInfo, Snippet (+2 more)

### Community 15 - "GoogleAccountLinkServiceTests"
Cohesion: 0.22
Nodes (5): Fact, Task, ulong, GoogleAccountLinkServiceTests, GoogleAccountOperationCoordinatorTests

### Community 16 - "CancellationToken"
Cohesion: 0.22
Nodes (8): CancellationToken, IDbContextFactory, IReadOnlyList, Task, GoogleAccountLinkStore, GoogleUnlinkPreparation, GoogleUnlinkTransitionOutcome, IGoogleAccountLinkStore

### Community 17 - "GoogleOAuthOperationLockLease"
Cohesion: 0.11
Nodes (23): CancellationToken, CancellationTokenSource, Exception, IDatabase, int, RedisKey, RedisValue, string (+15 more)

### Community 18 - "BililiveRecorderWebHookController"
Cohesion: 0.21
Nodes (10): ContentResult, HttpClient, HttpPost, IConfiguration, ILogger, Task, BililiveRecorderWebHookController, DateTime (+2 more)

### Community 19 - "RedisConnection"
Cohesion: 0.33
Nodes (4): ConnectionMultiplexer, string, RedisConnection, Lazy

### Community 20 - "Discord Stream Bot Backend"
Cohesion: 0.33
Nodes (5): Discord Stream Bot Backend, Docker Compose 部署, Prometheus, 公開網域與 OAuth URI, 直接執行

### Community 21 - "TwitchAuthorizationService.cs"
Cohesion: 0.25
Nodes (6): DiscordStreamBotBackend.Tests, DiscordStreamBotBackend.Services.Auth, DiscordStreamBotBackend.Model.Twitch, DiscordStreamBotBackend.DataBase, TwitchAuthorizationChangedPayload, TwitchUnlinkResult

### Community 22 - "GoogleAccountOperationCoordinator"
Cohesion: 0.21
Nodes (9): Dictionary, IDisposable, object, SemaphoreSlim, ulong, GateEntry, GoogleAccountOperationCoordinator, Releaser (+1 more)

### Community 23 - "DiscordStreamBotBackend.Services"
Cohesion: 0.18
Nodes (4): DiscordStreamBotBackend.Model, DiscordStreamBotBackend.Controllers, DiscordStreamBotBackend.Services, DiscordAccessTokenData

### Community 24 - "GoogleAccountLink"
Cohesion: 0.22
Nodes (8): DateTime, IReadOnlyList, GoogleAccountLink, GoogleMemberSubscription, GoogleUnlinkResponse, TwitchAccountLink, string, FakeGoogleAccountProvider

### Community 25 - "AdminSettingsContractTests"
Cohesion: 0.23
Nodes (5): Fact, IConfiguration, Task, ulong, AdminSettingsContractTests

### Community 26 - ".SuccessfulRevokeTransitionsBeforePublish"
Cohesion: 0.53
Nodes (3): GoogleUnlinkResult, InlineData, Theory

### Community 27 - "YouTubeNotificationsController"
Cohesion: 0.24
Nodes (7): ContentResult, HttpGet, HttpPost, IEnumerable, ILogger, YouTubeNotificationsController, Stream

### Community 28 - "MySqlDataStore.cs"
Cohesion: 0.15
Nodes (9): Counter, DiscordStreamBotBackend, BackendMetrics, DiscordUser, ProviderTokenLoadStatus, ProviderTokenUnreadableException, Exception, Gauge (+1 more)

### Community 29 - "RandomVideoController"
Cohesion: 0.17
Nodes (9): byte, EnableCors, HttpGet, ILogger, Task, RandomVideoController, RNG, RandomNumberGenerator (+1 more)

### Community 30 - "YoutubePubSubNotification"
Cohesion: 0.25
Nodes (5): DateTime, Ext, YoutubePubSubNotification, YTNotificationType, YTNotificationType

### Community 31 - ".AddPubMessageAsync"
Cohesion: 0.33
Nodes (3): ContentResult, HttpPost, ValueTask

### Community 32 - "StartupValidationHostedService"
Cohesion: 0.16
Nodes (10): CancellationToken, IConfiguration, Task, StartupValidationHostedService, IConfiguration, Startup, IApplicationBuilder, IHostedService (+2 more)

### Community 33 - "TwitCastingWebHookController"
Cohesion: 0.13
Nodes (12): Controller, EnableCors, HttpGet, IActionResult, IndexController, ContentResult, EnableCors, HttpGet (+4 more)

### Community 34 - "LogMiddleware"
Cohesion: 0.25
Nodes (6): DiscordStreamBotBackend.Middleware, HttpContext, Logger, Task, LogMiddleware, RequestDelegate

### Community 35 - ".PublishAsync"
Cohesion: 0.46
Nodes (3): CancellationToken, Task, KeyValuePair

### Community 36 - ".GetManageableGuildsAsync"
Cohesion: 0.16
Nodes (12): AdminGuild, CancellationToken, DateTime, HttpClient, IEnumerable, List, Task, ulong (+4 more)

### Community 37 - "TwitCastingWebHookJson.cs"
Cohesion: 0.60
Nodes (4): DiscordStreamBotBackend.Model.TwitCasting, Broadcaster, Movie, TwitCastingWebHookJson

### Community 38 - "DiscordStreamBotBackend.DataBase.Table"
Cohesion: 0.13
Nodes (9): DiscordStreamBotBackend.DataBase.Table, DateTime, GoogleOAuthUnlinkIntent, DateTime, YoutubeChannelSpider, DateTime, YoutubeMemberAccessToken, DateTime (+1 more)

### Community 39 - "AdminSettingsRedisService"
Cohesion: 0.29
Nodes (7): AdminSettingsRequestEnvelope, CancellationToken, ILogger, Task, AdminSettingsRedisService, HashSet, IReadOnlyCollection

### Community 40 - "AccountLinksController"
Cohesion: 0.38
Nodes (6): CancellationToken, HttpGet, IActionResult, Task, AccountLinksController, HttpDelete

### Community 41 - "BearerTokenService"
Cohesion: 0.24
Nodes (6): DateTime, DiscordSessionPayload, int, string, TimeSpan, BearerTokenService

### Community 42 - "RedisChannels"
Cohesion: 0.31
Nodes (6): string, AdminSettings, Member, OAuth, RedisChannels, Twitch

### Community 43 - "Utility"
Cohesion: 0.22
Nodes (6): DateTime, HttpContext, string, MemberData, Utility, IPAddress

### Community 44 - "AdminSettingsModels.cs"
Cohesion: 0.39
Nodes (7): JObject, List, AdminSettingsCommandReply, AdminSettingsCommandRequest, AdminSettingsSnapshotReply, BotGuildSnapshot, BotGuildSnapshotEnvelope

### Community 45 - "Program"
Cohesion: 0.33
Nodes (3): Assembly, Program, IHostBuilder

### Community 46 - ".SendAsync"
Cohesion: 0.33
Nodes (5): HttpMessageHandler, HttpRequestMessage, HttpResponseMessage, CancellationToken, GuildListHandler

## Knowledge Gaps
- **31 isolated node(s):** `net8.0`, `Discord.Net.Webhook (3.19.0)`, `EFCore.NamingConventions (9.0.0)`, `Google.Apis.Oauth2.v2 (1.68.0.1869)`, `Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24)` (+26 more)
  These have ≤1 connection - possible missing edges or undocumented components.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `TwitchAuthorizationService` connect `TwitchAuthorizationService` to `RedisService`, `GoogleOAuthController`, `TwitchRefreshRotationLifecycle`, `GoogleOAuthService`, `MySqlDataStore`, `AccountLinksController`, `TwitchOAuthRefreshLockLease`, `GoogleOAuthOperationLockLease`, `TwitchAuthorizationService.cs`?**
  _High betweenness centrality (0.223) - this node is a cross-community bridge._
- **Why does `DiscordStreamBotBackend.Services` connect `DiscordStreamBotBackend.Services` to `StartupValidationHostedService`, `LogMiddleware`, `GoogleOAuthController`, `TwitchRefreshRotationLifecycle`, `GoogleOAuthService`, `MySqlDataStore`, `GoogleAccountLinkService.cs`, `TwitchOAuthRefreshLockLease`, `EventSubHostedService`, `GoogleOAuthOperationLockLease`, `TwitchAuthorizationService.cs`, `MySqlDataStore.cs`?**
  _High betweenness centrality (0.202) - this node is a cross-community bridge._
- **Why does `RedisService` connect `RedisService` to `StartupValidationHostedService`, `TwitCastingWebHookController`, `LogMiddleware`, `GoogleOAuthController`, `.PublishAsync`, `TwitchRefreshRotationLifecycle`, `TwitchAuthorizationService`, `AdminSettingsRedisService`, `EventSubHostedService`, `GoogleAccountLinksContractTests`, `DiscordStreamBotBackend.Services`, `YouTubeNotificationsController`, `RandomVideoController`, `YoutubePubSubNotification`, `.AddPubMessageAsync`?**
  _High betweenness centrality (0.159) - this node is a cross-community bridge._
- **What connects `net8.0`, `Discord.Net.Webhook (3.19.0)`, `EFCore.NamingConventions (9.0.0)` to the rest of the system?**
  _31 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `TwitchAuthorizationService` be split into smaller, more focused modules?**
  _Cohesion score 0.08146705615060046 - nodes in this community are weakly interconnected._
- **Should `GoogleOAuthController` be split into smaller, more focused modules?**
  _Cohesion score 0.05580693815987934 - nodes in this community are weakly interconnected._
- **Should `TwitchRefreshRotationLifecycle` be split into smaller, more focused modules?**
  _Cohesion score 0.07686274509803921 - nodes in this community are weakly interconnected._