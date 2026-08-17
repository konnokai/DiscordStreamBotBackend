# Discord Stream Bot Backend

本專案使用 .NET 8 (`net8.0`)，可直接使用 .NET 8 SDK 建置，或用本專案提供的 Docker Compose 部署後端。

## Docker Compose 部署

Compose 只啟動 ASP.NET Core 後端，不會建立或管理 MySQL、Redis、volume、migration 或資料表。

部署前請確認：

1. Docker 與 Docker Compose 已安裝。
2. MySQL 與 Redis 已在 Docker 主機上執行，並開放可連線的連接埠。
3. MySQL 使用者允許來自容器的連線，且既有 database/schema/table 已存在。
4. 已準備 Discord、Google、Twitch OAuth 與 webhook 所需的設定值。
5. `Token:Frontend` 與 `Token:ProviderTokenEncryptionKey` 都至少為 64 個字元；`Token:ProviderTokenEncryptionKey` 必須沿用可解密既有 OAuth token 的共享金鑰，只改設定欄位名稱，不可變更正式金鑰值。

這次 OAuth 契約與前端網域切換不保留舊版相容性，請在維護窗口同時部署後端、Cloudflare Pages 前端與 Bot 的新連結設定。部署後端時請旋轉 `Token:Frontend`，讓歷史長效 DT 立即失效；新的 DT 是 12 小時短效 session，前端只應將它視為 opaque token。`Token:ProviderTokenEncryptionKey` 不可跟著旋轉，否則既有 provider token 將無法解密。後端只讀取這個 provider token 金鑰欄位，部署設定必須一次完成改名，避免同時存在兩個金鑰來源。

建立正式部署設定。此檔案已被 Git 忽略，且只會以唯讀方式掛載到容器：

```sh
cp config/appsettings.Production.example.json config/appsettings.Production.json
```

PowerShell 可執行：

```powershell
Copy-Item config/appsettings.Production.example.json config/appsettings.Production.json
```

編輯 `config/appsettings.Production.json`，填入所有 `CHANGE_ME` 值與實際連線資訊。預設設定會讓 Kestrel 綁定 `0.0.0.0:5003`，並透過 `host.docker.internal:3306` 與 `host.docker.internal:6379` 連線到主機上的 MySQL 與 Redis。若外部服務使用不同連接埠或需要其他 Redis 連線選項，直接修改此檔案，不必修改 Compose。

啟動後端：

```sh
docker compose up -d --build
```

預設會將主機的 5003 發布到容器的 5003。若只需變更主機發布埠，可設定 `BACKEND_PORT`，此變數不包含任何應用程式機密：

```sh
BACKEND_PORT=15003 docker compose up -d --build
```

PowerShell 可執行：

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

Linux 會由 Compose 的 `host-gateway` 映射解析 `host.docker.internal`；Docker Desktop 可直接使用此主機名稱。MySQL 或 Redis 尚未就緒或無法連線時，後端會退出，Compose 的 `unless-stopped` 重啟策略會持續重試。

修改掛載的 production 設定後，請執行 `docker compose restart backend`。TwitCasting webhook URL 為 `https://[後端網域]/TwitcastingWebHook`。

## 公開網域與 OAuth URI

設定檔只填公開網域，不填 callback path，也不可包含結尾 `/`：

| 欄位 | 正式值 | 用途 |
|---|---|---|
| `FrontendDomain` | `https://stream-bot.konnokai.me` | Cloudflare Pages 前端公開網域、CORS、Discord callback、OAuth 完成返回頁面 |
| `ApiServerDomain` | `https://api.konnokai.me` | 後端公開網域，產生 Google/Twitch callback URL |

正式環境必須在 Provider 管理主控台登記以下完整 URI：

| Provider | 完整 URI |
|---|---|
| Discord Developer Portal | `https://stream-bot.konnokai.me/` |
| Google Cloud Console | `https://api.konnokai.me/oauth/google/callback` |
| Twitch Developer Console | `https://api.konnokai.me/oauth/twitch/callback` |

本機前端可使用 `http://localhost:3333`；其他環境必須使用 HTTPS 絕對 URI。後端啟動時會驗證網域格式，並確認設定的 `Twitch:WebHookSecret` 與 Redis DB 0 的 `twitch:webhook_secret` 完全一致。

