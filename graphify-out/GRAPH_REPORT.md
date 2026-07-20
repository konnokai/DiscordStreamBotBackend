# Graph Report - DiscordStreamBotBackend  (2026-07-20)

## Corpus Check
- 58 files · ~12,925 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 459 nodes · 822 edges · 23 communities (22 shown, 1 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 36 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `73c2b5b2`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- TwitchAuthorizationService
- RedisService
- DiscordStreamBotBackend.Services
- GoogleOAuthController
- DiscordStreamBotBackend.Model
- .RandomVideo
- GoogleOAuthService
- MySqlDataStore
- BiliBiliGetLiveUserInfoJson.cs
- EventSubHostedService
- DiscordStreamBotBackend.csproj
- AccountLinksController
- StartupValidationHostedService
- RedisDataStore
- GoogleJson.cs
- BiliBiliGetRoomInfoJson.cs
- Utility
- LogMiddleware
- Program
- RedisConnection
- Discord Stream Bot Backend
- TwitCastingWebHookJson.cs
- ServerConfig

## God Nodes (most connected - your core abstractions)
1. `TwitchAuthorizationService` - 38 edges
2. `RedisService` - 33 edges
3. `DiscordStreamBotBackend.Services` - 19 edges
4. `GoogleOAuthService` - 19 edges
5. `StartupValidationHostedService` - 14 edges
6. `MainDbContext` - 13 edges
7. `DiscordStreamBotBackend.Model` - 13 edges
8. `BearerTokenService` - 12 edges
9. `EventSubHostedService` - 12 edges
10. `DiscordStreamBotBackend.Controllers` - 11 edges

## Surprising Connections (you probably didn't know these)
- `AccountLinksController` --references--> `GoogleOAuthService`  [EXTRACTED]
  DiscordStreamBotBackend/Controllers/AccountLinksController.cs → DiscordStreamBotBackend/Services/GoogleOAuthService.cs
- `AccountLinksController` --references--> `TwitchAuthorizationService`  [EXTRACTED]
  DiscordStreamBotBackend/Controllers/AccountLinksController.cs → DiscordStreamBotBackend/Services/TwitchAuthorizationService.cs
- `DiscordOAuthController` --references--> `BearerTokenService`  [EXTRACTED]
  DiscordStreamBotBackend/Controllers/DiscordOAuthController.cs → DiscordStreamBotBackend/Services/BearerTokenService.cs
- `DiscordOAuthController` --references--> `PublicUrlService`  [EXTRACTED]
  DiscordStreamBotBackend/Controllers/DiscordOAuthController.cs → DiscordStreamBotBackend/Services/PublicUrlService.cs
- `GoogleOAuthController` --references--> `BearerTokenService`  [EXTRACTED]
  DiscordStreamBotBackend/Controllers/GoogleOAuthController.cs → DiscordStreamBotBackend/Services/BearerTokenService.cs

## Import Cycles
- None detected.

## Communities (23 total, 1 thin omitted)

### Community 0 - "TwitchAuthorizationService"
Cohesion: 0.11
Nodes (24): DiscordStreamBotBackend.Model.Twitch, DbContext, DbSet, Dictionary, MainDbContext, DateTime, TwitchBroadcasterAuthorization, TwitchAccessTokenData (+16 more)

### Community 1 - "RedisService"
Cohesion: 0.07
Nodes (28): CancellationTokenSource, Channel, ConcurrentDictionary, ContentResult, HttpPost, ContentResult, DateTime, HttpGet (+20 more)

### Community 2 - "DiscordStreamBotBackend.Services"
Cohesion: 0.08
Nodes (21): Counter, DiscordStreamBotBackend.Model.TwitCasting, DiscordStreamBotBackend.Controllers, DiscordStreamBotBackend.Services.Auth, DiscordStreamBotBackend.DataBase, DiscordStreamBotBackend.Services, DiscordStreamBotBackend, BackendMetrics (+13 more)

### Community 3 - "GoogleOAuthController"
Cohesion: 0.06
Nodes (32): ControllerBase, CancellationToken, EnableCors, HttpGet, HttpPost, IActionResult, ILogger, Task (+24 more)

### Community 4 - "DiscordStreamBotBackend.Model"
Cohesion: 0.15
Nodes (11): CancellationToken, EnableCors, HttpClient, HttpPost, IActionResult, IConfiguration, ILogger, Task (+3 more)

### Community 5 - ".RandomVideo"
Cohesion: 0.07
Nodes (21): byte, Controller, EnableCors, HttpGet, IActionResult, IndexController, EnableCors, HttpGet (+13 more)

### Community 6 - "GoogleOAuthService"
Cohesion: 0.36
Nodes (5): BackgroundService, CancellationToken, ILogger, Task, TwitchTokenValidationHostedService

### Community 7 - "MySqlDataStore"
Cohesion: 0.14
Nodes (7): IDbContextFactory, Logger, Task, MySqlDataStore, TokenCrypto, string, TokenService

### Community 8 - "BiliBiliGetLiveUserInfoJson.cs"
Cohesion: 0.07
Nodes (28): DiscordStreamBotBackend.Model, DiscordStreamBotBackend.Model.BiliBili, ContentResult, HttpClient, HttpPost, IConfiguration, ILogger, Task (+20 more)

### Community 9 - "EventSubHostedService"
Cohesion: 0.18
Nodes (10): ChannelUpdateArgs, CancellationToken, ILogger, Task, EventSubHostedService, IEventSubWebhooks, IHostedService, OnErrorArgs (+2 more)

### Community 10 - "DiscordStreamBotBackend.csproj"
Cohesion: 0.12
Nodes (15): net8.0, Discord.Net.Webhook (3.19.0), EFCore.NamingConventions (9.0.0), Google.Apis.Oauth2.v2 (1.68.0.1869), Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24), Microsoft.EntityFrameworkCore.Design (9.0.3), Newtonsoft.Json (13.0.4), NLog (6.1.1) (+7 more)

