# Graph Report - DiscordStreamBotBackend  (2026-09-19)

## Corpus Check
- 74 files · ~26,171 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1366 nodes · 2715 edges · 83 communities (77 shown, 6 thin omitted)
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 159 edges (avg confidence: 0.82)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `986d42ba`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- TwitchAuthorizationService
- RedisService
- AdminGuildsController
- GoogleOAuthController
- YoutubePubSubNotification
- GetRoomInfo
- GoogleOAuthService
- YoutubeMemberCheck
- BiliBiliGetLiveUserInfoJson.cs
- Movie
- DiscordStreamBotBackend.csproj
- TwitchOAuthRefreshLockLease
- EventSubHostedService
- GoogleAccountLink
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
- DiscordStreamBotBackend.Model
- DiscordSessionPayload
- AdminSettingsSnapshotReply
- YouTubeNotificationsController
- .AddPubMessageAsync
- MainDbContext
- DiscordUser
- TwitCastingWebHookController
- TwitchRefreshRotationLifecycle
- AdminSettingsContractTests
- LogMiddleware
- YoutubeChannelSpider
- PublicUrlService
- AccountLinksController
- TwitchOAuthController
- OAuthStateService
- DiscordStreamBotBackend.DataBase.Table
- BiliBiliGetRoomInfoJson.cs
- RedisChannels
- AdminSettingsCommandReply
- AdminGuild
- YoutubeMemberAccessToken
- RandomVideoController
- Frame
- MobileFrame
- Program
- .RandomVideo
- TokenService
- CancellationToken
- MemberData
- YoutubeWebSubContract
- AdminSettingsRequestEnvelope
- Startup
- DiscordStreamBotBackend
- Fact
- Utility
- BiliBiliGetRoomInfoJson
- NewPendants
- YoutubeWebSubContractTests
- YoutubeWebSubChallengeAction
- YoutubeWebSubPendingAction
- Broadcaster
- .GetManageableGuildsAsync
- BililiveRecorderWebHookController
- GetLiveUserInfo
- .PublishAsync
- BililiveRecorderWebHookJson
- BearerTokenService
- GoogleProviderRevokeOutcome
- GuildListHandler
- TwitCastingWebHookJson
- DiscordOAuthController
- TwitchAccountLink
- MasterLevel
- Info
- BiliBiliGetLiveUserInfoJson
- GoogleUnlinkResult
- GoogleUnlinkTransitionOutcome
- YTNotificationType

## God Nodes (most connected - your core abstractions)
1. `TwitchAuthorizationService` - 62 edges
2. `GetRoomInfo` - 40 edges
3. `RedisService` - 40 edges
4. `FakeGoogleAccountLinkStore` - 36 edges
5. `YoutubeWebSubContractTests` - 35 edges
6. `DiscordStreamBotBackend.Services` - 31 edges
7. `GoogleAccountLinkServiceTests` - 28 edges
8. `MainDbContext` - 25 edges
9. `GoogleOAuthService` - 25 edges
10. `TwitchRefreshRotationLifecycle` - 23 edges

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

## Communities (83 total, 6 thin omitted)

### Community 0 - "TwitchAuthorizationService"
Cohesion: 0.05
Nodes (54): DiscordStreamBotBackend.Model.Twitch, TwitchAccessTokenData, AccessToken, ExpiresIn, RefreshToken, Scopes, TokenType, TwitchUserId (+46 more)

### Community 1 - "RedisService"
Cohesion: 0.12
Nodes (15): Channel, CancellationTokenSource, ConcurrentDictionary, ConnectionMultiplexer, IDatabase, ILogger, List, RedisService (+7 more)

### Community 2 - "AdminGuildsController"
Cohesion: 0.23
Nodes (12): CancellationToken, HttpGet, HttpPost, IActionResult, JObject, List, Task, TimeSpan (+4 more)

### Community 3 - "GoogleOAuthController"
Cohesion: 0.22
Nodes (8): CancellationToken, EnableCors, HttpGet, HttpPost, IActionResult, ILogger, Task, GoogleOAuthController

### Community 4 - "YoutubePubSubNotification"
Cohesion: 0.10
Nodes (16): DateTime, YoutubePubSubNotification, ChannelId, Link, NotificationType, Published, Title, Updated (+8 more)

### Community 5 - "GetRoomInfo"
Cohesion: 0.05
Nodes (38): GetRoomInfo, AllowChangeAreaTime, AllowUploadCoverTime, AreaId, AreaName, AreaPendants, Attention, Background (+30 more)

