using System.Security.Cryptography;

namespace Divinity.ContractsProto.GameTickets;

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static string CreateRandom(int bytes) =>
        Encode(RandomNumberGenerator.GetBytes(bytes));
}
