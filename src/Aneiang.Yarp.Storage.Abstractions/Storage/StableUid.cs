using System.Security.Cryptography;
using System.Text;

namespace Aneiang.Yarp.Storage;

public static class StableUid
{
    public static string FromKey(string prefix, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + ":" + key));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }
}
