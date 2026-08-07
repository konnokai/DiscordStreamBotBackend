# Graph Report - DiscordStreamBotBackend  (2026-08-07)

## Corpus Check
- 64 files · ~20,489 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 737 nodes · 1540 edges · 37 communities (32 shown, 5 thin omitted)
- Extraction: 95% EXTRACTED · 5% INFERRED · 0% AMBIGUOUS · INFERRED: 82 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `29148c3b`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- TwitchAuthorizationService
- RedisService
- DiscordStreamBotBackend
- GoogleOAuthController
- TwitchRefreshRotationLifecycle
- BililiveRecorderWebHookController
- GoogleOAuthService
- MySqlDataStore
- BiliBiliGetLiveUserInfoJson.cs
- EventSubHostedService
- DiscordStreamBotBackend.csproj
- TwitchOAuthRefreshLockLease
- StartupValidationHostedService
- DiscordOAuthController
- GoogleJson.cs
- GoogleAccountLinkServiceTests
- GoogleAccountLinkService.cs
- GoogleOAuthOperationLockLease
- TwitCastingWebHookController
- RedisConnection
- Discord Stream Bot Backend
- TwitchAuthorizationService.cs
- RandomVideoController
- TwitchOAuthController
- DiscordStreamBotBackend.Services
- AccountLinksController
- DiscordStreamBotBackend.Model
- OAuthStateService
- Utility
- Program
- RedisChannels
- BearerTokenService
- TwitCastingWebHookJson.cs
- GoogleOAuthUnlinkIntent
- YoutubeChannelSpider
- YoutubeMemberAccessToken
- YoutubeMemberCheck

## God Nodes (most connected - your core abstractions)
1. `TwitchAuthorizationService` - 62 edges
2. `RedisService` - 34 edges
3. `GoogleAccountLinkServiceTests` - 29 edges
4. `DiscordStreamBotBackend.Services` - 26 edges
5. `GoogleOAuthService` - 24 edges
6. `TwitchOAuthRefreshLockLease` - 22 edges
7. `TwitchRefreshRotationLifecycle` - 20 edges
8. `GoogleAccountLinkService` - 16 edges
9. `DiscordStreamBotBackend.Model` - 15 edges
10. `MainDbContext` - 14 edges

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

## Communities (37 total, 5 thin omitted)

### Community 0 - "TwitchAuthorizationService"
Cohesion: 0.08
Nodes (28): DbContext, DbSet, MainDbContext, DateTime, TwitchBroadcasterAuthorization, TwitchAccessTokenData, TwitchTokenErrorData, TwitchUserData (+20 more)

### Community 1 - "RedisService"
Cohesion: 0.05
Nodes (35): Channel, DiscordStreamBotBackend.Middleware, ContentResult, HttpPost, ContentResult, DateTime, HttpGet, HttpPost (+27 more)

### Community 2 - "DiscordStreamBotBackend"
Cohesion: 0.22
Nodes (6): Counter, DiscordStreamBotBackend, BackendMetrics, DiscordUser, Gauge, Histogram

### Community 3 - "GoogleOAuthController"
Cohesion: 0.16
Nodes (10): ControllerBase, CancellationToken, EnableCors, HttpGet, HttpPost, IActionResult, ILogger, Task (+2 more)

### Community 4 - "TwitchRefreshRotationLifecycle"
Cohesion: 0.08
Nodes (21): Action, bool, Dictionary, int, object, Task, Lease, TwitchRefreshRotationLifecycle (+13 more)

### Community 5 - "BililiveRecorderWebHookController"
Cohesion: 0.21
Nodes (10): ContentResult, HttpClient, HttpPost, IConfiguration, ILogger, Task, BililiveRecorderWebHookController, DateTime (+2 more)

### Community 6 - "GoogleOAuthService"
Cohesion: 0.14
Nodes (15): BackgroundService, CancellationToken, IDbContextFactory, IHttpClientFactory, ILogger, string, Task, GoogleOAuthService (+7 more)

### Community 7 - "MySqlDataStore"
Cohesion: 0.09
Nodes (12): CancellationToken, IDbContextFactory, Logger, Task, MySqlDataStore, ProviderTokenLoadResult, TokenCrypto, string (+4 more)

