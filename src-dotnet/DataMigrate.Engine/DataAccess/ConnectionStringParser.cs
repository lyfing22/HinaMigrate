namespace DataMigrate.DataAccess;

/// <summary>
/// 通用连接串解析器：统一格式 key=value;key=value;... 中读取 DatabaseType，
/// 并按目标数据库类型拼接对应驱动需要的原生连接串。
///
/// 统一格式样例：
///   MongoDB   : DatabaseType=mongodb;Host=...;Port=27017;Database=...;User=...;Password=...;serverSelectionTimeoutMS=5000
///   SqlServer : DatabaseType=sqlserver;Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=true
///   KingBase  : DatabaseType=kingbase;Host=...;Port=5432;Database=...;Username=...;Password=...
///
/// 约定：
///   - Password 值不得包含分号（用户自行避免）
///   - 大小写不敏感
/// </summary>
public static class ConnectionStringParser
{
    private static readonly Dictionary<string, Func<Dictionary<string, string>, string>> _builders
        = new(StringComparer.OrdinalIgnoreCase)
        {
            [DatabaseTypes.MongoDb]   = BuildMongoUri,
            [DatabaseTypes.SqlServer] = BuildSqlServer,
            [DatabaseTypes.KingBase]  = BuildKingBase,
        };

    public static (string cleanConnectionString, string databaseType) Parse(string unifiedConfig)
    {
        if (string.IsNullOrWhiteSpace(unifiedConfig))
            throw new ArgumentException("连接字符串不能为空", nameof(unifiedConfig));

        var dict = ParseKeyValue(unifiedConfig);

        if (!dict.TryGetValue("DatabaseType", out var dbTypeRaw) ||
            string.IsNullOrWhiteSpace(dbTypeRaw))
            throw new InvalidOperationException("缺少 DatabaseType 参数");

        dict.Remove("DatabaseType");
        var dbType = dbTypeRaw.ToLowerInvariant();

        if (!_builders.TryGetValue(dbType, out var build))
            throw new InvalidOperationException(
                $"未知 DatabaseType: {dbType}（支持: {string.Join(", ", _builders.Keys)}）");

        return (build(dict), dbType);
    }

    /// <summary>将 key=value;key=value;... 切分为字典（大小写不敏感）</summary>
    private static Dictionary<string, string> ParseKeyValue(string input)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in input.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = segment.IndexOf('=');
            if (idx < 0) continue;

            var key = segment[..idx].Trim();
            var val = segment[(idx + 1)..].Trim();
            if (key.Length == 0) continue;