### Community 6 - "GoogleOAuthService"
Cohesion: 0.08
Nodes (27): BackgroundService, CancellationToken, Exception, IDbContextFactory, Logger, Task, MySqlDataStore, ProviderTokenLoadResult (+19 more)

### Community 7 - "YoutubeMemberCheck"
Cohesion: 0.20
Nodes (10): DateTime, YoutubeMemberCheck, CheckYtChannelId, DateAdded, GuildId, Id, IsChecked, LastCheckTime (+2 more)

### Community 8 - "BiliBiliGetLiveUserInfoJson.cs"
Cohesion: 0.20
Nodes (9): Exp, MasterLevel, OfficialVerify, Desc, Type, RoomNews, Content, Ctime (+1 more)

### Community 9 - "Movie"
Cohesion: 0.09
Nodes (22): Movie, Category, CommentCount, Country, Created, CurrentViewCount, Duration, HlsUrl (+14 more)

### Community 10 - "DiscordStreamBotBackend.csproj"
Cohesion: 0.09
Nodes (20): net8.0, Discord.Net.Webhook (3.19.0), EFCore.NamingConventions (9.0.0), Google.Apis.Oauth2.v2 (1.68.0.1869), Microsoft.AspNetCore.Mvc.NewtonsoftJson (8.0.24), Microsoft.EntityFrameworkCore.Design (9.0.0), Microsoft.NET.Test.Sdk (17.8.0), Newtonsoft.Json (13.0.4) (+12 more)

### Community 11 - "TwitchOAuthRefreshLockLease"
Cohesion: 0.09
Nodes (27): Status, CancellationToken, CancellationTokenSource, IDatabase, RedisKey, RedisValue, Task, TimeSpan (+19 more)

### Community 12 - "EventSubHostedService"
Cohesion: 0.18
Nodes (10): ChannelUpdateArgs, CancellationToken, ILogger, Task, EventSubHostedService, IEventSubWebhooks, IHostedService, OnErrorArgs (+2 more)

### Community 13 - "GoogleAccountLink"
Cohesion: 0.11
Nodes (18): DateTime, IReadOnlyList, GoogleAccountLink, ChannelId, CleanupPending, ProfileImageUrl, Status, Subscriptions (+10 more)

### Community 14 - "GoogleJson.cs"
Cohesion: 0.06
Nodes (37): DateTime, List, Default, height, url, width, High, height (+29 more)

### Community 15 - "FakeGoogleAccountLinkStore"
Cohesion: 0.05
Nodes (56): Dictionary, IDisposable, SemaphoreSlim, GateEntry, ReferenceCount, Semaphore, GoogleAccountOperationCoordinator, GateCount (+48 more)

### Community 16 - ".ConfigureServices"
Cohesion: 0.14
Nodes (17): CancellationTokenSource, ILogger, TimeSpan, ValueTask, GoogleAccountLinkService, GoogleMemberCleanupWakeupPublisher, GoogleUnlinkOperationCancellationFactory, IGoogleAccountLinkMetricsRefresher (+9 more)

### Community 17 - "GoogleOAuthOperationLockLease"
Cohesion: 0.09
Nodes (28): CancellationToken, CancellationTokenSource, IDatabase, RedisKey, RedisValue, Task, TimeSpan, ValueTask (+20 more)

### Community 18 - "BililiveRecorderWebHookEventData"
Cohesion: 0.12
Nodes (16): BililiveRecorderWebHookEventData, AreaNameChild, AreaNameParent, DanmakuConnected, Duration, FileCloseTime, FileOpenTime, FileSize (+8 more)

### Community 19 - "RedisConnection"
Cohesion: 0.33
Nodes (4): ConnectionMultiplexer, RedisConnection, Instance, Lazy

### Community 20 - "Discord Stream Bot Backend"
Cohesion: 0.25
Nodes (7): Discord Stream Bot Backend, Docker Compose 部署, OAuth 與 Webhook 網址, 主要 API, 本機開發, 第一次設定, 資料庫

### Community 21 - ".Callback"
Cohesion: 0.13
Nodes (12): CancellationToken, EnableCors, HttpPost, IActionResult, Task, DiscordAccessTokenData, AccessToken, ExpiresIn (+4 more)

### Community 22 - ".PublishAndWaitAsync"
Cohesion: 0.21
Nodes (12): CancellationToken, ILogger, Task, AdminSettingsRedisOutcome, DeadlineExceeded, Reply, Unavailable, AdminSettingsRedisResult (+4 more)

