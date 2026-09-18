# Graph Report - DiscordStreamBotBackend  (2026-09-16)

## Corpus Check
- 69 files · ~22,603 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1238 nodes · 2357 edges · 62 communities (61 shown, 1 thin omitted)
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 147 edges (avg confidence: 0.82)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `cdfe3fc3`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- TwitchAuthorizationService
- RedisService
- AdminGuildsController
- GoogleOAuthController
- TwitchRefreshRotationLifecycle
- GetRoomInfo
- GoogleOAuthService
- DiscordStreamBotBackend.DataBase.Table
- GetLiveUserInfo
- Movie
- DiscordStreamBotBackend.csproj
- TwitchOAuthRefreshLockLease
- EventSubHostedService
- GoogleAccountLinksContractTests
- GoogleJson.cs
- FakeGoogleAccountLinkStore
- .ConfigureServices
- GoogleOAuthOperationLockLease
- BililiveRecorderWebHookEventData
- RedisConnection
- Discord Stream Bot Backend
- .Callback
- .PublishAndWaitAsync
- DiscordStreamBotBackend.Services
- TwitchAccountLink
- BearerTokenService
- AdminSettingsSnapshotReply
- YouTubeNotificationsController
- YoutubePubSubNotification
- MainDbContext
- DiscordUser
- TwitCastingWebHookController
- TwitchBroadcasterAuthorization
- AdminSettingsContractTests
- LogMiddleware
- .AddPubMessageAsync
- PublicUrlService
- AccountLinksController
- TwitchOAuthController
- OAuthStateService
- Broadcaster
- BiliBiliGetRoomInfoJson.cs
- RedisChannels
- AdminSettingsCommandReply
- AdminGuild
- .GetManageableGuildsAsync
- RandomVideoController
- Frame
- MobileFrame
- Program
- .RandomVideo
- TokenService
- NonPersistentGoogleDataStore
- MemberData
- GoogleAccountLink
- AdminSettingsRequestEnvelope
- Startup
- DiscordStreamBotBackend
- GoogleMemberSubscription
- Utility
- BiliBiliGetRoomInfoJson
- NewPendants

## God Nodes (most connected - your core abstractions)
1. `TwitchAuthorizationService` - 62 edges
2. `GetRoomInfo` - 40 edges
3. `RedisService` - 40 edges
4. `FakeGoogleAccountLinkStore` - 36 edges
5. `DiscordStreamBotBackend.Services` - 30 edges
6. `GoogleAccountLinkServiceTests` - 28 edges
7. `MainDbContext` - 25 edges
8. `GoogleOAuthService` - 25 edges
9. `TwitchRefreshRotationLifecycle` - 23 edges
10. `Movie` - 22 edges

## Surprising Connections (you probably didn't know these)
- `FakeGoogleAccountLinkStore` --references--> `GoogleMemberSubscription`  [EXTRACTED]
  tests/DiscordStreamBotBackend.Tests/GoogleAccountLinksContractTests.cs → DiscordStreamBotBackend/Model/AccountLinks.cs
- `FakeGoogleAccountProvider` --implements--> `IGoogleAccountProvider`  [EXTRACTED]
  tests/DiscordStreamBotBackend.Tests/GoogleAccountLinksContractTests.cs → DiscordStreamBotBackend/Services/GoogleAccountLinkService.cs
- `FakeGoogleProviderRevoker` --implements--> `IGoogleProviderRevoker`  [EXTRACTED]
  tests/DiscordStreamBotBackend.Tests/GoogleAccountLinksContractTests.cs → DiscordStreamBotBackend/Services/GoogleAccountLinkService.cs
- `FakeGoogleAccountLinkStore` --implements--> `IGoogleAccountLinkStore`  [EXTRACTED]
  tests/DiscordStreamBotBackend.Tests/GoogleAccountLinksContractTests.cs → DiscordStreamBotBackend/Services/GoogleAccountLinkService.cs
- `FakeWakeupPublisher` --implements--> `IGoogleMemberCleanupWakeupPublisher`  [EXTRACTED]
  tests/DiscordStreamBotBackend.Tests/GoogleAccountLinksContractTests.cs → DiscordStreamBotBackend/Services/GoogleAccountLinkService.cs

