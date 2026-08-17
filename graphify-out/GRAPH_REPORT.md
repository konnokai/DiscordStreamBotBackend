# Graph Report - DiscordStreamBotBackend  (2026-08-17)

## Corpus Check
- 69 files · ~22,539 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 823 nodes · 1722 edges · 38 communities (37 shown, 1 thin omitted)
- Extraction: 95% EXTRACTED · 5% INFERRED · 0% AMBIGUOUS · INFERRED: 94 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `3845e425`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- TwitchAuthorizationService
- RedisService
- .GetAuthorizationAsync
- AccountLinksController
- TwitchRefreshRotationLifecycle
- CancellationToken
- GoogleOAuthService
- MySqlDataStore
- BiliBiliGetLiveUserInfoJson.cs
- GoogleAccountLinkService.cs
- DiscordStreamBotBackend.csproj
- TwitchOAuthRefreshLockLease
- StartupValidationHostedService
- GoogleAccountLinksContractTests
- GoogleJson.cs
- GoogleAccountLinkServiceTests
- CancellationToken
- GoogleOAuthOperationLockLease
- BililiveRecorderWebHookController
- RedisConnection
- Discord Stream Bot Backend
- DiscordStreamBotBackend.Model
- GoogleAccountOperationCoordinator
- DiscordStreamBotBackend.Services
- GoogleAccountLink
- BearerTokenService
- .SuccessfulRevokeTransitionsBeforePublish
- YouTubeNotificationsController
- DiscordStreamBotBackend
- .RandomVideo
- YouTubeNotificationsController.cs
- TwitCastingWebHookController
- BiliBiliGetRoomInfoJson.cs
- RandomVideoController
- LogMiddleware
- .PublishAsync
- .StatusCheck
- TwitCastingWebHookJson.cs

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

## Communities (38 total, 1 thin omitted)

### Community 0 - "TwitchAuthorizationService"
Cohesion: 0.06
Nodes (40): DiscordStreamBotBackend.DataBase.Table, DiscordStreamBotBackend.Model.Twitch, DbContext, DbSet, MainDbContext, DateTime, GoogleOAuthUnlinkIntent, DateTime (+32 more)

### Community 1 - "RedisService"
Cohesion: 0.15
Nodes (11): Channel, CancellationTokenSource, ConcurrentDictionary, ConnectionMultiplexer, IDatabase, ILogger, int, List (+3 more)

### Community 2 - ".GetAuthorizationAsync"
Cohesion: 0.07
Nodes (38): CancellationToken, HttpGet, HttpPost, IActionResult, JObject, List, Task, AdminGuildsController (+30 more)

### Community 3 - "AccountLinksController"
Cohesion: 0.07
Nodes (29): ControllerBase, CancellationToken, HttpGet, IActionResult, Task, AccountLinksController, CancellationToken, EnableCors (+21 more)

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
Cohesion: 0.09
Nodes (15): CancellationToken, IDbContextFactory, Logger, Task, MySqlDataStore, ProviderTokenLoadResult, ProviderTokenLoadStatus, ProviderTokenUnreadableException (+7 more)

### Community 8 - "BiliBiliGetLiveUserInfoJson.cs"
Cohesion: 0.39
Nodes (8): List, BiliBiliGetLiveUserInfoJson, Exp, GetLiveUserInfo, Info, MasterLevel, OfficialVerify, RoomNews

### Community 9 - "GoogleAccountLinkService.cs"
Cohesion: 0.18
Nodes (12): CancellationTokenSource, ILogger, TimeSpan, GoogleAccountLinkService, GoogleUnlinkOperationCancellationFactory, IGoogleAccountLinkMetricsRefresher, IGoogleAccountProvider, IGoogleMemberCleanupWakeupPublisher (+4 more)

### Community 10 - "DiscordStreamBotBackend.csproj"
Cohesion: 0.09
Nodes (20): net8.0, Discord.Net.Webhook (3.19.0), EFCore.NamingConventions (9.0.0), Google.Apis.Oauth2.v2 (1.68.0.1869), Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24), Microsoft.EntityFrameworkCore.Design (9.0.0), Microsoft.NET.Test.Sdk (17.8.0), Newtonsoft.Json (13.0.4) (+12 more)

### Community 11 - "TwitchOAuthRefreshLockLease"
Cohesion: 0.14
Nodes (18): CancellationToken, CancellationTokenSource, Exception, IDatabase, int, RedisKey, RedisValue, string (+10 more)

### Community 12 - "StartupValidationHostedService"
Cohesion: 0.11
Nodes (14): ChannelUpdateArgs, CancellationToken, ILogger, Task, EventSubHostedService, CancellationToken, IConfiguration, Task (+6 more)

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
Cohesion: 0.17
Nodes (11): DiscordStreamBotBackend.Model.BiliBili, ContentResult, HttpClient, HttpPost, IConfiguration, ILogger, Task, BililiveRecorderWebHookController (+3 more)

### Community 19 - "RedisConnection"
Cohesion: 0.33
Nodes (4): ConnectionMultiplexer, string, RedisConnection, Lazy

### Community 20 - "Discord Stream Bot Backend"
Cohesion: 0.33
Nodes (5): Discord Stream Bot Backend, Docker Compose 部署, Prometheus, 公開網域與 OAuth URI, 直接執行

### Community 21 - "DiscordStreamBotBackend.Model"
Cohesion: 0.23
Nodes (5): DiscordStreamBotBackend.Model, DiscordStreamBotBackend.Tests, DiscordStreamBotBackend.Services.Auth, DiscordStreamBotBackend.DataBase, DiscordAccessTokenData

