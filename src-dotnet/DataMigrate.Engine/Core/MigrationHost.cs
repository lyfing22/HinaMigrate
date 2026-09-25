using DataMigrate.DataAccess;
using DataMigrate.Infrastructure;
using DataMigrate.Upload;
using DataMigrate.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DataMigrate.Core;

/// <summary>
/// 桌面端 DI 构建器：基于一份 MigrationOptions（来自前端 profile）构建
/// 独立的 ServiceProvider，供 Sidecar 每条运行命令按需重建。
/// 与原控制台的 BuildInfrastructure 区别：不依赖 Host/配置文件，日志由调用方注入。
/// </summary>
public static class MigrationHost
{
    /// <summary>
    /// 构建一个面向单次运行的 ServiceProvider。
    /// </summary>
    /// <param name="options">本次运行配置（由前端 profile 反序列化得到）</param>
    /// <param name="serilogLogger">已含 IPC/文件 sink 的 Serilog logger</param>
    /// <param name="configure">可选：在默认注册前注册自定义服务（如 IProgressReporter、IpcWriter）</param>
    public static IServiceProvider BuildServiceProvider(
        MigrationOptions options,
        Serilog.ILogger serilogLogger,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        // 1. 调用方自定义注册（先于默认，以便 TryAdd 默认项不覆盖）
        configure?.Invoke(services);

        // 2. 日志：桥接 Microsoft.Extensions.Logging -> Serilog
        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddSerilog(serilogLogger, dispose: false);
        });

        // 3. 目标库数据访问器
        var accessor = DataAccessorFactory.Create(options.ConnectionStrings.DestinationConn);
        services.AddSingleton(accessor);

        // 4. 源库数据源
        services.AddSingleton<IMigrationSource>(sp =>
            MigrationSourceFactory.Create(options.ConnectionStrings.SourceConn, sp));

        services.AddSingleton(options);
        services.AddSingleton<MigrationStatistics>();

        // 默认进度上报：若调用方未注册则用控制台进度条（桌面端会覆盖为 IpcProgressReporter）
        services.TryAddSingleton<IProgressReporter, ConsoleProgressBar>();

        services.AddSingleton<ConfigValidator>(sp => new ConfigValidator(
            options,
            sp.GetRequiredService<ILogger<ConfigValidator>>(),
            sp.GetRequiredService<IDataAccessor>(),
            sp.GetRequiredService<IMigrationSource>()));

        services.AddSingleton(new JwtTokenManager(
            options.Upload.JwtAppId,
            string.IsNullOrWhiteSpace(options.Upload.JwtServerNode)
                ? options.Upload.JwtAppId
                : options.Upload.JwtServerNode,
            options.Upload.JwtExpiryMinutes,
            options.Upload.JwtAppSecret));

        // .NET 8 Keyed Services 注册命名 HttpClient
        services.AddKeyedSingleton<HttpClient>("examUploader", (sp, _) =>
        {
            var handler = new HttpClientHandler
            {
                MaxConnectionsPerServer = options.Migration.Parallelism * 4
            };
            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(options.Upload.Url),
                Timeout = TimeSpan.FromSeconds(options.Upload.TimeoutSeconds)
            };
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            return client;
        });

        // 按运行模式选择缓冲策略
        var trackMode = options.Migration.Mode.ToLowerInvariant() switch
        {
            "retry" => ExamTrackMode.BufferAll,
            "debug" => ExamTrackMode.Direct,
            _       => ExamTrackMode.BufferFailures,
        };

        services.AddSingleton(sp => new ExamUploader(
            sp.GetKeyedService<HttpClient>("examUploader")!,
            sp.GetRequiredService<JwtTokenManager>(),
            sp.GetRequiredService<IDataAccessor>(),
            options.Migration.DbFlag,
            options.Migration.ZTempBatchSize,
            sp.GetRequiredService<ILogger<ExamUploader>>(),
            trackMode: trackMode));

        services.AddSingleton<PlanManager>();
        services.AddSingleton<MigrationRunner>();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
