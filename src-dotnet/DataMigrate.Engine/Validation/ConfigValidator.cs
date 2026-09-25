using DataMigrate.Core;
using DataMigrate.DataAccess;
using DataMigrate.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DataMigrate.Validation;

/// <summary>启动前配置校验</summary>
public class ConfigValidator
{
    private readonly MigrationOptions _options;
    private readonly ILogger<ConfigValidator> _logger;
    private readonly IDataAccessor _accessor;
    private readonly IMigrationSource? _source;

    public ConfigValidator(
        MigrationOptions options,
        ILogger<ConfigValidator> logger,
        IDataAccessor accessor,
        IMigrationSource? source = null)
    {
        _options = options;
        _logger = logger;
        _accessor = accessor;
        _source = source;
    }

    public async Task ValidateAllAsync()
    {
        var errors = new List<string>();

        // 校验必填字段
        if (string.IsNullOrWhiteSpace(_options.ConnectionStrings.SourceConn))
            errors.Add("ConnectionStrings.SourceConn 未配置");
        if (string.IsNullOrWhiteSpace(_options.ConnectionStrings.DestinationConn))
            errors.Add("ConnectionStrings.DestinationConn 未配置");
        if (string.IsNullOrWhiteSpace(_options.Upload.Url))
            errors.Add("Upload.Url 未配置");
        if (string.IsNullOrWhiteSpace(_options.Upload.JwtAppId))
            errors.Add("Upload.JwtAppId 未配置");
        if (string.IsNullOrWhiteSpace(_options.Upload.JwtAppSecret))
            errors.Add("Upload.JwtAppSecret 未配置");
        if (string.IsNullOrWhiteSpace(_options.Migration.DbFlag))
            errors.Add("Migration.DbFlag 未配置");

        // 校验模式相关参数
        var mode = _options.Migration.Mode.ToLowerInvariant();
        if (mode == "debug" && string.IsNullOrWhiteSpace(_options.Migration.ExamId))
            errors.Add("debug 模式需配置 Migration.ExamId");

        if (mode == "batch" || mode == "retry")
        {
            if (_options.Migration.TimeRange.Start == default || _options.Migration.TimeRange.End == default)
                errors.Add("batch/retry 模式需配置 Migration.TimeRange.Start 和 End");
        }

        // 校验源连接串（Parser 解析 + 工厂支持类型校验）
        if (!string.IsNullOrWhiteSpace(_options.ConnectionStrings.SourceConn))
        {
            try
            {
                var (_, type) = ConnectionStringParser.Parse(_options.ConnectionStrings.SourceConn);
                var supported = MigrationSourceFactory.SupportedTypes;
                if (!supported.Contains(type))
                    errors.Add($"SourceConn 的 DatabaseType={type} 不支持，支持: {string.Join(", ", supported)}");
            }
            catch (Exception ex)
            {
                errors.Add($"SourceConn 解析失败: {ex.Message}");
            }
        }

        // 校验目标连接串（同上）
        if (!string.IsNullOrWhiteSpace(_options.ConnectionStrings.DestinationConn))
        {
            try
            {
                var (_, type) = ConnectionStringParser.Parse(_options.ConnectionStrings.DestinationConn);
                var supported = DataAccessorFactory.SupportedTypes;
                if (!supported.Contains(type))
                    errors.Add($"DestinationConn 的 DatabaseType={type} 不支持，支持: {string.Join(", ", supported)}");
            }
            catch (Exception ex)
            {
                errors.Add($"DestinationConn 解析失败: {ex.Message}");
            }
        }

        // 测试源数据库连通性
        if (_source != null)
        {
            try
            {
                await _source.ValidateAsync();
                _logger.LogInformation("源数据库 {Source} 连接正常", _source.SourceName);
            }
            catch (Exception ex)
            {
                errors.Add($"源数据库 {_source.SourceName} 连接失败: {ex.Message}");
            }
        }

        // 测试目标数据库连通性
        try
        {
            var isReachable = await _accessor.PingAsync();
            if (isReachable)
                _logger.LogInformation("目标数据库连接正常");
            else
                errors.Add("目标数据库连接失败");
        }
        catch (Exception ex)
        {
            errors.Add($"目标数据库连接失败: {ex.Message}");
        }

        // 自动建库建表（幂等；失败则终止迁移，让用户检查配置后重试）
        try
        {
            var initializer = new DatabaseInitializer(_accessor, _logger);
            await initializer.EnsureAsync();
            _logger.LogInformation("数据库及表结构初始化完成");
        }
        catch (Exception ex)
        {
            errors.Add(
                $"目标库初始化失败（EnsureTablesAsync）: {ex.Message}。"
                + "请检查 DestinationConn 配置、账号权限，以及 Install/DBInit/<dbtype>/CreateTables.sql 脚本，"
                + "修正后重新运行迁移。");
            _logger.LogError(ex, "目标库初始化失败，终止迁移");
        }

        if (errors.Count > 0)
        {
            _logger.LogError("配置校验失败:");
            foreach (var err in errors)
                _logger.LogError("  - {Error}", err);
            throw new InvalidOperationException($"配置校验失败: {string.Join("; ", errors)}");
        }

        _logger.LogInformation("配置校验通过");
    }
}
