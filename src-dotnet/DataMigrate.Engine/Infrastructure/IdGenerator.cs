using System.Security.Cryptography;
using System.Text;

namespace DataMigrate.Infrastructure;

/// <summary>
/// 确定性 GUID 生成器：对相同输入始终输出相同 GUID。
/// 算法 = MD5(ASCII(input)) → Guid。
/// </summary>
public static class IdGenerator
{
    public static Guid FromString(string input)
    {
        var hash = MD5.HashData(Encoding.ASCII.GetBytes(input));
        return new Guid(hash);
    }
}