### Community 24 - "DiscordStreamBotBackend.Model"
Cohesion: 0.26
Nodes (4): DiscordStreamBotBackend.Model, DiscordStreamBotBackend.Tests, DiscordStreamBotBackend.Services.Auth, DiscordStreamBotBackend.DataBase

### Community 25 - "DiscordSessionPayload"
Cohesion: 0.22
Nodes (9): DateTime, DiscordSessionPayload, DiscordAccessToken, DiscordUserId, ExpiresAtUtc, IssuedAtUtc, ProviderExpiresAtUtc, Purpose (+1 more)

### Community 26 - "AdminSettingsSnapshotReply"
Cohesion: 0.13
Nodes (16): List, AdminSettingsSnapshotReply, Capabilities, Common, ContractVersion, Crawlers, Guild, Health (+8 more)

### Community 27 - "YouTubeNotificationsController"
Cohesion: 0.14
Nodes (17): CancellationToken, ContentResult, HttpGet, HttpPost, IActionResult, ILogger, Task, YouTubeNotificationsController (+9 more)

### Community 29 - "MainDbContext"
Cohesion: 0.15
Nodes (15): DbContext, DbContextOptions, DbSet, TwitchBroadcasterAuthorization, MainDbContext, GoogleOAuthUnlinkIntent, TwitchBroadcasterAuthorization, YoutubeChannelSpider (+7 more)

### Community 30 - "DiscordUser"
Cohesion: 0.12
Nodes (16): DiscordUser, accent_color, avatar, avatar_decoration, banner, banner_color, discriminator, email (+8 more)

### Community 31 - "TwitCastingWebHookController"
Cohesion: 0.29
Nodes (5): ContentResult, HttpPost, IConfiguration, ILogger, TwitCastingWebHookController

### Community 32 - "TwitchRefreshRotationLifecycle"
Cohesion: 0.06
Nodes (39): Action, DateTime, TwitchBroadcasterAuthorization, AuthorizedAt, ClientId, DateUpdated, DiscordUserId, DisplayName (+31 more)

### Community 33 - "AdminSettingsContractTests"
Cohesion: 0.28
Nodes (3): Fact, IConfiguration, AdminSettingsContractTests

### Community 34 - "LogMiddleware"
Cohesion: 0.25
Nodes (6): DiscordStreamBotBackend.Middleware, HttpContext, Logger, Task, LogMiddleware, RequestDelegate

### Community 35 - "YoutubeChannelSpider"
Cohesion: 0.25
Nodes (8): DateTime, YoutubeChannelSpider, ChannelId, ChannelTitle, DateAdded, GuildId, IsTrustedChannel, LastSubscribeTime

### Community 36 - "PublicUrlService"
Cohesion: 0.25
Nodes (6): PublicUrlService, ApiServerDomain, DiscordRedirectUrl, FrontendDomain, GoogleCallbackUrl, TwitchCallbackUrl

### Community 37 - "AccountLinksController"
Cohesion: 0.33
Nodes (7): CancellationToken, HttpGet, IActionResult, Task, AccountLinksController, HttpDelete, ObjectResult

### Community 38 - "TwitchOAuthController"
Cohesion: 0.23
Nodes (9): ControllerBase, CancellationToken, EnableCors, HttpGet, HttpPost, IActionResult, ILogger, Task (+1 more)

### Community 39 - "OAuthStateService"
Cohesion: 0.23
Nodes (7): OAuthStateData, DiscordUserId, FrontendDomain, Provider, Task, TimeSpan, OAuthStateService

### Community 40 - "DiscordStreamBotBackend.DataBase.Table"
Cohesion: 0.20
Nodes (6): DiscordStreamBotBackend.DataBase.Table, DateTime, GoogleOAuthUnlinkIntent, DateAdded, DiscordUserId, ExpectedEncryptedToken

### Community 41 - "BiliBiliGetRoomInfoJson.cs"
Cohesion: 0.18
Nodes (10): DiscordStreamBotBackend.Model.BiliBili, List, Badge, Desc, Name, Position, Value, StudioInfo (+2 more)

### Community 42 - "RedisChannels"
Cohesion: 0.20
Nodes (6): AdminSettings, Member, OAuth, RedisChannels, Twitch, YoutubeWebSub

### Community 43 - "AdminSettingsCommandReply"
Cohesion: 0.18
Nodes (11): JObject, AdminSettingsCommandReply, Arguments, Code, ContractVersion, CorrelationId, ShardId, State (+3 more)

### Community 44 - "AdminGuild"
Cohesion: 0.20
Nodes (9): AdminGuild, BotInstalled, Icon, Id, Name, Owner, Permissions, InlineData (+1 more)