## Import Cycles
- None detected.

## Communities (62 total, 1 thin omitted)

### Community 0 - "TwitchAuthorizationService"
Cohesion: 0.05
Nodes (51): TwitchAccessTokenData, AccessToken, ExpiresIn, RefreshToken, Scopes, TokenType, TwitchUserId, TwitchTokenErrorData (+43 more)

### Community 1 - "RedisService"
Cohesion: 0.11
Nodes (15): Channel, CancellationTokenSource, ConcurrentDictionary, ConnectionMultiplexer, IDatabase, ILogger, List, RedisService (+7 more)

### Community 2 - "AdminGuildsController"
Cohesion: 0.23
Nodes (12): CancellationToken, HttpGet, HttpPost, IActionResult, JObject, List, Task, TimeSpan (+4 more)

### Community 3 - "GoogleOAuthController"
Cohesion: 0.20
Nodes (8): CancellationToken, EnableCors, HttpGet, HttpPost, IActionResult, ILogger, Task, GoogleOAuthController

### Community 4 - "TwitchRefreshRotationLifecycle"
Cohesion: 0.19
Nodes (12): Action, Lease, Dictionary, Task, Lease, TwitchRefreshRotationLifecycle, ActiveOperationCount, PendingPersistenceCount (+4 more)

### Community 5 - "GetRoomInfo"
Cohesion: 0.05
Nodes (38): GetRoomInfo, AllowChangeAreaTime, AllowUploadCoverTime, AreaId, AreaName, AreaPendants, Attention, Background (+30 more)

### Community 6 - "GoogleOAuthService"
Cohesion: 0.08
Nodes (27): BackgroundService, CancellationToken, Exception, IDbContextFactory, Logger, Task, MySqlDataStore, ProviderTokenLoadResult (+19 more)

### Community 7 - "DiscordStreamBotBackend.DataBase.Table"
Cohesion: 0.06
Nodes (29): DiscordStreamBotBackend.DataBase.Table, DateTime, GoogleOAuthUnlinkIntent, DateAdded, DiscordUserId, ExpectedEncryptedToken, DateTime, YoutubeChannelSpider (+21 more)

### Community 8 - "GetLiveUserInfo"
Cohesion: 0.06
Nodes (36): List, BiliBiliGetLiveUserInfoJson, Code, Data, Message, Msg, Exp, MasterLevel (+28 more)

### Community 9 - "Movie"
Cohesion: 0.09
Nodes (22): Movie, Category, CommentCount, Country, Created, CurrentViewCount, Duration, HlsUrl (+14 more)

### Community 10 - "DiscordStreamBotBackend.csproj"
Cohesion: 0.09
Nodes (20): net8.0, Discord.Net.Webhook (3.19.0), EFCore.NamingConventions (9.0.0), Google.Apis.Oauth2.v2 (1.68.0.1869), Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24), Microsoft.EntityFrameworkCore.Design (9.0.0), Microsoft.NET.Test.Sdk (17.8.0), Newtonsoft.Json (13.0.4) (+12 more)

### Community 11 - "TwitchOAuthRefreshLockLease"
Cohesion: 0.08
Nodes (30): TwitchAuthorizationChangedPayload, Status, TwitchUserId, CancellationToken, CancellationTokenSource, IDatabase, RedisKey, RedisValue (+22 more)

### Community 12 - "EventSubHostedService"
Cohesion: 0.19
Nodes (10): ChannelUpdateArgs, CancellationToken, ILogger, Task, EventSubHostedService, IEventSubWebhooks, IHostedService, OnErrorArgs (+2 more)

### Community 13 - "GoogleAccountLinksContractTests"
Cohesion: 0.22
Nodes (4): Version, IReadOnlyIndex, TokenResponse, GoogleAccountLinksContractTests

### Community 14 - "GoogleJson.cs"
Cohesion: 0.06
Nodes (37): DateTime, List, Default, height, url, width, High, height (+29 more)

### Community 15 - "FakeGoogleAccountLinkStore"
Cohesion: 0.07
Nodes (50): Dictionary, IDisposable, SemaphoreSlim, GateEntry, ReferenceCount, Semaphore, GoogleAccountOperationCoordinator, GateCount (+42 more)

