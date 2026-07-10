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

修改掛載的 production 設定後，請執行 `docker compose restart backend`。OAuth redirect URL、webhook URL 與任何反向代理設定仍必須指向實際公開網址；TwitCasting webhook URL 為 `https://[後端域名]/TwitcastingWebHook`。

## 直接執行

請安裝 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，再執行：

```sh
dotnet build DiscordStreamBotBackend.sln -c Release
```
