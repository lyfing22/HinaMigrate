using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DataMigrate.DataAccess;

/// <summary>
/// SQL Server 数据访问实现
/// </summary>
public class SqlServerAccessor : IDataAccessor
{
    private readonly string _connectionString;
    private SqlConnection? _connection;

    public static string DatabaseType => DatabaseTypes.SqlServer;

    public SqlServerAccessor(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> PingAsync()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        return true;
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null, CommandType? commandType = null, IDbTransaction? transaction = null)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.ExecuteAsync(sql, param, transaction, commandType: commandType ?? CommandType.Text);
    }

    public async Task<T?> QueryScalarAsync<T>(string sql, object? param = null, CommandType? commandType = null, IDbTransaction? transaction = null)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.QueryFirstAsync<T>(sql, param, transaction, commandType: commandType ?? CommandType.Text);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, CommandType? commandType = null, IDbTransaction? transaction = null)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.QueryAsync<T>(sql, param, transaction, commandType: commandType ?? CommandType.Text);
    }

    public async Task<IDbTransaction> BeginTransactionAsync()
    {
        _connection = new SqlConnection(_connectionString);
        await _connection.OpenAsync();
        return _connection.BeginTransaction();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
