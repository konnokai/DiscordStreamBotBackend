using DiscordStreamBotBackend.DataBase;
using DiscordStreamBotBackend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prometheus;
using System;
using System.Net;
using TwitchLib.EventSub.Webhooks.Extensions;

namespace DiscordStreamBotBackend
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            StartupValidationHostedService.ValidateConfiguration(Configuration);

            // token 儲存改走 MySQL：MySqlDataStore 每次操作用 factory 建短生命週期 context（DataStore 存活期可能跨越/併發於請求 scope，EF context 非執行緒安全）。
            // 仍保留 scoped MainDbContext（委派 factory）供 YouTubeNotificationsController 等既有注入使用。
            services.AddDbContextFactory<MainDbContext>(options =>
                options
                    .UseMySql(Configuration.GetConnectionString("MySql"), ServerVersion.AutoDetect(Configuration.GetConnectionString("MySql")))
                    .UseSnakeCaseNamingConvention());
            services.AddScoped(p => p.GetRequiredService<IDbContextFactory<MainDbContext>>().CreateDbContext());

            services.AddControllers().AddNewtonsoftJson(options =>
            {
                options.UseMemberCasing();
            });
            services.AddMemoryCache();

            services.AddSingleton<RedisService>();
            services.AddSingleton<Services.Auth.TokenService>();
            services.AddSingleton<BearerTokenService>();
            services.AddSingleton<OAuthStateService>();
            services.AddSingleton<GoogleAccountOperationCoordinator>();
            services.AddSingleton<IGoogleOAuthOperationLock>(p =>
                new GoogleOAuthOperationLock(p.GetRequiredService<RedisService>().RedisDb));
            services.AddSingleton<IGoogleUnlinkOperationCancellationFactory, GoogleUnlinkOperationCancellationFactory>();
            services.AddSingleton<GoogleOAuthService>();
            services.AddSingleton<IGoogleAccountProvider>(p => p.GetRequiredService<GoogleOAuthService>());
            services.AddSingleton<IGoogleProviderRevoker>(p => p.GetRequiredService<GoogleOAuthService>());
            services.AddSingleton<IGoogleAccountLinkMetricsRefresher>(p => p.GetRequiredService<GoogleOAuthService>());
            services.AddSingleton<IGoogleAccountLinkStore, GoogleAccountLinkStore>();
            services.AddSingleton<IGoogleMemberCleanupWakeupPublisher, GoogleMemberCleanupWakeupPublisher>();
            services.AddSingleton<GoogleAccountLinkService>(p => new GoogleAccountLinkService(
                p.GetRequiredService<IGoogleAccountProvider>(),
                p.GetRequiredService<IGoogleProviderRevoker>(),
                p.GetRequiredService<IGoogleAccountLinkStore>(),
                p.GetRequiredService<IGoogleMemberCleanupWakeupPublisher>(),
                p.GetRequiredService<IGoogleAccountLinkMetricsRefresher>(),
                p.GetRequiredService<GoogleAccountOperationCoordinator>(),
                p.GetRequiredService<IGoogleOAuthOperationLock>(),
                p.GetRequiredService<IGoogleUnlinkOperationCancellationFactory>(),
                p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoogleAccountLinkService>>()));
            services.AddSingleton<TwitchAuthorizationService>();
            services.AddSingleton<AdminSettingsRedisService>();
            services.AddTransient<DiscordGuildAuthorizationService>();

            var publicUrls = new PublicUrlService(Configuration);
            services.AddSingleton(publicUrls);
            services.AddCors(options =>
            {
                options.AddPolicy(name: "allowGET", builder =>
                {
                    builder.WithOrigins(publicUrls.FrontendDomain)
                           .WithMethods("GET")
                           .WithHeaders("Content-Type", "Authorization");
                });
                options.AddPolicy(name: "allowPOST", builder =>
                {
                    builder.WithOrigins(publicUrls.FrontendDomain)
                           .WithMethods("POST")
                           .WithHeaders("Content-Type", "Authorization");
                });
                options.AddPolicy(name: "frontend", builder =>
                {
                    builder.WithOrigins(publicUrls.FrontendDomain)
                           .WithMethods("GET", "POST", "DELETE")
                           .WithHeaders("Content-Type", "Authorization");
                });
            });

            services.AddTwitchLibEventSubWebhooks(config =>
            {
                config.CallbackPath = "/TwitchWebHooks";
                config.Secret = Configuration["Twitch:WebHookSecret"];
                config.EnableLogging = false;
            });

            services.AddHostedService<StartupValidationHostedService>();
            services.AddHostedService<EventSubHostedService>();
            services.AddHostedService<TwitchTokenValidationHostedService>();

            services.Configure<KestrelServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });

            services.AddHttpClient();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseForwardedHeaders();
            app.UseMiddleware<Middleware.LogMiddleware>();

            app.UseRouting();
            app.UseCors();
            app.UseAuthorization();

            app.UseTwitchLibEventSubWebhooks();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapMetrics();
            });
        }
    }
}