### Community 8 - "BiliBiliGetLiveUserInfoJson.cs"
Cohesion: 0.18
Nodes (17): DiscordStreamBotBackend.Model.BiliBili, List, BiliBiliGetLiveUserInfoJson, Exp, GetLiveUserInfo, Info, MasterLevel, OfficialVerify (+9 more)

### Community 9 - "EventSubHostedService"
Cohesion: 0.18
Nodes (10): ChannelUpdateArgs, CancellationToken, ILogger, Task, EventSubHostedService, IEventSubWebhooks, IHostedService, OnErrorArgs (+2 more)

### Community 10 - "DiscordStreamBotBackend.csproj"
Cohesion: 0.09
Nodes (20): net8.0, Discord.Net.Webhook (3.19.0), EFCore.NamingConventions (9.0.0), Google.Apis.Oauth2.v2 (1.68.0.1869), Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24), Microsoft.EntityFrameworkCore.Design (9.0.0), Microsoft.NET.Test.Sdk (17.8.0), Newtonsoft.Json (13.0.4) (+12 more)

### Community 11 - "TwitchOAuthRefreshLockLease"
Cohesion: 0.14
Nodes (18): CancellationToken, CancellationTokenSource, Exception, IDatabase, int, RedisKey, RedisValue, string (+10 more)

### Community 12 - "StartupValidationHostedService"
Cohesion: 0.17
Nodes (9): CancellationToken, IConfiguration, Task, StartupValidationHostedService, IConfiguration, Startup, IApplicationBuilder, IServiceCollection (+1 more)

### Community 13 - "DiscordOAuthController"
Cohesion: 0.15
Nodes (11): CancellationToken, EnableCors, HttpClient, HttpPost, IActionResult, IConfiguration, ILogger, Task (+3 more)

### Community 14 - "GoogleJson.cs"
Cohesion: 0.31
Nodes (10): DateTime, List, Default, High, Item, Medium, PageInfo, Snippet (+2 more)

### Community 15 - "GoogleAccountLinkServiceTests"
Cohesion: 0.06
Nodes (32): Dictionary, IDisposable, object, SemaphoreSlim, ulong, GateEntry, GoogleAccountOperationCoordinator, GoogleProviderRevokeOutcome (+24 more)

### Community 16 - "GoogleAccountLinkService.cs"
Cohesion: 0.08
Nodes (30): DateTime, IReadOnlyList, GoogleAccountLink, GoogleMemberSubscription, GoogleUnlinkResponse, TwitchAccountLink, CancellationToken, CancellationTokenSource (+22 more)

### Community 17 - "GoogleOAuthOperationLockLease"
Cohesion: 0.11
Nodes (23): CancellationToken, CancellationTokenSource, Exception, IDatabase, int, RedisKey, RedisValue, string (+15 more)

### Community 18 - "TwitCastingWebHookController"
Cohesion: 0.13
Nodes (12): Controller, EnableCors, HttpGet, IActionResult, IndexController, ContentResult, EnableCors, HttpGet (+4 more)

### Community 19 - "RedisConnection"
Cohesion: 0.33
Nodes (4): ConnectionMultiplexer, string, RedisConnection, Lazy

### Community 20 - "Discord Stream Bot Backend"
Cohesion: 0.33
Nodes (5): Discord Stream Bot Backend, Docker Compose 部署, Prometheus, 公開網域與 OAuth URI, 直接執行

### Community 21 - "TwitchAuthorizationService.cs"
Cohesion: 0.18
Nodes (10): DiscordStreamBotBackend.Tests, DiscordStreamBotBackend.Services.Auth, DiscordStreamBotBackend.DataBase.Table, DiscordStreamBotBackend.Model.Twitch, DiscordStreamBotBackend.DataBase, ProviderTokenLoadStatus, ProviderTokenUnreadableException, TwitchAuthorizationChangedPayload (+2 more)

### Community 22 - "RandomVideoController"
Cohesion: 0.17
Nodes (9): byte, EnableCors, HttpGet, ILogger, Task, RandomVideoController, RNG, RandomNumberGenerator (+1 more)

