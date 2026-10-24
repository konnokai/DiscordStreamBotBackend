## Agent skills

### Issue tracker

Issue 記在 GitHub Issues（`konnokai/DiscordStreamBotBackend`），用 `gh` CLI 操作。See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context：根目錄一份 `CONTEXT.md`，ADR 放在 `docs/adr/`。See `docs/agents/domain.md`.

## Twitch OAuth 已定案的設計

使用者要求修改以下行為時，先引用這裡的理由回覆，等使用者看過後明確確認，才動手。

### 定期驗證維持每小時一次

`TwitchTokenValidationHostedService` 每小時呼叫一次 `/oauth2/validate`，這是 Twitch 的硬性規定：「啟動時驗證一次，之後每小時驗證一次」（https://dev.twitch.tv/docs/authentication/validate-tokens/）。Twitch 會抽查，不照做可能被撤銷 API key。log 太多的問題用 `BackendLogging.cs` 的 `MinimumLevel.Override` 處理，週期維持不變。

### revoked 資料列保留在資料庫

`TwitchBroadcasterAuthorization` 被標記 revoked 後，資料列保留：

- `/validate` 回 401 通常只代表 access token 過期（約 4 小時），流程會先 refresh；refresh 也失敗才標記 revoked。401 本身不能當成撤銷的證據。
- `ValidateAllAsync` 只驗證 `RevokedAt == null` 的資料列，revoked 資料列不會再呼叫 Twitch API。
- Bot 收到 `twitch:authorization_changed` 的 invalid 事件後，會從 MySQL 讀這筆資料列找出 Discord 使用者，再清掉訂閱身分組（Bot 的 `TwitchSubscriptionService.HandleAuthorizationChangedAsync`）。`ReplayInvalidatedAuthorizationsAsync` 也靠這些資料列補送遺失的事件。資料列被刪掉，使用者就會一直留著身分組。

要清理舊資料時，做法是「revoked 超過一段寬限期後才刪」，並且 Bot 與 Backend 兩邊一起改。