            dict[key] = val;
        }

        return dict;
    }

    /// <summary>拼 MongoDB URI：<c>mongodb://[user:pass@]host[:port][/db][?options]</c></summary>
    private static string BuildMongoUri(Dictionary<string, string> p)
    {
        if (!TryGetHost(p, out var host))
            throw new InvalidOperationException("MongoDB 缺少 Host/Server 参数");

        var sb = new System.Text.StringBuilder("mongodb://");

        if (p.TryGetValue("User", out var user) && !string.IsNullOrWhiteSpace(user))
        {
            sb.Append(user);
            if (p.TryGetValue("Password", out var pw) && !string.IsNullOrEmpty(pw))
                sb.Append(':').Append(pw);
            sb.Append('@');
        }

        sb.Append(host);

        if (p.TryGetValue("Port", out var port) && !string.IsNullOrWhiteSpace(port))
            sb.Append(':').Append(port.Trim());

        //if (p.TryGetValue("Database", out var db) && !string.IsNullOrWhiteSpace(db))
        //    sb.Append('/').Append(db.Trim());

        // 剩余参数（非识别键）拼到 query
        bool hasQuery = false;
        foreach (var kv in p)
        {
            if (IsMongoBuildKey(kv.Key)) continue;
            if (string.IsNullOrEmpty(kv.Value)) continue;
            sb.Append(hasQuery ? '&' : '?').Append(kv.Key).Append('=').Append(kv.Value);
            hasQuery = true;
        }

        return sb.ToString();
    }

    private static bool IsMongoBuildKey(string key) => key.ToLowerInvariant() switch
    {
        "host" or "server" or "port" or "database" or "user" or "password"=> true,
        _ => false
    };

    /// <summary>拼 SQL Server 原生连接串（末尾带分号）</summary>
    private static string BuildSqlServer(Dictionary<string, string> p)
    {
        var sb = new System.Text.StringBuilder();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Server 优先用 Server，兼容 Host
        if (p.TryGetValue("Server", out var server) && !string.IsNullOrWhiteSpace(server))
        {
            sb.Append("Server=").Append(server).Append(';');
            emitted.Add("server");
        }
        else if (TryGetHost(p, out var host))
        {
            sb.Append("Server=").Append(host).Append(';');
            emitted.Add("host");
        }
        else
        {
            throw new InvalidOperationException("SQL Server 缺少 Server/Host 参数");
        }

        if (p.TryGetValue("Port", out var port) && !string.IsNullOrWhiteSpace(port))
        {
            sb.Append("Port=").Append(port).Append(';');
            emitted.Add("port");
        }

        if (p.TryGetValue("Database", out var db) && !string.IsNullOrWhiteSpace(db))
        {
            sb.Append("Database=").Append(db).Append(';');
            emitted.Add("database");
        }

        // User / Password 保留原写法（User= 或 User Id= 均可）
        if (p.TryGetValue("User", out var user) && !string.IsNullOrWhiteSpace(user))
        {
            sb.Append("User=").Append(user).Append(';');
            emitted.Add("user");
        }
        else if (p.TryGetValue("User Id", out var userId) && !string.IsNullOrWhiteSpace(userId))
        {
            sb.Append("User Id=").Append(userId).Append(';');
            emitted.Add("user id");
        }

        if (p.TryGetValue("Password", out var pw) && !string.IsNullOrEmpty(pw))
        {
            sb.Append("Password=").Append(pw).Append(';');
            emitted.Add("password");
        }

        // 剩余参数（TrustServerCertificate / Connect Timeout 等）原样透传
        foreach (var kv in p)
        {
            if (emitted.Contains(kv.Key)) continue;
            if (string.IsNullOrEmpty(kv.Value)) continue;
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        }

        return sb.ToString();
    }

    /// <summary>拼 KingBase 原生连接串</summary>
    private static string BuildKingBase(Dictionary<string, string> p)
    {
        if (!TryGetHost(p, out var host))
            throw new InvalidOperationException("KingBase 缺少 Host/Server 参数");

        var sb = new System.Text.StringBuilder();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        sb.Append("Host=").Append(host).Append(';');
        emitted.Add("host");
        emitted.Add("server");

        sb.Append("Port=").Append(p.GetValueOrDefault("Port", "") is { Length: > 0 } pt ? pt : "54321").Append(';');
        emitted.Add("port");

        if (p.TryGetValue("Database", out var db) && !string.IsNullOrWhiteSpace(db))
        {
            sb.Append("Database=").Append(db).Append(';');
            emitted.Add("database");
        }

        if (p.TryGetValue("Username", out var uname) && !string.IsNullOrWhiteSpace(uname))
        {
            sb.Append("Username=").Append(uname).Append(';');
            emitted.Add("username");
        }
        else if (p.TryGetValue("User", out var user) && !string.IsNullOrWhiteSpace(user))
        {
            sb.Append("Username=").Append(user).Append(';');
            emitted.Add("user");
        }

        if (p.TryGetValue("Password", out var pw) && !string.IsNullOrEmpty(pw))
        {
            sb.Append("Password=").Append(pw).Append(';');
            emitted.Add("password");
        }

        foreach (var kv in p)
        {
            if (emitted.Contains(kv.Key)) continue;
            if (string.IsNullOrEmpty(kv.Value)) continue;
            var keyLower = kv.Key.ToLowerInvariant();
            if (keyLower == "serverselectiontimeoutms")
            {
                // MongoDB 参数：serverSelectionTimeoutMS → Kingbase Timeout（秒）
                if (int.TryParse(kv.Value, out var ms) && ms > 0)
                    sb.Append("Timeout=").Append(ms / 1000).Append(';');
            }
            else if (IsKingBaseKnownKey(keyLower))
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            }
        }

        return sb.ToString();
    }

    private static bool IsKingBaseKnownKey(string key) => key switch
    {
        "timeout" or "commandtimeout" or "internalcommandtimeout"
        or "keepalive" or "sslmode" or "sslcert" or "sslkey" or "sslrootcert"
        or "applicationname" or "encoding" or "connectttl" or "pintosite"
        or "pooling" or "minpoolsize" or "maxpoolsize"
        => true,
        _ => false
    };

    private static bool TryGetHost(Dictionary<string, string> p, out string host)
    {
        if (p.TryGetValue("Host", out var h) && !string.IsNullOrWhiteSpace(h)) { host = h.Trim(); return true; }
        if (p.TryGetValue("Server", out var s) && !string.IsNullOrWhiteSpace(s)) { host = s.Trim(); return true; }
        host = "";
        return false;
    }
}