### Community 16 - ".ConfigureServices"
Cohesion: 0.07
Nodes (39): CancellationToken, CancellationTokenSource, IDbContextFactory, ILogger, IReadOnlyList, Task, TimeSpan, ValueTask (+31 more)

### Community 17 - "GoogleOAuthOperationLockLease"
Cohesion: 0.09
Nodes (27): CancellationToken, CancellationTokenSource, IDatabase, RedisKey, RedisValue, Task, TimeSpan, ValueTask (+19 more)

### Community 18 - "BililiveRecorderWebHookEventData"
Cohesion: 0.07
Nodes (29): ContentResult, HttpClient, HttpPost, IConfiguration, ILogger, Task, BililiveRecorderWebHookController, DateTime (+21 more)

### Community 19 - "RedisConnection"
Cohesion: 0.33
Nodes (4): ConnectionMultiplexer, RedisConnection, Instance, Lazy

### Community 20 - "Discord Stream Bot Backend"
Cohesion: 0.33
Nodes (5): Discord Stream Bot Backend, Docker Compose 部署, Prometheus, 公開網域與 OAuth URI, 直接執行

### Community 21 - ".Callback"
Cohesion: 0.10
Nodes (16): CancellationToken, EnableCors, HttpClient, HttpPost, IActionResult, IConfiguration, ILogger, Task (+8 more)

### Community 22 - ".PublishAndWaitAsync"
Cohesion: 0.21
Nodes (12): CancellationToken, ILogger, Task, AdminSettingsRedisOutcome, DeadlineExceeded, Reply, Unavailable, AdminSettingsRedisResult (+4 more)

### Community 23 - "DiscordStreamBotBackend.Services"
Cohesion: 0.17
Nodes (7): DiscordStreamBotBackend.Model, DiscordStreamBotBackend.Controllers, DiscordStreamBotBackend.Tests, DiscordStreamBotBackend.Services.Auth, DiscordStreamBotBackend.Model.Twitch, DiscordStreamBotBackend.DataBase, DiscordStreamBotBackend.Services

### Community 24 - "TwitchAccountLink"
Cohesion: 0.20
Nodes (9): GoogleUnlinkResponse, CleanupPending, Status, TwitchAccountLink, DisplayName, ProfileImageUrl, Status, TwitchUserId (+1 more)

### Community 25 - "BearerTokenService"
Cohesion: 0.17
Nodes (11): DateTime, DiscordSessionPayload, DiscordAccessToken, DiscordUserId, ExpiresAtUtc, IssuedAtUtc, ProviderExpiresAtUtc, Purpose (+3 more)

### Community 26 - "AdminSettingsSnapshotReply"
Cohesion: 0.13
Nodes (16): List, AdminSettingsSnapshotReply, Capabilities, Common, ContractVersion, Crawlers, Guild, Health (+8 more)

### Community 27 - "YouTubeNotificationsController"
Cohesion: 0.21
Nodes (8): ControllerBase, ContentResult, HttpGet, HttpPost, IEnumerable, ILogger, YouTubeNotificationsController, Stream

### Community 28 - "YoutubePubSubNotification"
Cohesion: 0.12
Nodes (14): DateTime, Ext, YoutubePubSubNotification, ChannelId, Link, NotificationType, Published, Title (+6 more)

### Community 29 - "MainDbContext"
Cohesion: 0.15
Nodes (15): DbContext, DbContextOptions, DbSet, TwitchBroadcasterAuthorization, MainDbContext, GoogleOAuthUnlinkIntent, TwitchBroadcasterAuthorization, YoutubeChannelSpider (+7 more)

### Community 30 - "DiscordUser"
Cohesion: 0.12
Nodes (16): DiscordUser, accent_color, avatar, avatar_decoration, banner, banner_color, discriminator, email (+8 more)

### Community 31 - "TwitCastingWebHookController"
Cohesion: 0.17
Nodes (10): DiscordStreamBotBackend.Model.TwitCasting, ContentResult, HttpPost, IConfiguration, ILogger, TwitCastingWebHookController, TwitCastingWebHookJson, Broadcaster (+2 more)

