using System.Security.Cryptography;
using System.Text;

namespace Divinity.Launcher.Security;

public static class PkceGenerator
{
    public const string ChallengeMethod = "S256";
    private const int VerifierBytes = 64;
    private const int MinimumVerifierLength = 43;
    private const int MaximumVerifierLength = 128;

    public static PkcePair Create()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(VerifierBytes);
        var verifier = Base64Url.Encode(verifierBytes);
        return new PkcePair(verifier, CreateChallenge(verifier), ChallengeMethod);
    }

    public static string CreateChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64Url.Encode(bytes);
    }

    public static bool Validate(PkcePair pair) =>
        pair.Method == ChallengeMethod
        && pair.CodeVerifier.Length is >= MinimumVerifierLength and <= MaximumVerifierLength
        && string.Equals(CreateChallenge(pair.CodeVerifier), pair.CodeChallenge, StringComparison.Ordinal);
}

public sealed record PkcePair(string CodeVerifier, string CodeChallenge, string Method);