### Community 22 - "GoogleAccountOperationCoordinator"
Cohesion: 0.21
Nodes (9): Dictionary, IDisposable, object, SemaphoreSlim, ulong, GateEntry, GoogleAccountOperationCoordinator, Releaser (+1 more)

### Community 24 - "GoogleAccountLink"
Cohesion: 0.22
Nodes (8): DateTime, IReadOnlyList, GoogleAccountLink, GoogleMemberSubscription, GoogleUnlinkResponse, TwitchAccountLink, string, FakeGoogleAccountProvider

### Community 25 - "BearerTokenService"
Cohesion: 0.06
Nodes (27): CancellationToken, EnableCors, HttpClient, HttpPost, IActionResult, IConfiguration, ILogger, Task (+19 more)

### Community 26 - ".SuccessfulRevokeTransitionsBeforePublish"
Cohesion: 0.53
Nodes (3): GoogleUnlinkResult, InlineData, Theory

### Community 27 - "YouTubeNotificationsController"
Cohesion: 0.24
Nodes (7): ContentResult, HttpGet, HttpPost, IEnumerable, ILogger, YouTubeNotificationsController, Stream

### Community 28 - "DiscordStreamBotBackend"
Cohesion: 0.05
Nodes (26): Assembly, Counter, DiscordStreamBotBackend, BackendMetrics, DiscordUser, Program, string, AdminSettings (+18 more)

### Community 29 - ".RandomVideo"
Cohesion: 0.20
Nodes (7): byte, EnableCors, HttpGet, Task, RNG, RandomNumberGenerator, RedirectResult

### Community 30 - "YouTubeNotificationsController.cs"
Cohesion: 0.27
Nodes (5): DateTime, Ext, YoutubePubSubNotification, YTNotificationType, YTNotificationType

### Community 31 - "TwitCastingWebHookController"
Cohesion: 0.22
Nodes (6): ContentResult, HttpPost, IConfiguration, ILogger, TwitCastingWebHookController, ValueTask

### Community 32 - "BiliBiliGetRoomInfoJson.cs"
Cohesion: 0.42
Nodes (8): List, Badge, BiliBiliGetRoomInfoJson, Frame, GetRoomInfo, MobileFrame, NewPendants, StudioInfo

### Community 33 - "RandomVideoController"
Cohesion: 0.25
Nodes (7): Controller, EnableCors, HttpGet, IActionResult, IndexController, ILogger, RandomVideoController

### Community 34 - "LogMiddleware"
Cohesion: 0.25
Nodes (6): DiscordStreamBotBackend.Middleware, HttpContext, Logger, Task, LogMiddleware, RequestDelegate

### Community 35 - ".PublishAsync"
Cohesion: 0.46
Nodes (3): CancellationToken, Task, KeyValuePair

### Community 36 - ".StatusCheck"
Cohesion: 0.33
Nodes (4): ContentResult, EnableCors, HttpGet, StatusCheckController

### Community 37 - "TwitCastingWebHookJson.cs"
Cohesion: 0.60
Nodes (4): DiscordStreamBotBackend.Model.TwitCasting, Broadcaster, Movie, TwitCastingWebHookJson

## Knowledge Gaps
- **31 isolated node(s):** `net8.0`, `Discord.Net.Webhook (3.19.0)`, `EFCore.NamingConventions (9.0.0)`, `Google.Apis.Oauth2.v2 (1.68.0.1869)`, `Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24)` (+26 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **1 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `TwitchAuthorizationService` connect `TwitchAuthorizationService` to `RedisService`, `AccountLinksController`, `TwitchRefreshRotationLifecycle`, `GoogleOAuthService`, `MySqlDataStore`, `TwitchOAuthRefreshLockLease`, `GoogleOAuthOperationLockLease`?**
  _High betweenness centrality (0.222) - this node is a cross-community bridge._
- **Why does `DiscordStreamBotBackend.Services` connect `DiscordStreamBotBackend.Services` to `TwitchAuthorizationService`, `LogMiddleware`, `.GetAuthorizationAsync`, `AccountLinksController`, `TwitchRefreshRotationLifecycle`, `GoogleOAuthService`, `MySqlDataStore`, `GoogleAccountLinkService.cs`, `TwitchOAuthRefreshLockLease`, `StartupValidationHostedService`, `GoogleOAuthOperationLockLease`, `DiscordStreamBotBackend.Model`, `DiscordStreamBotBackend`, `YouTubeNotificationsController.cs`?**
  _High betweenness centrality (0.203) - this node is a cross-community bridge._
- **Why does `RedisService` connect `RedisService` to `TwitchAuthorizationService`, `RandomVideoController`, `.GetAuthorizationAsync`, `LogMiddleware`, `AccountLinksController`, `.PublishAsync`, `TwitchRefreshRotationLifecycle`, `StartupValidationHostedService`, `GoogleAccountLinksContractTests`, `DiscordStreamBotBackend.Services`, `YouTubeNotificationsController`, `YouTubeNotificationsController.cs`, `TwitCastingWebHookController`?**
  _High betweenness centrality (0.160) - this node is a cross-community bridge._
- **What connects `net8.0`, `Discord.Net.Webhook (3.19.0)`, `EFCore.NamingConventions (9.0.0)` to the rest of the system?**
  _31 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `TwitchAuthorizationService` be split into smaller, more focused modules?**
  _Cohesion score 0.059922680412371136 - nodes in this community are weakly interconnected._
- **Should `.GetAuthorizationAsync` be split into smaller, more focused modules?**
  _Cohesion score 0.06641604010025062 - nodes in this community are weakly interconnected._
- **Should `AccountLinksController` be split into smaller, more focused modules?**
  _Cohesion score 0.06612244897959184 - nodes in this community are weakly interconnected._