### Community 32 - "TwitchBroadcasterAuthorization"
Cohesion: 0.07
Nodes (27): DateTime, TwitchBroadcasterAuthorization, AuthorizedAt, ClientId, DateUpdated, DiscordUserId, DisplayName, EncryptedAccessToken (+19 more)

### Community 33 - "AdminSettingsContractTests"
Cohesion: 0.19
Nodes (8): HttpMessageHandler, HttpResponseMessage, CancellationToken, Fact, Task, AdminSettingsContractTests, GuildListHandler, RequestCount

### Community 34 - "LogMiddleware"
Cohesion: 0.25
Nodes (6): DiscordStreamBotBackend.Middleware, HttpContext, Logger, Task, LogMiddleware, RequestDelegate

### Community 35 - ".AddPubMessageAsync"
Cohesion: 0.36
Nodes (4): CancellationToken, Task, ValueTask, KeyValuePair

### Community 36 - "PublicUrlService"
Cohesion: 0.14
Nodes (10): EnableCors, HttpGet, IActionResult, IndexController, PublicUrlService, ApiServerDomain, DiscordRedirectUrl, FrontendDomain (+2 more)

### Community 37 - "AccountLinksController"
Cohesion: 0.33
Nodes (7): CancellationToken, HttpGet, IActionResult, Task, AccountLinksController, HttpDelete, ObjectResult

### Community 38 - "TwitchOAuthController"
Cohesion: 0.23
Nodes (8): CancellationToken, EnableCors, HttpGet, HttpPost, IActionResult, ILogger, Task, TwitchOAuthController

### Community 39 - "OAuthStateService"
Cohesion: 0.23
Nodes (7): OAuthStateData, DiscordUserId, FrontendDomain, Provider, Task, TimeSpan, OAuthStateService

### Community 40 - "Broadcaster"
Cohesion: 0.17
Nodes (12): Broadcaster, Created, Id, Image, IsLive, LastMovieId, Level, Name (+4 more)

### Community 41 - "BiliBiliGetRoomInfoJson.cs"
Cohesion: 0.18
Nodes (10): DiscordStreamBotBackend.Model.BiliBili, List, Badge, Desc, Name, Position, Value, StudioInfo (+2 more)

### Community 42 - "RedisChannels"
Cohesion: 0.25
Nodes (5): AdminSettings, Member, OAuth, RedisChannels, Twitch

### Community 43 - "AdminSettingsCommandReply"
Cohesion: 0.18
Nodes (11): JObject, AdminSettingsCommandReply, Arguments, Code, ContractVersion, CorrelationId, ShardId, State (+3 more)

### Community 44 - "AdminGuild"
Cohesion: 0.20
Nodes (9): AdminGuild, BotInstalled, Icon, Id, Name, Owner, Permissions, InlineData (+1 more)

### Community 45 - ".GetManageableGuildsAsync"
Cohesion: 0.22
Nodes (8): CancellationToken, DateTime, HttpClient, IEnumerable, List, Task, DiscordGuildAuthorizationService, IMemoryCache

### Community 46 - "RandomVideoController"
Cohesion: 0.20
Nodes (7): Controller, ILogger, RandomVideoController, ContentResult, EnableCors, HttpGet, StatusCheckController

### Community 47 - "Frame"
Cohesion: 0.20
Nodes (10): Frame, Area, AreaOld, BgColor, BgPic, Desc, Name, Position (+2 more)

### Community 48 - "MobileFrame"
Cohesion: 0.20
Nodes (10): MobileFrame, Area, AreaOld, BgColor, BgPic, Desc, Name, Position (+2 more)

### Community 49 - "Program"
Cohesion: 0.25
Nodes (5): Assembly, AssemblyInformationalVersionAttribute, Program, VERSION, IHostBuilder

### Community 50 - ".RandomVideo"
Cohesion: 0.22
Nodes (6): EnableCors, HttpGet, Task, RNG, RandomNumberGenerator, RedirectResult

### Community 52 - "NonPersistentGoogleDataStore"
Cohesion: 0.33
Nodes (3): Task, NonPersistentGoogleDataStore, IDataStore

### Community 53 - "MemberData"
Cohesion: 0.22
Nodes (9): DateTime, MemberData, DiscordUserId, GoogleAccessToken, GoogleExpiresIn, GoogleRefrechToken, GoogleUserAvatar, GoogleUserName (+1 more)

