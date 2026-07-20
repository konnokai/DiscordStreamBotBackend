# Discord Stream Bot Backend

此專案目標為 .NET 8 (`net8.0`)；可直接以 .NET 8 SDK 建置，或使用本專案提供的 Docker Compose 部署後端。

## Docker Compose 部署

Compose 只啟動 ASP.NET Core 後端，不會建立或管理 MySQL、Redis、volume、migration 或資料表。

部署前請確認：

1. Docker 與 Docker Compose 已安裝。
2. MySQL 與 Redis 已在 Docker 主機上運行並發布可連線的連接埠。
3. MySQL 使用者允許容器來源連線，且既有 database/schema/table 已存在。
4. 已準備 Discord、Google、Twitch OAuth 與 webhook 所需的設定值。
5. `Token:Frontend` 與 `Token:Redis` 都至少為 64 個字元；`Token:Redis` 必須沿用可解密既有 OAuth token 的共享金鑰。

此次 OAuth 契約與前端網域切換不保留舊版本相容性，必須在維護窗口同步啟用 Backend、Cloudflare Pages Frontend 與 Bot 新連結。部署 Backend 時請旋轉 `Token:Frontend`，讓歷史長效 DT 立即失效；新的 DT 是 12 小時短效 session，Frontend 只應將其視為 opaque token。`Token:Redis` 不可跟著旋轉，否則既有 provider token 將無法解密。

建立實際部署設定。此檔案已被 Git 忽略，且只會以唯讀方式掛載到容器：

```sh
cp config/appsettings.Production.example.json config/appsettings.Production.json
```

PowerShell 可使用：

```powershell
Copy-Item config/appsettings.Production.example.json config/appsettings.Production.json
```

編輯 `config/appsettings.Production.json`，填入所有 `CHANGE_ME` 值與實際連線資訊。預設設定會讓 Kestrel 綁定 `0.0.0.0:5003`，並透過 `host.docker.internal:3306` 與 `host.docker.internal:6379` 連線至主機上的 MySQL 與 Redis。外部服務使用不同連接埠或需要其他 Redis 連線選項時，請直接修改此檔案，不需修改 Compose。

啟動後端：

```sh
docker compose up -d --build
```

預設會將主機的 5003 發布到容器的 5003。若只需變更主機發布埠，可設定 `BACKEND_PORT`，此變數不承載任何應用程式機密：

```sh
BACKEND_PORT=15003 docker compose up -d --build
```

PowerShell 可使用：

```powershell
$env:BACKEND_PORT = "15003"
docker compose up -d --build
```

常用操作：

```sh
docker compose logs -f backend
docker compose restart backend
docker compose down
```

Linux 會由 Compose 的 `host-gateway` 映射解析 `host.docker.internal`；Docker Desktop 可直接使用此主機名稱。MySQL 或 Redis 尚未就緒或無法連線時，後端會退出，Compose 的 `unless-stopped` restart policy 會持續重試。

修改掛載的 production 設定後，請執行 `docker compose restart backend`。TwitCasting webhook URL 為 `https://[後端域名]/TwitcastingWebHook`。

## 公開網域與 OAuth URI

設定檔只填公開網域，不填 callback path，也不可包含結尾 `/`：

| 欄位 | 正式值 | 用途 |
|---|---|---|
| `FrontendDomain` | `https://stream-bot.konnokai.me` | Cloudflare Pages 前端公開網域、CORS、Discord callback、OAuth 完成返回頁面 |
| `ApiServerDomain` | `https://api.konnokai.me` | Backend 公開網域，產生 Google/Twitch callback URL |

正式環境必須在 Provider Console 登記以下完整 URI：

| Provider | 完整 URI |
|---|---|
| Discord Developer Portal | `https://stream-bot.konnokai.me/` |
| Google Cloud Console | `https://api.konnokai.me/oauth/google/callback` |
| Twitch Developer Console | `https://api.konnokai.me/oauth/twitch/callback` |

