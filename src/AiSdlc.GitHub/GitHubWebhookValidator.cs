using System.Security.Cryptography;
using System.Text;

namespace AiSdlc.GitHub;

public static class GitHubWebhookValidator
{
    private const string Prefix = "sha256=";

    /// <summary>
    /// Verifies the X-Hub-Signature-256 header against the raw request body.
    /// Uses a constant-time comparison to prevent timing attacks.
    /// </summary>
    public static bool IsValid(ReadOnlySpan<byte> payload, string signatureHeader, string secret)
    {
        if (!signatureHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        ReadOnlySpan<char> hexPart = signatureHeader.AsSpan(Prefix.Length);
        if (hexPart.Length != 64) // SHA-256 is 32 bytes = 64 hex chars
            return false;

        byte[] receivedHash;
        try
        {
            receivedHash = Convert.FromHexString(hexPart);
        }
        catch (FormatException)
        {
            return false;
        }

        if (receivedHash.Length != 32)
            return false;

        var key = Encoding.UTF8.GetBytes(secret);
        var computedHash = HMACSHA256.HashData(key, payload);

        return CryptographicOperations.FixedTimeEquals(computedHash, receivedHash);
    }
}
