using System.Reflection;

namespace DataMigrate.DataAccess;

/// <summary>
/// 数据访问器工厂：通过反射扫描所有实现 <see cref="IDataAccessor"/> 的类，
/// 自动读取其 <see cref="IDataAccessor.DatabaseType"/> 静态属性完成注册。
/// 新增目标数据库只需实现 <see cref="IDataAccessor"/> 并在程序集中定义即可。
/// </summary>
public static class DataAccessorFactory
{
    private static readonly Dictionary<string, Func<string, IDataAccessor>> _adapters
        = new(StringComparer.OrdinalIgnoreCase);

    static DataAccessorFactory()
    {
        var assembly = typeof(IDataAccessor).Assembly;
        foreach (var type in assembly.GetTypes()
            .Where(t => typeof(IDataAccessor).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
        {
            // 静态抽象属性通过类型本身读取，不依赖实例
            var dbTypeProp = type.GetProperty("DatabaseType",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (dbTypeProp == null)
                throw new InvalidOperationException(
                    $"{type.FullName} 实现了 IDataAccessor 但未定义静态 DatabaseType 属性");

            var dbType = dbTypeProp.GetValue(null)?.ToString();
            if (string.IsNullOrWhiteSpace(dbType))
                throw new InvalidOperationException(
                    $"{type.FullName}.DatabaseType 返回值为空");

            if (_adapters.TryAdd(dbType.ToLowerInvariant(), conn => (IDataAccessor)Activator.CreateInstance(type, conn)!))
                continue;

            throw new InvalidOperationException(
                $"数据库类型 \"{dbType}\" 已被 {type.FullName} 注册，无法重复注册");
        }
    }

    /// <summary>当前已注册的目标数据库类型集合（对外只读）</summary>
    public static IReadOnlyCollection<string> SupportedTypes => _adapters.Keys;

    public static IDataAccessor Create(string destinationConnectionString)
    {
        var (cleanConnStr, dbType) = ConnectionStringParser.Parse(destinationConnectionString);

        if (!_adapters.TryGetValue(dbType, out var factory))
            throw new InvalidOperationException(
                $"不支持的数据库类型: {dbType}（支持: {string.Join(", ", _adapters.Keys)}）");

        return factory(cleanConnStr);
    }
}
