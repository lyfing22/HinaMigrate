using DataMigrate.DataAccess;
using Dapper;
using Microsoft.Extensions.Logging;

namespace DataMigrate.Infrastructure;

/// <summary>
/// 目标数据库自动初始化：建库（如不存在）+ 建表。
/// 仅在首次启动时执行，后续幂等跳过。
/// </summary>
public class DatabaseInitializer
{
    private readonly IDataAccessor _accessor;
    private readonly string? _initSql;
    private readonly ILogger _logger;

    public DatabaseInitializer(IDataAccessor accessor, ILogger logger)
    {
        _accessor = accessor;
        _logger = logger;

        // 读取 SQL 文件
        _initSql = ReadInitSql();
    }

    /// <summary>
    /// 确保数据库存在且表已创建。幂等操作，重复调用无副作用。
    /// </summary>
    public async Task EnsureAsync()
    {
        // 1. 确保数据库存在
        await EnsureDatabaseExistsAsync();

        // 2. 建表（幂等）
        await EnsureTablesAsync();
    }

    private async Task EnsureDatabaseExistsAsync()
    {
        // 暂不支持自动建库（不同数据库建库逻辑差异较大），仅记录类型信息
        // 通过反射读取实现类的静态 DatabaseType 属性
        var dbTypeProp = _accessor.GetType().GetProperty("DatabaseType",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
        var dbType = dbTypeProp?.GetValue(null) as string ?? "unknown";
        _logger.LogInformation("目标数据库类型: {DbType}", dbType);
    }

    private async Task EnsureTablesAsync()
    {
        if (string.IsNullOrWhiteSpace(_initSql))
        {
            // 缺失建表脚本视为配置级错误：终止迁移，让用户补齐后重试
            throw new InvalidOperationException(
                "未找到建表初始化 SQL 文件（期望路径：Install/DBInit/<dbtype>/CreateTables.sql），"
                + "请补齐脚本后重新运行迁移。");
        }

        // 不允许吞异常：任何失败都向上传播，由 ConfigValidator 汇总后终止迁移
        await _accessor.ExecuteAsync(_initSql);
        _logger.LogInformation("建表检查完成（幂等，不重复创建）");
    }

    private string? ReadInitSql()
    {
        // 根据目标数据库类型动态选择对应的初始化脚本
        var dbTypeProp = _accessor.GetType().GetProperty("DatabaseType",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
        var dbType = (dbTypeProp?.GetValue(null)?.ToString() ?? "sqlserver").ToLowerInvariant();

        // 1. 优先读取嵌入资源（单文件发布 / Tauri 打包后，磁盘上不存在 .sql 文件）
        var asm = typeof(DatabaseInitializer).Assembly;
        var suffix = $".Install.DBInit.{dbType}.CreateTables.sql";
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream != null)
                    return new StreamReader(stream).ReadToEnd();
            }
        }

        // 2. 兑底磁盘文件（开发调试场景）
        var basePath = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(basePath, "Install", "DBInit", dbType, "CreateTables.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "Install", "DBInit", dbType, "CreateTables.sql"),
            Path.Combine(Path.GetFullPath(Path.Combine(basePath, "..", "..", "..")),
                         "Install", "DBInit", dbType, "CreateTables.sql")
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        // 兜底：兼容旧结构（无子目录的单文件）
        var fallback = Path.Combine(basePath, "Install", "DBInit", "CreateTables.sql");
        return File.Exists(fallback) ? File.ReadAllText(fallback) : null;
    }
}
