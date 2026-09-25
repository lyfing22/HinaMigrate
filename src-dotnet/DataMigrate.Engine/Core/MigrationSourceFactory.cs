using DataMigrate.DataAccess;
using DataMigrate.Sources.KingBase;
using DataMigrate.Sources.MongoDb;
using DataMigrate.Sources.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataMigrate.Core;

/// <summary>
/// 数据源适配工厂：按连接串中的 DatabaseType 分派对应的 IMigrationSource 实现。
/// 新增源类型只需在 <see cref="_adapters"/> 注册表加一行。
/// </summary>
public static class MigrationSourceFactory
{
    private static readonly Dictionary<string, Func<string, IServiceProvider, IMigrationSource>> _adapters
        = new(StringComparer.OrdinalIgnoreCase)
        {
            [DatabaseTypes.MongoDb]   = (conn, sp) => new MongoDbSource(conn, sp.GetRequiredService<ILogger<MongoDbSource>>()),
            [DatabaseTypes.SqlServer] = (conn, sp) => new SqlServerSource(conn, sp.GetRequiredService<ILogger<SqlServerSource>>()),
            [DatabaseTypes.KingBase]  = (conn, sp) => new KingbaseSource(conn, sp.GetRequiredService<ILogger<KingbaseSource>>()),
        };

    /// <summary>当前已注册的源数据库类型集合（对外只读）</summary>
    public static IReadOnlyCollection<string> SupportedTypes => _adapters.Keys;

    public static IMigrationSource Create(string sourceConn, IServiceProvider sp)
    {
        var (clean, type) = ConnectionStringParser.Parse(sourceConn);

        if (!_adapters.TryGetValue(type, out var factory))
            throw new InvalidOperationException(
                $"不支持的源数据库类型: {type}（支持: {string.Join(", ", _adapters.Keys)}）");

        return factory(clean, sp);
    }
}
