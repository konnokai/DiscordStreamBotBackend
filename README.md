# Discord Stream Bot Backend

這是直播小幫手的 ASP.NET Core 後端，負責 Discord 登入、Google／Twitch 帳號連結、webhook 接收，以及網站管理設定 API。

後端不能單獨提供完整服務。部署前請先準備：

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，或 Docker 與 Docker Compose
- MySQL 或 MariaDB
- Redis
- Discord、Google 與 Twitch OAuth 應用程式
- 可公開連線的 HTTPS 後端網域
- 前端公開網域
- 已由 [Bot 儲存庫](https://github.com/konnokai/DiscordStreamNotifyBot) migration 建立的資料表

## 第一次設定

建議從正式環境範例建立自己的設定檔：

```powershell
Copy-Item config/appsettings.Production.example.json config/appsettings.Production.json
```

Linux 或 macOS：

```sh
cp config/appsettings.Production.example.json config/appsettings.Production.json
```

編輯 `config/appsettings.Production.json`，將所有 `CHANGE_ME`、`REPLACE_WITH` 及範例網域換成自己的值。

| 設定 | 說明 |
|---|---|
| `ConnectionStrings:MySql` | 與 Bot 共用的 MySQL 資料庫 |
| `ConnectionStrings:Redis` | 與 Bot 共用的 Redis |
| `FrontendDomain` | 前端公開來源，例如 `https://bot.example.com`，不可有結尾 `/` |
| `ApiServerDomain` | 後端公開來源，例如 `https://api.example.com`，不可有結尾 `/` |
| `Discord:ClientId`、`Discord:ClientSecret` | Discord OAuth 應用程式資料 |
| `Google:ClientId`、`Google:ClientSecret` | Google OAuth 應用程式資料 |
| `Twitch:ClientId`、`Twitch:ClientSecret` | Twitch OAuth 應用程式資料 |
| `Twitch:WebHookSecret` | Twitch EventSub webhook secret |
| `TwitCasting:WebHookSignature` | 啟用 TwitCasting webhook 時使用的簽章 |
| `Token:Frontend` | 簽發前端 session 的金鑰，至少 64 個字元 |
| `Token:ProviderTokenEncryptionKey` | 加密 provider token 的金鑰，至少 64 個字元，必須與 Bot 相同 |

設定檔已被 Git 忽略。不要提交 OAuth Secret、資料庫密碼或 token 金鑰。

## OAuth 與 Webhook 網址

假設前端為 `https://bot.example.com`，後端為 `https://api.example.com`，請在各平台登記：

| 平台 | Redirect／Callback URI |
|---|---|
| Discord | `https://bot.example.com/` |
| Google | `https://api.example.com/oauth/google/callback` |
| Twitch OAuth | `https://api.example.com/oauth/twitch/callback` |
| Twitch EventSub | `https://api.example.com/TwitchWebHooks` |
| TwitCasting | `https://api.example.com/TwitCastingWebHook` |

Discord OAuth scope 必須包含 `identify guilds`。

後端啟動時會確認 `Twitch:WebHookSecret` 與 Redis DB 0 的 `twitch:webhook_secret` 相同。第一次部署可執行：

```sh
redis-cli -n 0 SET twitch:webhook_secret "你的 Twitch webhook secret"
```

## 資料庫

資料表與 migration 由 Bot 儲存庫管理。先在 Bot 儲存庫匯入 `migrate_sql/all.sql`，再啟動後端。

後端只映射既有 schema，不會自動建立或更新資料表。Bot 與後端必須使用相同的 `ProviderTokenEncryptionKey`，才能讀取彼此寫入的 OAuth token。

## Docker Compose 部署

Compose 只啟動後端，不會建立 MySQL、Redis 或資料表。範例設定預設以 `host.docker.internal` 連到 Docker 主機上的 MySQL 與 Redis。

```sh
docker compose up -d --build
docker compose logs -f backend
```

預設公開 `5003` 連接埠。要改主機連接埠，可設定 `BACKEND_PORT`：

```powershell
$env:BACKEND_PORT = "15003"
docker compose up -d --build
```

修改掛載的設定檔後，重新啟動服務：

```sh
docker compose restart backend
```

正式環境應在後端前方放置反向代理並啟用 HTTPS。對外連接埠只允許可信任的反向代理存取，避免使用者偽造 forwarded header。

## 本機開發

建置與測試：

```powershell
dotnet build DiscordStreamBotBackend.sln -c Release
dotnet test DiscordStreamBotBackend.sln -c Release
```

開發環境可用 [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) 覆寫 `DiscordStreamBotBackend/appsettings.json`，避免把機密寫進儲存庫。例如：

```powershell
dotnet user-secrets set "ConnectionStrings:MySql" "Server=localhost;Port=3306;User Id=stream_bot;Password=你的密碼;Database=discord_stream_bot" --project DiscordStreamBotBackend
dotnet user-secrets set "ConnectionStrings:Redis" "127.0.0.1,syncTimeout=3000" --project DiscordStreamBotBackend
```

其餘必填鍵請依 `config/appsettings.Production.example.json` 設定，再執行：

```powershell
dotnet run --project DiscordStreamBotBackend
```

## 主要 API

| Method | Path | 用途 |
|---|---|---|
| `POST` | `/oauth/discord/callback` | 以 Discord authorization code 建立 session |
| `POST` | `/oauth/google/start` | 開始 Google OAuth |
| `GET` | `/oauth/google/callback` | 接收 Google callback |
| `POST` | `/oauth/twitch/start` | 開始 Twitch OAuth |
| `GET` | `/oauth/twitch/callback` | 接收 Twitch callback |
| `GET`／`POST` | `/NotificationCallback` | 接收 YouTube PubSubHubbub callback |
| `POST` | `/TwitCastingWebHook` | 接收 TwitCasting webhook |
| `GET` | `/account-links` | 查詢帳號連結狀態 |
| `DELETE` | `/account-links/google` | 解除 Google 連結 |
| `DELETE` | `/account-links/twitch` | 解除 Twitch 連結 |
| `GET` | `/admin/guilds` | 列出目前使用者可管理的 Discord 伺服器 |
| `GET` | `/admin/guilds/{guildId}/settings` | 取得伺服器設定快照 |
| `POST` | `/admin/guilds/{guildId}/commands` | 提交單一設定異動 |
| `GET` | `/metrics` | Prometheus 指標 |

需要登入的 API 使用 `Authorization: Bearer <DT>`。`guildId` 必須保留為字串，前端不可轉成 JavaScript `number`。