本機前端可使用 `http://localhost:3333`；其他環境必須使用 HTTPS absolute URI。Backend 啟動時會驗證網域格式，並確認設定的 `Twitch:WebHookSecret` 與 Redis DB 0 的 `twitch:webhook_secret` 完全一致。

應用層 rate limit 只使用 ASP.NET Core 正規化後的 `HttpContext.Connection.RemoteIpAddress`，不會直接信任 `CF-Connecting-IP` 或 `X-Forwarded-For`。若 Backend 位於反向代理後方，必須在程式中啟用 Forwarded Headers middleware，並以 `ForwardedHeadersOptions.KnownProxies` 或 `KnownNetworks` 明確列出可信代理；未設定可信代理前，不應啟用轉送 header。

`twitch_broadcaster_authorization` 的 migration 由 Bot repo 統一管理，Backend 只映射既有資料表，不會建立或更新 schema。

Backend 每次啟動、HTTP 開始服務前，會以 Redis `SCAN` 尋找 DB 1 的舊 `twitch:oauth:{discordUserId}` token。可驗證或成功刷新的 token 會寫入 `twitch_broadcaster_authorization`；refresh rotation 會先以 compare-and-set 保存回 Redis，MySQL 寫入及授權狀態提示入列後再 compare-and-delete 舊 key。暫時失敗、解密失敗、Client ID/scope 不符或帳號衝突時會保留舊 key，供下次 Backend 啟動重試或人工確認。hourly token validation 不再掃描 legacy key，避免與新 OAuth 或解除連結競態；legacy token 可由 account links API 顯示為失效狀態並由使用者重試解除。

部署時必須沿用既有 `Token:Redis`，且先套用 Bot repo 提供的 migration SQL。Backend 不支援新舊版本同時寫入 legacy key；停止舊 Backend 後才能啟動新版，並在部署 Frontend 前確認 log 顯示 legacy token 已完成遷移或已由維運者處理。未完成的 legacy key 不會在服務運行期間自動遷移。MySQL 已有同帳號 row 時一律視為較新的權威狀態，不會被 legacy token 覆寫；若部署時掃到超過 1 筆 legacy key，為避免 SCAN 順序造成錯誤綁定，Backend 會停止自動遷移並要求人工確認。正式環境目前確認只有 1 筆 legacy key，但仍須以部署時的 Redis `SCAN` 結果為準。

OAuth 與帳號連結 API：

| Method | Path | 說明 |
|---|---|---|
| `POST` | `/oauth/discord/callback` | 以前端收到的 Discord authorization code 換取 Discord session token；code 放在 JSON body |
| `POST` | `/oauth/google/start` | 以 `Authorization: Bearer <DT>` 建立 Google OAuth 流程 |
| `GET` | `/oauth/google/callback` | Google provider callback |
| `POST` | `/oauth/twitch/start` | 以 `Authorization: Bearer <DT>` 建立 Twitch OAuth 流程 |
| `GET` | `/oauth/twitch/callback` | Twitch provider callback |
| `GET` | `/account-links` | 查詢 Google 與 Twitch 連結狀態 |
| `DELETE` | `/account-links/google` | 解除 Google 連結 |
| `DELETE` | `/account-links/twitch` | 解除 Twitch 連結 |

舊的 `/DiscordCallBack`、`/GoogleCallBack`、`/GetGoogleData`、`/UnlinkGoogle` 端點已移除，不提供向前相容。

## Prometheus

Backend 在既有 HTTP server 提供 `/metrics`。此路徑不會進入一般 access log、錯誤計數或 rate limit。Prometheus scrape 範例：

```yaml
- job_name: discord-stream-backend
  metrics_path: /metrics
  static_configs:
    - targets: ['backend:5003']
```

提供 OAuth linked account、token validation/refresh、Twitch Webhook 接收與 queue 狀態等低基數指標。

## 直接執行

請安裝 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，再執行：

```sh
dotnet build DiscordStreamBotBackend.sln -c Release
```
