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

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
                options.ForwardedForHeaderName = Configuration["ForwardedHeaders:ForwardedForHeaderName"] ?? "CF-Connecting-IP";
                options.ForwardLimit = 1;

                foreach (var proxy in Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
                {
                    if (!IPAddress.TryParse(proxy, out var address))
                        throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains invalid IP address '{proxy}'.");

                    options.KnownProxies.Add(address);
                }

                foreach (var network in Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
                {
                    var parts = network.Split('/', 2);
                    if (parts.Length != 2 ||
                        !IPAddress.TryParse(parts[0], out var prefix) ||
                        !int.TryParse(parts[1], out var prefixLength))
                    {
                        throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks contains invalid CIDR network '{network}'.");
                    }

                    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
                }
            });

            services.AddSingleton<RedisService>();
            services.AddSingleton<Services.Auth.TokenService>();
            services.AddSingleton<BearerTokenService>();
            services.AddSingleton<OAuthStateService>();
            services.AddSingleton<GoogleOAuthService>();
            services.AddSingleton<TwitchAuthorizationService>();

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
