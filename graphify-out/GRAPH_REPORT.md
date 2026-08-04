# Graph Report - DiscordStreamBotBackend  (2026-08-04)

## Corpus Check
- 59 files · ~15,610 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 556 nodes · 1086 edges · 21 communities
- Extraction: 95% EXTRACTED · 5% INFERRED · 0% AMBIGUOUS · INFERRED: 56 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `b9803204`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- TwitchAuthorizationService
- RedisService
- DiscordStreamBotBackend
- GoogleOAuthService
- TwitchRefreshRotationLifecycle
- BililiveRecorderWebHookController
- TwitchTokenValidationHostedService
- MySqlDataStore
- BiliBiliGetLiveUserInfoJson.cs
- EventSubHostedService
- DiscordStreamBotBackend.csproj
- TwitchOAuthRefreshLockLease
- StartupValidationHostedService
- DiscordOAuthController
- GoogleJson.cs
- BiliBiliGetRoomInfoJson.cs
- AccountLinks.cs
- LogMiddleware
- RedisConnection
- Discord Stream Bot Backend
- DiscordStreamBotBackend.Services

## God Nodes (most connected - your core abstractions)
1. `TwitchAuthorizationService` - 62 edges
2. `RedisService` - 34 edges
3. `DiscordStreamBotBackend.Services` - 22 edges
4. `TwitchOAuthRefreshLockLease` - 22 edges
5. `TwitchRefreshRotationLifecycle` - 20 edges
6. `GoogleOAuthService` - 19 edges
7. `TwitchOAuthContractTests` - 14 edges
8. `MainDbContext` - 13 edges
9. `DiscordStreamBotBackend.Model` - 13 edges
10. `StartupValidationHostedService` - 13 edges

## Surprising Connections (you probably didn't know these)
- `AccountLinksController` --references--> `TwitchAuthorizationService`  [EXTRACTED]
  DiscordStreamBotBackend/Controllers/AccountLinksController.cs → DiscordStreamBotBackend/Services/TwitchAuthorizationService.cs
- `DiscordOAuthController` --references--> `BearerTokenService`  [EXTRACTED]
  DiscordStreamBotBackend/Controllers/DiscordOAuthController.cs → DiscordStreamBotBackend/Services/BearerTokenService.cs
- `DiscordOAuthController` --references--> `PublicUrlService`  [EXTRACTED]
  DiscordStreamBotBackend/Controllers/DiscordOAuthController.cs → DiscordStreamBotBackend/Services/PublicUrlService.cs
- `IndexController` --references--> `PublicUrlService`  [EXTRACTED]
  DiscordStreamBotBackend/Controllers/IndexController.cs → DiscordStreamBotBackend/Services/PublicUrlService.cs
- `RandomVideoController` --references--> `PublicUrlService`  [EXTRACTED]
  DiscordStreamBotBackend/Controllers/RandomVideoController.cs → DiscordStreamBotBackend/Services/PublicUrlService.cs

## Import Cycles
- None detected.

## Communities (21 total, 0 thin omitted)

### Community 0 - "TwitchAuthorizationService"
Cohesion: 0.09
Nodes (25): DbContext, DbSet, MainDbContext, DateTime, TwitchBroadcasterAuthorization, TwitchAccessTokenData, TwitchValidateTokenData, CancellationToken (+17 more)

### Community 1 - "RedisService"
Cohesion: 0.06
Nodes (32): Channel, ContentResult, HttpPost, IConfiguration, ILogger, TwitCastingWebHookController, ContentResult, DateTime (+24 more)

### Community 2 - "DiscordStreamBotBackend"
Cohesion: 0.06
Nodes (23): Assembly, Counter, DiscordStreamBotBackend, BackendMetrics, DiscordUser, Program, string, RedisChannels (+15 more)

### Community 3 - "GoogleOAuthService"
Cohesion: 0.05
Nodes (42): ControllerBase, CancellationToken, HttpGet, IActionResult, Task, AccountLinksController, CancellationToken, EnableCors (+34 more)

### Community 4 - "TwitchRefreshRotationLifecycle"
Cohesion: 0.08
Nodes (21): Action, bool, Dictionary, int, object, Task, Lease, TwitchRefreshRotationLifecycle (+13 more)

### Community 5 - "BililiveRecorderWebHookController"
Cohesion: 0.06
Nodes (28): byte, Controller, ContentResult, HttpClient, HttpPost, IConfiguration, ILogger, Task (+20 more)

### Community 6 - "TwitchTokenValidationHostedService"
Cohesion: 0.40
Nodes (5): BackgroundService, CancellationToken, ILogger, Task, TwitchTokenValidationHostedService

### Community 7 - "MySqlDataStore"
Cohesion: 0.13
Nodes (8): IDbContextFactory, Logger, Task, MySqlDataStore, TokenCrypto, string, TokenService, IDataStore

### Community 8 - "BiliBiliGetLiveUserInfoJson.cs"
Cohesion: 0.39
Nodes (8): List, BiliBiliGetLiveUserInfoJson, Exp, GetLiveUserInfo, Info, MasterLevel, OfficialVerify, RoomNews

### Community 9 - "EventSubHostedService"
Cohesion: 0.18
Nodes (10): ChannelUpdateArgs, CancellationToken, ILogger, Task, EventSubHostedService, IEventSubWebhooks, IHostedService, OnErrorArgs (+2 more)