### Community 23 - "TwitchOAuthController"
Cohesion: 0.23
Nodes (8): CancellationToken, EnableCors, HttpGet, HttpPost, IActionResult, ILogger, Task, TwitchOAuthController

### Community 25 - "AccountLinksController"
Cohesion: 0.38
Nodes (6): CancellationToken, HttpGet, IActionResult, Task, AccountLinksController, HttpDelete

### Community 26 - "DiscordStreamBotBackend.Model"
Cohesion: 0.22
Nodes (4): DiscordStreamBotBackend.Model, DiscordAccessTokenData, DateTime, DiscordSessionPayload

### Community 27 - "OAuthStateService"
Cohesion: 0.31
Nodes (5): OAuthStateData, string, Task, TimeSpan, OAuthStateService

### Community 28 - "Utility"
Cohesion: 0.22
Nodes (6): DateTime, HttpContext, string, MemberData, Utility, IPAddress

### Community 29 - "Program"
Cohesion: 0.33
Nodes (3): Assembly, Program, IHostBuilder

### Community 30 - "RedisChannels"
Cohesion: 0.38
Nodes (5): string, Member, OAuth, RedisChannels, Twitch

### Community 31 - "BearerTokenService"
Cohesion: 0.40
Nodes (4): int, string, TimeSpan, BearerTokenService

### Community 32 - "TwitCastingWebHookJson.cs"
Cohesion: 0.60
Nodes (4): DiscordStreamBotBackend.Model.TwitCasting, Broadcaster, Movie, TwitCastingWebHookJson

## Knowledge Gaps
- **31 isolated node(s):** `net8.0`, `Discord.Net.Webhook (3.19.0)`, `EFCore.NamingConventions (9.0.0)`, `Google.Apis.Oauth2.v2 (1.68.0.1869)`, `Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24)` (+26 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **5 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `TwitchAuthorizationService` connect `TwitchAuthorizationService` to `RedisService`, `GoogleOAuthController`, `TwitchRefreshRotationLifecycle`, `GoogleOAuthService`, `MySqlDataStore`, `TwitchOAuthRefreshLockLease`, `GoogleOAuthOperationLockLease`, `TwitchAuthorizationService.cs`, `TwitchOAuthController`, `AccountLinksController`?**
  _High betweenness centrality (0.249) - this node is a cross-community bridge._
- **Why does `DiscordStreamBotBackend.Services` connect `DiscordStreamBotBackend.Services` to `RedisService`, `DiscordStreamBotBackend`, `GoogleOAuthController`, `TwitchRefreshRotationLifecycle`, `GoogleOAuthService`, `MySqlDataStore`, `EventSubHostedService`, `TwitchOAuthRefreshLockLease`, `StartupValidationHostedService`, `GoogleAccountLinkService.cs`, `GoogleOAuthOperationLockLease`, `TwitchAuthorizationService.cs`, `DiscordStreamBotBackend.Model`?**
  _High betweenness centrality (0.185) - this node is a cross-community bridge._
- **Why does `RedisService` connect `RedisService` to `TwitchAuthorizationService`, `TwitchRefreshRotationLifecycle`, `EventSubHostedService`, `StartupValidationHostedService`, `GoogleAccountLinkService.cs`, `TwitCastingWebHookController`, `RandomVideoController`, `DiscordStreamBotBackend.Services`, `OAuthStateService`?**
  _High betweenness centrality (0.163) - this node is a cross-community bridge._
- **What connects `net8.0`, `Discord.Net.Webhook (3.19.0)`, `EFCore.NamingConventions (9.0.0)` to the rest of the system?**
  _31 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `TwitchAuthorizationService` be split into smaller, more focused modules?**
  _Cohesion score 0.08069620253164557 - nodes in this community are weakly interconnected._
- **Should `RedisService` be split into smaller, more focused modules?**
  _Cohesion score 0.05185185185185185 - nodes in this community are weakly interconnected._
- **Should `TwitchRefreshRotationLifecycle` be split into smaller, more focused modules?**
  _Cohesion score 0.07686274509803921 - nodes in this community are weakly interconnected._