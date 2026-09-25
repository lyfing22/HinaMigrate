using System.Data;
using Dapper;
using Kdbndp;

namespace DataMigrate.DataAccess;

/// <summary>
/// KingbaseES 数据访问实现
/// </summary>
public class KingbaseAccessor : IDataAccessor
{
    private readonly string _connectionString;
    private KdbndpConnection? _connection;

    public static string DatabaseType => DatabaseTypes.KingBase;

    public KingbaseAccessor(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> PingAsync()
    {
        await using var conn = new KdbndpConnection(_connectionString);
        await conn.OpenAsync();
        return true;
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null, CommandType? commandType = null, IDbTransaction? transaction = null)
    {
        await using var conn = new KdbndpConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.ExecuteAsync(sql, param, transaction, commandType: commandType ?? CommandType.Text);
    }

    public async Task<T?> QueryScalarAsync<T>(string sql, object? param = null, CommandType? commandType = null, IDbTransaction? transaction = null)
    {
        await using var conn = new KdbndpConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.QueryFirstAsync<T>(sql, param, transaction, commandType: commandType ?? CommandType.Text);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, CommandType? commandType = null, IDbTransaction? transaction = null)
    {
        await using var conn = new KdbndpConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.QueryAsync<T>(sql, param, transaction, commandType: commandType ?? CommandType.Text);
    }

    public async Task<IDbTransaction> BeginTransactionAsync()
    {
        _connection = new KdbndpConnection(_connectionString);
        await _connection.OpenAsync();
        return _connection.BeginTransaction();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