應用層 rate limit 與 access log 使用 ASP.NET Core Forwarded Headers middleware 正規化後的 `HttpContext.Connection.RemoteIpAddress`。預設從 Cloudflare 的 `CF-Connecting-IP` 取得原始 IP；若反向代理送的是 `CF-Real-IP` 或 `X-Forwarded-For`，請修改 `ForwardedHeaders:ForwardedForHeaderName`。只有 `ForwardedHeaders:KnownProxies` 或 `KnownNetworks` 內的可信代理能改寫 IP，Docker 預設私有網段可設定為 `172.16.0.0/12`，實際部署仍應依 `docker network inspect` 結果縮小範圍。對外發布的後端連接埠必須限制為只能由反向代理存取，否則使用者可直接連線並偽造轉送 header。

共享 OAuth 與會員驗證資料表的 migration 由 Bot repo 統一管理，後端只映射既有 schema，不會建立或更新資料表。`youtube_member_check` 包含 `pending_role_removal`，並沿用 Bot migration 建立的 `(pending_role_removal, guild_id)`、`(user_id, pending_role_removal)` 與唯一 `(guild_id, user_id, check_yt_channel_id(24))` 索引；後端 repo 不建立對應 migration。

部署前須先套用 Bot repo 提供的 migration SQL。Twitch provider token 僅保存於 `twitch_broadcaster_authorization`，後端不讀寫 Redis token，也不提供舊版 Redis token 遷移流程。

Discord 登入 scope 必須包含 `identify guilds`。新的 DT 加密 payload 會保留 Discord access token 與 provider 到期時間，但前端只能取得並回傳 opaque DT，不會取得明文 provider token。DT 到期時間取 12 小時與 Discord `expires_in` 的較早者，且不保存 Discord refresh token。管理 API 每次 GET／POST 都會重新呼叫 Discord `/users/@me/guilds`，只接受 guild owner 或 permissions 包含 `ADMINISTRATOR` (`8`) 的使用者；Redis guild snapshot 僅用於顯示 Bot 是否已加入，不作為授權依據。

MySQL Twitch token 的定期 refresh、解除連結與 pending revocation 重試，會與 Bot 共用 Redis DB 1 的 `twitch:oauth:refresh-lock:{twitchUserId}` 分散式鎖。鎖使用唯一 owner、TTL、背景續租及 Lua owner check；每次 MySQL 狀態寫入前都會確認 owner，失去 ownership 的過期持有者不得覆寫資料。取得鎖後會重新讀取 MySQL 資料列並驗證最新 access token。鎖競爭或 Redis 暫時錯誤只會延後處理並保留現有授權狀態，不會因此撤銷授權。

refresh rotation 產生的新 token 會先以舊密文作 compare-and-set 條件寫回 MySQL，遇到暫時失敗會以 fresh DbContext 指數退避重試 6 次；仍失敗時，後端會在記憶體保留加密後的新 token、持續續租 refresh lock，並每 30 秒重試，成功寫入後才釋放鎖。這避免正常運作中的短暫 MySQL 故障讓其他實例拿舊 refresh token 再次刷新。若 process 在 MySQL 故障期間同時崩潰，現有 schema 無法跨 Twitch 與 MySQL 做原子提交；要封閉這個剩餘風險窗口，必須新增 MySQL recovery/outbox 欄位或資料表，不會把 provider token 重新存回 Redis。

解除 Twitch 連結會先將 MySQL 資料列標記為 `revocation_pending`，再嘗試取得 refresh lock；因此 lock contention 回傳 HTTP 202 前一定已有排程可重試的資料。低流量的 `twitch:authorization_changed` 不再經過一般 bounded webhook queue，而是直接發布；所有 `RevokedAt` 資料列仍會在啟動時及每小時 replay invalidation。Redis 暫時失敗、沒有 subscriber，或 process 在 MySQL commit 後、發布前崩潰時，MySQL 資料列本身就是重播來源，不需要新增 Bot-to-Backend endpoint。

`tests/DiscordStreamBotBackend.Tests` 覆蓋兩端可在單元層確認的 key、channel、JSON、token encryption 與 startup config 契約。unlink/refresh contention、lease expiry/renewal、MySQL 暫時故障及 invalidation replay 仍需使用真實 MySQL + Redis 的整合環境驗證；刻意不使用 EF Core InMemory provider，因為它不會驗證這些 SQL/鎖語意。

