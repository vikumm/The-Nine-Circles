using System.Security.Cryptography;
using System.Text;

namespace Divinity.GameGateway.Session;

public static class AccountPseudonym
{
    public static string Create(string accountId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(accountId));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }
}
