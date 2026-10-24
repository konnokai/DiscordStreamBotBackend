using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Debugging;
using System;
using System.Reflection;

#if DEBUG
using System.Diagnostics;
using System.Threading;
#endif

namespace DiscordStreamBotBackend
{
    public class Program
    {
        public static string VERSION => GetLinkerTime(Assembly.GetEntryAssembly());
        public static void Main(string[] args)
        {
#if DEBUG
            Console.WriteLine($"Waiting for debugger to attach, PID: {Process.GetCurrentProcess().Id}");
            while (!Debugger.IsAttached)
            {
                Thread.Sleep(100);
            }
            Console.WriteLine("Debugger attached");
#endif

            // 讀到設定檔前先用只有 console 的 bootstrap logger，Host 建好後再換成含 Loki 的完整設定。
            // sink 內部錯誤（例如 Loki DNS 失敗）只會寫到 SelfLog，不開的話 Loki 沒資料時查不到原因。
            SelfLog.Enable(Console.Error);
            Log.Logger = BackendLogging.Configure(new LoggerConfiguration(), lokiUrl: null).CreateBootstrapLogger();
            Log.Information("{Version} 正在初始化", VERSION);

            try
            {
                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception exception)
            {
                Log.Fatal(exception, "程式因例外而停止。");
            }
            finally
            {
                // Loki sink 在背景批次推送，結束前要 flush，否則最後幾筆（通常就是停止原因）會遺失。
                BackendLogging.CloseAndFlush(TimeSpan.FromSeconds(3));
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                .UseSerilog((context, _, configuration) =>
                    BackendLogging.Configure(configuration, context.Configuration["LokiUrl"]));
        }

        public static string GetLinkerTime(Assembly assembly)
        {
            const string BuildVersionMetadataPrefix = "+build";

            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attribute?.InformationalVersion != null)
            {
                var value = attribute.InformationalVersion;
                var index = value.IndexOf(BuildVersionMetadataPrefix);
                if (index > 0)
                {
                    value = value[(index + BuildVersionMetadataPrefix.Length)..];
                    return value;
                }
            }
            return default;
        }
    }
}
