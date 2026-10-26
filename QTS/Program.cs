using System.Text.Json;
using System.Text.Json.Serialization;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QTS.Contracts;
using QTS.Data;
using QTS.Infrastructure;
using QTS.Infrastructure.Auth;
using QTS.Infrastructure.Middleware;
using QTS.Infrastructure.Okx;
using QTS.Services;
using QTS.Workers;
using SqlSugar;

namespace QTS;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
        builder.Host.ConfigureContainer<ContainerBuilder>(container => container.RegisterModule<QtsModule>());

        // 数据库：SqlSugarScope 线程安全，单例注入。
        var connectionString = builder.Configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException("缺少数据库连接串 ConnectionStrings:MySql");
        builder.Services.AddSingleton<ISqlSugarClient>(_ => SqlSugarSetup.Create(connectionString));

        // DataProtection：加密保存欧易 API 凭据，密钥持久化到 App_Data/keys。
        var keyPath = builder.Configuration["DataProtection:KeyPath"];
        if (string.IsNullOrWhiteSpace(keyPath))
            keyPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "App_Data", "keys"));
        Directory.CreateDirectory(keyPath);
        builder.Services.AddDataProtection()
            .SetApplicationName("QTS")
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath));

        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
        builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));

        // 欧易 HTTP 客户端：12 秒超时，客户端内部还有兜底取消。
        builder.Services.AddHttpClient(OkxClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("QTS/1.0");
        });

        // AI 大模型客户端（OpenAI 兼容）；未配置 Key 时不会发起请求，全部走本地量化引擎。
        builder.Services.AddHttpClient("ai", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(35);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("QTS-AI/1.0");
        });

        // 认证：自定义 Bearer Token；全局 fallback policy 要求登录，匿名端点使用 [AllowAnonymous]。
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = BearerAuthenticationHandler.SchemeName;
                options.DefaultAuthenticateScheme = BearerAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = BearerAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, BearerAuthenticationHandler>(
                BearerAuthenticationHandler.SchemeName, _ => { });

        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("QtsDev", policy => policy
                .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                .AllowAnyHeader()
                .AllowAnyMethod());
        });

        builder.Services.AddOpenApi();
        builder.Services.AddHostedService<TradingWorker>();
        builder.Services.AddHostedService<SampleDataWorker>();

        var app = builder.Build();

        // 初始化数据库结构与种子数据。数据库不可用时快速失败并给出清晰日志。
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            SqlSugarSetup.InitSchema(db);
            DbInitializer.MigrateDefaults(db, app.Logger);
            DbInitializer.Seed(
                db,
                scope.ServiceProvider.GetRequiredService<IStrategyRegistry>(),
                scope.ServiceProvider.GetRequiredService<PasswordHasher>(),
                app.Logger);
            app.Logger.LogInformation("数据库初始化完成");
        }
        catch (Exception ex)
        {
            app.Logger.LogCritical(ex, "数据库初始化失败，请检查 MySQL 是否启动以及连接串是否正确");
            throw;
        }

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseCors("QtsDev");
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapOpenApi().AllowAnonymous();

        // SPA fallback：非 /api 路径返回 Vue 的 index.html。
        var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        var indexPath = Path.Combine(webRoot, "index.html");
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(ApiResponse<object>.Fail("接口不存在", "NOT_FOUND"), QtsJson.Options));
                return;
            }

            if (!File.Exists(indexPath))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(
                    "前端尚未构建。请进入 web 目录执行 npm install && npm run build，然后重启后端；开发模式可访问 http://localhost:5173。");
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(indexPath);
        }).AllowAnonymous();

        app.Run();
    }
}