### Community 10 - "DiscordStreamBotBackend.csproj"
Cohesion: 0.09
Nodes (20): net8.0, Discord.Net.Webhook (3.19.0), EFCore.NamingConventions (9.0.0), Google.Apis.Oauth2.v2 (1.68.0.1869), Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24), Microsoft.EntityFrameworkCore.Design (9.0.0), Microsoft.NET.Test.Sdk (17.8.0), Newtonsoft.Json (13.0.4) (+12 more)

### Community 11 - "TwitchOAuthRefreshLockLease"
Cohesion: 0.14
Nodes (19): CancellationToken, CancellationTokenSource, IDatabase, int, string, Task, TimeSpan, ValueTask (+11 more)

### Community 12 - "StartupValidationHostedService"
Cohesion: 0.32
Nodes (4): CancellationToken, IConfiguration, Task, StartupValidationHostedService

### Community 13 - "DiscordOAuthController"
Cohesion: 0.15
Nodes (11): CancellationToken, EnableCors, HttpClient, HttpPost, IActionResult, IConfiguration, ILogger, Task (+3 more)

### Community 14 - "GoogleJson.cs"
Cohesion: 0.31
Nodes (10): DateTime, List, Default, High, Item, Medium, PageInfo, Snippet (+2 more)

### Community 15 - "BiliBiliGetRoomInfoJson.cs"
Cohesion: 0.42
Nodes (8): List, Badge, BiliBiliGetRoomInfoJson, Frame, GetRoomInfo, MobileFrame, NewPendants, StudioInfo

### Community 16 - "AccountLinks.cs"
Cohesion: 0.40
Nodes (5): DateTime, GoogleAccountLink, GoogleMemberSubscription, TwitchAccountLink, IReadOnlyList

### Community 17 - "LogMiddleware"
Cohesion: 0.25
Nodes (6): DiscordStreamBotBackend.Middleware, HttpContext, Logger, Task, LogMiddleware, RequestDelegate

### Community 19 - "RedisConnection"
Cohesion: 0.33
Nodes (4): ConnectionMultiplexer, string, RedisConnection, Lazy

### Community 20 - "Discord Stream Bot Backend"
Cohesion: 0.33
Nodes (5): Discord Stream Bot Backend, Docker Compose 部署, Prometheus, 公開網域與 OAuth URI, 直接執行

### Community 21 - "DiscordStreamBotBackend.Services"
Cohesion: 0.06
Nodes (27): DiscordStreamBotBackend.Model, DiscordStreamBotBackend.Model.TwitCasting, DiscordStreamBotBackend.Controllers, DiscordStreamBotBackend.Tests, DiscordStreamBotBackend.Model.BiliBili, DiscordStreamBotBackend.Services.Auth, DiscordStreamBotBackend.DataBase.Table, DiscordStreamBotBackend.Model.Twitch (+19 more)

## Knowledge Gaps
- **30 isolated node(s):** `net8.0`, `Discord.Net.Webhook (3.19.0)`, `EFCore.NamingConventions (9.0.0)`, `Google.Apis.Oauth2.v2 (1.68.0.1869)`, `Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24)` (+25 more)
  These have ≤1 connection - possible missing edges or undocumented components.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `TwitchAuthorizationService` connect `TwitchAuthorizationService` to `RedisService`, `GoogleOAuthService`, `TwitchRefreshRotationLifecycle`, `TwitchTokenValidationHostedService`, `MySqlDataStore`, `TwitchOAuthRefreshLockLease`, `DiscordStreamBotBackend.Services`?**
  _High betweenness centrality (0.322) - this node is a cross-community bridge._
- **Why does `RedisService` connect `RedisService` to `TwitchAuthorizationService`, `GoogleOAuthService`, `TwitchRefreshRotationLifecycle`, `BililiveRecorderWebHookController`, `EventSubHostedService`, `StartupValidationHostedService`, `LogMiddleware`, `DiscordStreamBotBackend.Services`?**
  _High betweenness centrality (0.239) - this node is a cross-community bridge._
- **Why does `DiscordStreamBotBackend.Services` connect `DiscordStreamBotBackend.Services` to `RedisService`, `DiscordStreamBotBackend`, `GoogleOAuthService`, `TwitchRefreshRotationLifecycle`, `TwitchTokenValidationHostedService`, `EventSubHostedService`, `TwitchOAuthRefreshLockLease`, `StartupValidationHostedService`, `LogMiddleware`?**
  _High betweenness centrality (0.140) - this node is a cross-community bridge._
- **What connects `net8.0`, `Discord.Net.Webhook (3.19.0)`, `EFCore.NamingConventions (9.0.0)` to the rest of the system?**
  _30 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `TwitchAuthorizationService` be split into smaller, more focused modules?**
  _Cohesion score 0.08701754385964912 - nodes in this community are weakly interconnected._
- **Should `RedisService` be split into smaller, more focused modules?**
  _Cohesion score 0.0603921568627451 - nodes in this community are weakly interconnected._
- **Should `DiscordStreamBotBackend` be split into smaller, more focused modules?**
  _Cohesion score 0.05873015873015873 - nodes in this community are weakly interconnected._