using Newtonsoft.Json;
using NLog;
using System;
using System.IO;

public class ServerConfig
{
    public string BililiveWebHookUrl { get; set; } = "";
    public string DiscordClientId { get; set; } = "";
    public string DiscordClientSecret { get; set; } = "";
    public string GoogleClientId { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";
    public string TwitchClientId { get; set; } = "";
    public string TwitchClientSecret { get; set; } = "";
    public string FrontendDomain { get; set; } = "";
    public string ApiServerDomain { get; set; } = "";
    public string RedisOption { get; set; } = "127.0.0.1,syncTimeout=3000";
    public string TokenKey { get; set; } = "";
    public string RedisTokenKey { get; set; } = "";

    private readonly Logger _logger = LogManager.GetLogger("Conf");

    public void InitServerConfig()
    {
        try { File.WriteAllText("server_config_example.json", JsonConvert.SerializeObject(new ServerConfig(), Formatting.Indented)); } catch { }
        if (!File.Exists("server_config.json"))
        {
            _logger.Error($"server_config.json 遺失，請依照 {Path.GetFullPath("server_config_example.json")} 內的格式填入正確的數值");
            if (!Console.IsInputRedirected)
                Console.ReadKey();
            Environment.Exit(3);
        }

        var config = JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText("server_config.json"));

        try
        {
            if (string.IsNullOrWhiteSpace(config.DiscordClientId))
            {
                _logger.Error($"{nameof(DiscordClientId)} 遺失，請輸入至 server_config.json 後重開伺服器");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }
            if (string.IsNullOrWhiteSpace(config.DiscordClientSecret))
            {
                _logger.Error($"{nameof(DiscordClientSecret)} 遺失，請輸入至 server_config.json 後重開伺服器");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            if (string.IsNullOrWhiteSpace(config.GoogleClientId))
            {
                _logger.Error($"{nameof(GoogleClientId)} 遺失，請輸入至 server_config.json 後重開伺服器");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            if (string.IsNullOrWhiteSpace(config.GoogleClientSecret))
            {
                _logger.Error($"{nameof(GoogleClientSecret)} 遺失，請輸入至 server_config.json 後重開伺服器");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            if (string.IsNullOrWhiteSpace(config.TwitchClientId))
            {
                _logger.Error($"{nameof(TwitchClientId)} 遺失，請輸入至 server_config.json 後重開伺服器");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            if (string.IsNullOrWhiteSpace(config.TwitchClientSecret))
            {
                _logger.Error($"{nameof(TwitchClientSecret)} 遺失，請輸入至 server_config.json 後重開伺服器");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            if (string.IsNullOrWhiteSpace(config.FrontendDomain))
            {
                _logger.Error($"{nameof(FrontendDomain)} 遺失，請輸入至 server_config.json 後重開伺服器");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            if (string.IsNullOrWhiteSpace(config.ApiServerDomain))
            {
                _logger.Error($"{nameof(ApiServerDomain)} 遺失，請輸入至 server_config.json 後重開伺服器");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            if (string.IsNullOrWhiteSpace(config.RedisOption))
            {
                _logger.Error($"{nameof(RedisOption)} 遺失，請輸入至 server_config.json 後重開伺服器");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            BililiveWebHookUrl = config.BililiveWebHookUrl;
            DiscordClientId = config.DiscordClientId;
            DiscordClientSecret = config.DiscordClientSecret;
            GoogleClientId = config.GoogleClientId;
            GoogleClientSecret = config.GoogleClientSecret;
            TwitchClientId = config.TwitchClientId;
            TwitchClientSecret = config.TwitchClientSecret;
            FrontendDomain = config.FrontendDomain;
            ApiServerDomain = config.ApiServerDomain;
            RedisOption = config.RedisOption;
            TokenKey = config.TokenKey;
            RedisTokenKey = config.RedisTokenKey;

            if (string.IsNullOrWhiteSpace(config.TokenKey) || string.IsNullOrWhiteSpace(TokenKey))
            {
                _logger.Error($"{nameof(TokenKey)} 遺失，將重新建立隨機亂數");

                TokenKey = GenRandomKey();

                try { File.WriteAllText("server_config.json", JsonConvert.SerializeObject(this, Formatting.Indented)); }
                catch (Exception ex)
                {
                    _logger.Error($"設定檔保存失敗: {ex}");
                    _logger.Error($"請手動將此字串填入設定檔中的 \"{nameof(TokenKey)}\" 欄位: {TokenKey}");
                    Environment.Exit(3);
                }
            }

            if (string.IsNullOrWhiteSpace(config.RedisTokenKey) || string.IsNullOrWhiteSpace(RedisTokenKey))
            {
                _logger.Error($"{nameof(RedisTokenKey)} 遺失，將重新建立隨機亂數");

                RedisTokenKey = GenRandomKey();

                try { File.WriteAllText("server_config.json", JsonConvert.SerializeObject(this, Formatting.Indented)); }
                catch (Exception ex)
                {
                    _logger.Error($"設定檔保存失敗: {ex}");
                    _logger.Error($"請手動將此字串填入設定檔中的 \"{nameof(RedisTokenKey)}\" 欄位: {RedisTokenKey}");
                    Environment.Exit(3);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message);
            throw;
        }
    }

    public static string GenRandomKey(int length = 128)
    {
        var characters = "ABCDEF_GHIJKLMNOPQRSTUVWXYZ@abcdefghijklmnopqrstuvwx-yz0123456789";
        var Charsarr = new char[128];
        var random = new Random();

        for (int i = 0; i < Charsarr.Length; i++)
        {
            Charsarr[i] = characters[random.Next(characters.Length)];
        }

        var resultString = new string(Charsarr);
        resultString = resultString[Math.Min(length, resultString.Length)..];
        return resultString;
    }
}