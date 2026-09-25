namespace DataMigrate.DataAccess;

/// <summary>
/// 数据库类型字符串常量，与各工厂注册表 / Parser 拼接器的 key 保持一致。
/// 全小写，与连接串中 <c>DatabaseType=</c> 的规范化值匹配。
/// </summary>
public static class DatabaseTypes
{
    public const string MongoDb   = "mongodb";
    public const string SqlServer = "sqlserver";
    public const string KingBase  = "kingbase";
    public const string Dameng    = "dameng";
}
