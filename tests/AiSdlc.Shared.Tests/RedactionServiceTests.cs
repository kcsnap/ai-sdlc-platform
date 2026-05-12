using AiSdlc.Shared.Redaction;
using Xunit;

namespace AiSdlc.Shared.Tests;

public sealed class RedactionServiceTests
{
    private readonly RegexRedactionService _svc = new();

    [Fact]
    public void Redact_PlainText_ReturnsUnchanged()
    {
        var result = _svc.Redact("Hello world, this is a normal sentence.");
        Assert.Equal("Hello world, this is a normal sentence.", result.RedactedText);
        Assert.Equal(0, result.RedactionCount);
    }

    [Fact]
    public void Redact_AnthropicApiKey_IsRedacted()
    {
        var input  = "api key: sk-ant-api03-abc123def456ghi789jkl012mno345pqr678stu901vwx234yz";
        var result = _svc.Redact(input);
        Assert.DoesNotContain("sk-ant-api", result.RedactedText);
        Assert.Contains("[REDACTED:ANTHROPIC_API_KEY]", result.RedactedText);
        Assert.Contains("Anthropic API key", result.RedactedPatterns);
    }

    [Fact]
    public void Redact_GitHubPat_IsRedacted()
    {
        var input  = "token ghp_abcdefghijklmnopqrstuvwxyz123456789012";
        var result = _svc.Redact(input);
        Assert.DoesNotContain("ghp_", result.RedactedText);
        Assert.Contains("[REDACTED:GITHUB_PAT]", result.RedactedText);
    }

    [Fact]
    public void Redact_EmailAddress_IsRedacted()
    {
        var input  = "Contact us at support@example.com for help.";
        var result = _svc.Redact(input);
        Assert.DoesNotContain("support@example.com", result.RedactedText);
        Assert.Contains("[REDACTED:EMAIL]", result.RedactedText);
    }

    [Fact]
    public void Redact_CreditCard_IsRedacted()
    {
        var input  = "Card: 4111111111111111 should be redacted";
        var result = _svc.Redact(input);
        Assert.DoesNotContain("4111111111111111", result.RedactedText);
        Assert.Contains("[REDACTED:CARD_NUMBER]", result.RedactedText);
    }

    [Fact]
    public void Redact_MultipleSecrets_AllRedacted()
    {
        var input  = "key=sk-ant-api03-abc123def456ghi789jkl012mno345pqr678stu901vwx email=user@test.com";
        var result = _svc.Redact(input);
        Assert.True(result.RedactionCount >= 2);
        Assert.DoesNotContain("sk-ant-api", result.RedactedText);
        Assert.DoesNotContain("user@test.com", result.RedactedText);
    }

    [Fact]
    public void Redact_JWT_IsRedacted()
    {
        var jwt   = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var input = $"Authorization header contains: {jwt}";
        var result = _svc.Redact(input);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", result.RedactedText);
    }

    [Fact]
    public void Redact_EmptyString_ReturnsEmpty()
    {
        var result = _svc.Redact(string.Empty);
        Assert.Equal(string.Empty, result.RedactedText);
        Assert.Equal(0, result.RedactionCount);
    }
}