### Community 54 - "GoogleAccountLink"
Cohesion: 0.25
Nodes (8): IReadOnlyList, GoogleAccountLink, ChannelId, CleanupPending, ProfileImageUrl, Status, Subscriptions, UserName

### Community 55 - "AdminSettingsRequestEnvelope"
Cohesion: 0.25
Nodes (8): AdminSettingsRequestEnvelope, Action, ActorUserId, ContractVersion, CorrelationId, DeadlineUnixMs, GuildId, Payload

### Community 56 - "Startup"
Cohesion: 0.25
Nodes (6): IConfiguration, Startup, Configuration, IApplicationBuilder, IWebHostEnvironment, LogMiddleware

### Community 57 - "DiscordStreamBotBackend"
Cohesion: 0.29
Nodes (5): Counter, DiscordStreamBotBackend, BackendMetrics, Gauge, Histogram

### Community 58 - "GoogleMemberSubscription"
Cohesion: 0.29
Nodes (7): DateTime, GoogleMemberSubscription, ChannelId, GuildId, IsChecked, LastCheckedAt, PendingRoleRemoval

### Community 59 - "Utility"
Cohesion: 0.29
Nodes (4): HttpContext, Utility, TwitchWebHookSecret, IPAddress

### Community 60 - "BiliBiliGetRoomInfoJson"
Cohesion: 0.40
Nodes (5): BiliBiliGetRoomInfoJson, Code, Data, Message, Msg

### Community 61 - "NewPendants"
Cohesion: 0.40
Nodes (5): NewPendants, Badge, Frame, MobileBadge, MobileFrame

## Knowledge Gaps
- **442 isolated node(s):** `CreateOrUpdated`, `Deleted`, `NotificationType`, `VideoId`, `ChannelId` (+437 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 618 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **1 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `DiscordStreamBotBackend.Model` connect `DiscordStreamBotBackend.Services` to `TwitchAuthorizationService`, `OAuthStateService`, `.GetManageableGuildsAsync`, `GoogleJson.cs`, `.ConfigureServices`, `BililiveRecorderWebHookEventData`, `.Callback`, `.PublishAndWaitAsync`, `TwitchAccountLink`, `BearerTokenService`, `AdminSettingsSnapshotReply`?**
  _High betweenness centrality (0.245) - this node is a cross-community bridge._
- **Why does `TwitchAuthorizationService` connect `TwitchAuthorizationService` to `RedisService`, `PublicUrlService`, `AccountLinksController`, `TwitchOAuthController`, `TwitchRefreshRotationLifecycle`, `GoogleOAuthService`, `TwitchOAuthRefreshLockLease`, `.ConfigureServices`, `TokenService`, `MainDbContext`?**
  _High betweenness centrality (0.155) - this node is a cross-community bridge._
- **Why does `DiscordStreamBotBackend.Services` connect `DiscordStreamBotBackend.Services` to `TwitchAuthorizationService`, `LogMiddleware`, `GoogleOAuthController`, `PublicUrlService`, `TwitchOAuthController`, `OAuthStateService`, `GoogleOAuthService`, `TwitchOAuthRefreshLockLease`, `.GetManageableGuildsAsync`, `RandomVideoController`, `.ConfigureServices`, `GoogleOAuthOperationLockLease`, `NonPersistentGoogleDataStore`, `.Callback`, `.PublishAndWaitAsync`, `Startup`, `YoutubePubSubNotification`, `TwitCastingWebHookController`?**
  _High betweenness centrality (0.134) - this node is a cross-community bridge._
- **What connects `CreateOrUpdated`, `Deleted`, `NotificationType` to the rest of the system?**
  _442 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `TwitchAuthorizationService` be split into smaller, more focused modules?**
  _Cohesion score 0.05463544641157434 - nodes in this community are weakly interconnected._
- **Should `RedisService` be split into smaller, more focused modules?**
  _Cohesion score 0.11052631578947368 - nodes in this community are weakly interconnected._
- **Should `GetRoomInfo` be split into smaller, more focused modules?**
  _Cohesion score 0.05263157894736842 - nodes in this community are weakly interconnected._