using System.Security.Cryptography;
using System.Text;
using AiSdlc.GitHub;
using Xunit;

namespace AiSdlc.GitHub.Tests;

public sealed class GitHubWebhookValidatorTests
{
    private const string Secret = "my-webhook-secret";

    private static string Sign(string body, string secret)
    {
        var key     = Encoding.UTF8.GetBytes(secret);
        var payload = Encoding.UTF8.GetBytes(body);
        var hash    = HMACSHA256.HashData(key, payload);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void ValidSignature_ReturnsTrue()
    {
        var body      = """{"action":"opened"}""";
        var signature = Sign(body, Secret);
        var payload   = Encoding.UTF8.GetBytes(body);

        Assert.True(GitHubWebhookValidator.IsValid(payload, signature, Secret));
    }

    [Fact]
    public void TamperedBody_ReturnsFalse()
    {
        var original  = """{"action":"opened"}""";
        var tampered  = """{"action":"deleted"}""";
        var signature = Sign(original, Secret);
        var payload   = Encoding.UTF8.GetBytes(tampered);

        Assert.False(GitHubWebhookValidator.IsValid(payload, signature, Secret));
    }

    [Fact]
    public void WrongSecret_ReturnsFalse()
    {
        var body      = """{"action":"opened"}""";
        var signature = Sign(body, "wrong-secret");
        var payload   = Encoding.UTF8.GetBytes(body);

        Assert.False(GitHubWebhookValidator.IsValid(payload, signature, Secret));
    }

    [Fact]
    public void MissingPrefix_ReturnsFalse()
    {
        var body      = """{"action":"opened"}""";
        var signature = Sign(body, Secret).Replace("sha256=", string.Empty);
        var payload   = Encoding.UTF8.GetBytes(body);

        Assert.False(GitHubWebhookValidator.IsValid(payload, signature, Secret));
    }

    [Fact]
    public void EmptyPayload_StillValidatesCorrectly()
    {
        var signature = Sign(string.Empty, Secret);
        Assert.True(GitHubWebhookValidator.IsValid([], signature, Secret));
    }

    [Fact]
    public void CaseInsensitivePrefix_IsAccepted()
    {
        var body      = """{"action":"opened"}""";
        var signature = Sign(body, Secret).Replace("sha256=", "SHA256=");
        var payload   = Encoding.UTF8.GetBytes(body);

        Assert.True(GitHubWebhookValidator.IsValid(payload, signature, Secret));
    }
}