### Community 45 - "YoutubeMemberAccessToken"
Cohesion: 0.33
Nodes (5): DateTime, YoutubeMemberAccessToken, DateAdded, DiscordUserId, EncryptedAccessToken

### Community 46 - "RandomVideoController"
Cohesion: 0.14
Nodes (11): Controller, EnableCors, HttpGet, IActionResult, IndexController, ILogger, RandomVideoController, ContentResult (+3 more)

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

### Community 52 - "CancellationToken"
Cohesion: 0.24
Nodes (7): CancellationToken, IDbContextFactory, IReadOnlyList, Task, GoogleAccountLinkStore, GoogleUnlinkPreparation, IGoogleAccountLinkStore

### Community 53 - "MemberData"
Cohesion: 0.22
Nodes (9): DateTime, MemberData, DiscordUserId, GoogleAccessToken, GoogleExpiresIn, GoogleRefrechToken, GoogleUserAvatar, GoogleUserName (+1 more)

### Community 54 - "YoutubeWebSubContract"
Cohesion: 0.13
Nodes (4): DiscordStreamBotBackend.YoutubeWebSub, YoutubeWebSubContract, InlineData, Theory

### Community 55 - "AdminSettingsRequestEnvelope"
Cohesion: 0.25
Nodes (8): AdminSettingsRequestEnvelope, Action, ActorUserId, ContractVersion, CorrelationId, DeadlineUnixMs, GuildId, Payload

### Community 56 - "Startup"
Cohesion: 0.25
Nodes (6): IConfiguration, Startup, Configuration, IApplicationBuilder, IWebHostEnvironment, LogMiddleware

### Community 57 - "DiscordStreamBotBackend"
Cohesion: 0.25
Nodes (5): Counter, DiscordStreamBotBackend, BackendMetrics, Gauge, Histogram

### Community 59 - "Utility"
Cohesion: 0.29
Nodes (4): HttpContext, Utility, TwitchWebHookSecret, IPAddress

### Community 60 - "BiliBiliGetRoomInfoJson"
Cohesion: 0.40
Nodes (5): BiliBiliGetRoomInfoJson, Code, Data, Message, Msg

### Community 61 - "NewPendants"
Cohesion: 0.40
Nodes (5): NewPendants, Badge, Frame, MobileBadge, MobileFrame

### Community 63 - "YoutubeWebSubChallengeAction"
Cohesion: 0.17
Nodes (12): TimeSpan, YoutubeWebSubChallengeAction, AlreadyConfirmed, ConfirmDenied, ConfirmSubscribe, ConfirmUnsubscribe, ServerError, YoutubeWebSubChallengeEvaluation (+4 more)

### Community 64 - "YoutubeWebSubPendingAction"
Cohesion: 0.15
Nodes (10): DateTime, YoutubeWebSubPendingAction, CallbackToken, ChannelId, ConfirmedAtUtc, DeniedAtUtc, Mode, RequestedAtUtc (+2 more)

### Community 65 - "Broadcaster"
Cohesion: 0.17
Nodes (12): Broadcaster, Created, Id, Image, IsLive, LastMovieId, Level, Name (+4 more)

### Community 66 - ".GetManageableGuildsAsync"
Cohesion: 0.21
Nodes (8): CancellationToken, DateTime, HttpClient, List, Task, DiscordGuildAuthorizationService, IEnumerable, IMemoryCache

### Community 67 - "BililiveRecorderWebHookController"
Cohesion: 0.24
Nodes (7): ContentResult, HttpClient, HttpPost, IConfiguration, ILogger, Task, BililiveRecorderWebHookController

### Community 68 - "GetLiveUserInfo"
Cohesion: 0.20
Nodes (10): GetLiveUserInfo, Exp, FollowerNum, GloryCount, Info, LinkGroupNum, MedalName, Pendant (+2 more)

### Community 69 - ".PublishAsync"
Cohesion: 0.46
Nodes (3): CancellationToken, Task, KeyValuePair

### Community 70 - "BililiveRecorderWebHookJson"
Cohesion: 0.29
Nodes (6): DateTime, BililiveRecorderWebHookJson, EventData, EventId, EventTimestamp, EventType

### Community 72 - "GoogleProviderRevokeOutcome"
Cohesion: 0.29
Nodes (7): GoogleProviderRevokeOutcome, Failed, NoGrant, Revoked, TokenChanged, TokenUnreadable, GoogleProviderRevokeResult

