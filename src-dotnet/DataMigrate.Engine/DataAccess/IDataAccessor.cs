using System.Data;

namespace DataMigrate.DataAccess;

/// <summary>
/// 数据访问抽象接口，支持多种数据库后端（SQL Server、KingbaseES 等）
/// </summary>
public interface IDataAccessor : IDisposable
{
    // 注意：DatabaseType 改为各实现类上的 static 只读属性，不通过接口声明
    // 工厂通过反射读取每个实现类的 public static string DatabaseType

    /// <summary>
    /// 测试数据库连接是否可用
    /// </summary>
    Task<bool> PingAsync();

    /// <summary>
    /// 执行 SQL 语句（INSERT/UPDATE/DELETE）
    /// </summary>
    Task<int> ExecuteAsync(string sql, object? param = null, CommandType? commandType = null, IDbTransaction? transaction = null);
    
    /// <summary>
    /// 查询单个标量值
    /// </summary>
    Task<T?> QueryScalarAsync<T>(string sql, object? param = null, CommandType? commandType = null, IDbTransaction? transaction = null);
    
    /// <summary>
    /// 查询结果集
    /// </summary>
    Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, CommandType? commandType = null, IDbTransaction? transaction = null);

    /// <summary>
    /// 开始事务
    /// </summary>
    Task<IDbTransaction> BeginTransactionAsync();
}