OAuth 與帳號連結 API：

| Method | Path | 說明 |
|---|---|---|
| `POST` | `/oauth/discord/callback` | 使用前端收到的 Discord authorization code 換取 Discord session token；code 放在 JSON body |
| `POST` | `/oauth/google/start` | 以 `Authorization: Bearer <DT>` 建立 Google OAuth 流程 |
| `GET` | `/oauth/google/callback` | Google provider 的 callback |
| `POST` | `/oauth/twitch/start` | 以 `Authorization: Bearer <DT>` 建立 Twitch OAuth 流程 |
| `GET` | `/oauth/twitch/callback` | Twitch provider 的 callback |
| `GET` | `/account-links` | 查詢 Google 與 Twitch 連結狀態 |
| `DELETE` | `/account-links/google` | 解除 Google 連結 |
| `DELETE` | `/account-links/twitch` | 解除 Twitch 連結 |
| `GET` | `/admin/guilds` | 列出使用者可管理的 Discord guild，以及 Bot 是否已安裝 |
| `GET` | `/admin/guilds/{guildId}/settings` | 完成即時授權後，向負責該 guild 的 Notifier 取得設定快照 |
| `POST` | `/admin/guilds/{guildId}/commands` | 完成即時授權後提交單一設定命令；請求內容為 `{action,payload}` |

上述管理 API 使用 `Authorization: Bearer <DT>`，並套用既有 `frontend` CORS policy（GET／POST、`Content-Type`、`Authorization`）。設定快照與命令直接使用 Redis request/reply channel，不會進入一般通知重試佇列；後端會先訂閱 correlation reply channel 再發布。沒有 subscriber、負責該 guild 的 Notifier 未回覆或 reply 無效時回傳 HTTP 503 與 `state=unknown`、`code=settings.unavailable`，不得自動重送設定異動，只能重新取得快照確認結果。

Google account-link 回應的 `status` 只表示 OAuth 狀態，值維持 `linked | unlinked | invalid`。Discord 身分組清理狀態由 `cleanupPending` 與各 subscription 的 `pendingRoleRemoval` 分開表示。`guildId` 一律回傳十進位字串，前端必須以 `string` 接收，不可轉成 JavaScript `number`：

```json
{
  "google": {
    "status": "unlinked",
    "subscriptions": [
      {
        "guildId": "18446744073709551615",
        "channelId": "UC...",
        "isChecked": false,
        "pendingRoleRemoval": true,
        "lastCheckedAt": "2026-08-04T12:00:00Z"
      }
    ],
    "cleanupPending": true
  }
}
```

Google 已連結時，`GET /account-links` 回傳該 Discord user 的所有 subscription 資料列；未連結或 token 無效時，仍回傳待清理資料列。刪除 token 不會讓 `cleanupPending` 消失。

`DELETE /account-links/google` 先撤銷 Google provider token。撤銷失敗時不修改本機 token 或 check 資料列，並回傳 503。撤銷成功後，後端在同一個 MySQL transaction 以原始 token 密文作 CAS，將該 user 的 checks 設為 `is_checked=false`、`pending_role_removal=true`，同時刪除本機 token；若操作期間 token 已被新連結取代，回傳 409 並保留新 token/check state。commit 完成後才發布 `member.revokeToken`，payload 維持 Discord user ID 十進位字串。Redis 訊息只負責喚醒 Bot，傳送失敗不會推翻已提交的 unlink。存在待清理資料列時回傳 HTTP 202，否則回傳 HTTP 200；兩者 body 都是 `{"status":"unlinked","cleanupPending":boolean}`。

舊的 `/DiscordCallBack`、`/GoogleCallBack`、`/GetGoogleData`、`/UnlinkGoogle` 端點已移除，不提供舊版相容性。

## Prometheus

後端在既有 HTTP server 提供 `/metrics`。此路徑不會進入一般 access log、錯誤計數或 rate limit。Prometheus 抓取設定範例：

```yaml
- job_name: discord-stream-backend
  metrics_path: /metrics
  static_configs:
    - targets: ['backend:5003']
```

提供已連結帳號、Token 驗證／更新、Twitch Webhook 收件與佇列狀態等低基數指標。

## 直接執行

請安裝 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，再執行：

```sh
dotnet build DiscordStreamBotBackend.sln -c Release
```
