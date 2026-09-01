using System.Security.Cryptography;
using System.Text;

namespace Divinity.ContractsProto.GameTickets;

public static class GameTicketSecret
{
    private const string Prefix = "gt_";
    private const int SecretBytes = 32;
    private const int MinimumLength = 32;

    public static string Create() => Prefix + Base64Url.CreateRandom(SecretBytes);

    public static bool IsWellFormed(string ticket) =>
        ticket.StartsWith(Prefix, StringComparison.Ordinal)
        && ticket.Length >= MinimumLength
        && ticket.Skip(Prefix.Length).All(IsBase64UrlCharacter);

    public static string Hash(string ticket)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ticket));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string HashNonce(string nonce)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(nonce));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    private static bool IsBase64UrlCharacter(char value) =>
        value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '-'
            or '_';
}