### Community 11 - "AccountLinksController"
Cohesion: 0.19
Nodes (10): CancellationToken, HttpGet, IActionResult, Task, AccountLinksController, string, TimeSpan, BearerTokenService (+2 more)

### Community 12 - "StartupValidationHostedService"
Cohesion: 0.26
Nodes (5): CancellationToken, IConfiguration, Task, StartupValidationHostedService, IServiceCollection

### Community 13 - "RedisDataStore"
Cohesion: 0.21
Nodes (7): Task, ITokenDataStore, Logger, Task, RedisDataStore, IDataStore, Type

### Community 14 - "GoogleJson.cs"
Cohesion: 0.31
Nodes (10): DateTime, List, Default, High, Item, Medium, PageInfo, Snippet (+2 more)

### Community 15 - "BiliBiliGetRoomInfoJson.cs"
Cohesion: 0.42
Nodes (8): List, Badge, BiliBiliGetRoomInfoJson, Frame, GetRoomInfo, MobileFrame, NewPendants, StudioInfo

### Community 16 - "Utility"
Cohesion: 0.22
Nodes (6): DateTime, HttpContext, string, MemberData, Utility, IPAddress

### Community 17 - "LogMiddleware"
Cohesion: 0.25
Nodes (6): DiscordStreamBotBackend.Middleware, HttpContext, Logger, Task, LogMiddleware, RequestDelegate

### Community 18 - "Program"
Cohesion: 0.33
Nodes (3): Assembly, Program, IHostBuilder

### Community 19 - "RedisConnection"
Cohesion: 0.33
Nodes (4): ConnectionMultiplexer, string, RedisConnection, Lazy

### Community 20 - "Discord Stream Bot Backend"
Cohesion: 0.33
Nodes (5): Discord Stream Bot Backend, Docker Compose 部署, Prometheus, 公開網域與 OAuth URI, 直接執行

### Community 21 - "TwitCastingWebHookJson.cs"
Cohesion: 0.17
Nodes (7): DiscordStreamBotBackend.DataBase.Table, DateTime, YoutubeChannelSpider, DateTime, YoutubeMemberAccessToken, DateTime, YoutubeMemberCheck

## Knowledge Gaps
- **25 isolated node(s):** `YTNotificationType`, `net8.0`, `Discord.Net.Webhook (3.19.0)`, `EFCore.NamingConventions (9.0.0)`, `Google.Apis.Oauth2.v2 (1.68.0.1869)` (+20 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **1 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `RedisService` connect `RedisService` to `TwitchAuthorizationService`, `DiscordStreamBotBackend.Services`, `GoogleOAuthController`, `.RandomVideo`, `EventSubHostedService`, `StartupValidationHostedService`, `RedisDataStore`, `LogMiddleware`?**
  _High betweenness centrality (0.279) - this node is a cross-community bridge._
- **Why does `TwitchAuthorizationService` connect `TwitchAuthorizationService` to `RedisService`, `GoogleOAuthController`, `GoogleOAuthService`, `MySqlDataStore`, `AccountLinksController`, `StartupValidationHostedService`?**
  _High betweenness centrality (0.210) - this node is a cross-community bridge._
- **Why does `DiscordStreamBotBackend.Model` connect `BiliBiliGetLiveUserInfoJson.cs` to `TwitchAuthorizationService`, `DiscordStreamBotBackend.Services`, `GoogleOAuthController`, `DiscordStreamBotBackend.Model`, `AccountLinksController`, `GoogleJson.cs`?**
  _High betweenness centrality (0.128) - this node is a cross-community bridge._
- **What connects `YTNotificationType`, `net8.0`, `Discord.Net.Webhook (3.19.0)` to the rest of the system?**
  _25 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `TwitchAuthorizationService` be split into smaller, more focused modules?**
  _Cohesion score 0.10839598997493734 - nodes in this community are weakly interconnected._
- **Should `RedisService` be split into smaller, more focused modules?**
  _Cohesion score 0.06570048309178744 - nodes in this community are weakly interconnected._
- **Should `DiscordStreamBotBackend.Services` be split into smaller, more focused modules?**
  _Cohesion score 0.07539118065433854 - nodes in this community are weakly interconnected._