### Community 73 - "GuildListHandler"
Cohesion: 0.29
Nodes (6): HttpMessageHandler, HttpResponseMessage, CancellationToken, Task, GuildListHandler, RequestCount

### Community 74 - "TwitCastingWebHookJson"
Cohesion: 0.33
Nodes (5): DiscordStreamBotBackend.Model.TwitCasting, TwitCastingWebHookJson, Broadcaster, Movie, Signature

### Community 75 - "DiscordOAuthController"
Cohesion: 0.33
Nodes (5): HttpClient, IConfiguration, ILogger, DiscordOAuthController, HttpStatusCode

### Community 76 - "TwitchAccountLink"
Cohesion: 0.33
Nodes (6): TwitchAccountLink, DisplayName, ProfileImageUrl, Status, TwitchUserId, UserLogin

### Community 77 - "MasterLevel"
Cohesion: 0.33
Nodes (6): List, MasterLevel, Color, Current, Level, Next

### Community 78 - "Info"
Cohesion: 0.33
Nodes (6): Info, Face, Gender, OfficialVerify, Uid, Uname

### Community 79 - "BiliBiliGetLiveUserInfoJson"
Cohesion: 0.40
Nodes (5): BiliBiliGetLiveUserInfoJson, Code, Data, Message, Msg

### Community 80 - "GoogleUnlinkResult"
Cohesion: 0.40
Nodes (5): GoogleUnlinkResult, CleanupPending, ProviderRevokeFailed, TokenChanged, Unlinked

### Community 81 - "GoogleUnlinkTransitionOutcome"
Cohesion: 0.67
Nodes (3): GoogleUnlinkTransitionOutcome, Committed, TokenChanged

### Community 82 - "YTNotificationType"
Cohesion: 0.67
Nodes (3): YTNotificationType, CreateOrUpdated, Deleted

## Knowledge Gaps
- **464 isolated node(s):** `TwitchBroadcasterAuthorization`, `GoogleOAuthUnlinkIntent`, `YoutubeChannelSpider`, `YoutubeMemberAccessToken`, `YoutubeMemberCheck` (+459 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 646 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **6 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `DiscordStreamBotBackend.Model` connect `DiscordStreamBotBackend.Model` to `TwitchAuthorizationService`, `.GetManageableGuildsAsync`, `BililiveRecorderWebHookController`, `BililiveRecorderWebHookJson`, `OAuthStateService`, `GoogleAccountLink`, `GoogleJson.cs`, `.ConfigureServices`, `.Callback`, `.PublishAndWaitAsync`, `DiscordStreamBotBackend.Services`, `AdminSettingsSnapshotReply`?**
  _High betweenness centrality (0.210) - this node is a cross-community bridge._
- **Why does `TwitchAuthorizationService` connect `TwitchAuthorizationService` to `TwitchRefreshRotationLifecycle`, `RedisService`, `PublicUrlService`, `AccountLinksController`, `TwitchOAuthController`, `GoogleOAuthService`, `TwitchOAuthRefreshLockLease`, `.ConfigureServices`, `GoogleOAuthOperationLockLease`, `TokenService`, `MainDbContext`?**
  _High betweenness centrality (0.156) - this node is a cross-community bridge._
- **Why does `DiscordStreamBotBackend.Services` connect `DiscordStreamBotBackend.Services` to `TwitchAuthorizationService`, `LogMiddleware`, `.GetManageableGuildsAsync`, `PublicUrlService`, `GoogleOAuthService`, `OAuthStateService`, `TwitchOAuthRefreshLockLease`, `EventSubHostedService`, `FakeGoogleAccountLinkStore`, `.ConfigureServices`, `GoogleOAuthOperationLockLease`, `.PublishAndWaitAsync`, `YoutubeWebSubContract`, `DiscordStreamBotBackend.Model`, `Startup`, `YouTubeNotificationsController`?**
  _High betweenness centrality (0.148) - this node is a cross-community bridge._
- **What connects `TwitchBroadcasterAuthorization`, `GoogleOAuthUnlinkIntent`, `YoutubeChannelSpider` to the rest of the system?**
  _464 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `TwitchAuthorizationService` be split into smaller, more focused modules?**
  _Cohesion score 0.051666372773761245 - nodes in this community are weakly interconnected._
- **Should `RedisService` be split into smaller, more focused modules?**
  _Cohesion score 0.11764705882352941 - nodes in this community are weakly interconnected._
- **Should `YoutubePubSubNotification` be split into smaller, more focused modules?**
  _Cohesion score 0.10080645161290322 - nodes in this community are weakly interconnected._