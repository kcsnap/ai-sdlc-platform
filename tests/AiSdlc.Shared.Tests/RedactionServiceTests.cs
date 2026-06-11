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

    [Theory]
    [InlineData("Sort code: 20-12-34")]
    [InlineData("Sort code: 20 12 34")]
    [InlineData("Sort code: 201234")]
    public void Redact_UkSortCode_IsRedacted(string input)
    {
        var result = _svc.Redact(input);
        Assert.Contains("[REDACTED:SORT_CODE]", result.RedactedText);
        Assert.Contains("UK sort code", result.RedactedPatterns);
    }

    [Theory]
    [InlineData("\"createdAt\": \"2024-01-15T00:00:00Z\"")]
    [InlineData("Date: 2026-05-16")]
    [InlineData("Timestamp: 2024-01-01T00:00:00Z")]
    public void Redact_IsoDate_IsNotRedacted(string input)
    {
        var result = _svc.Redact(input);
        Assert.DoesNotContain("[REDACTED:SORT_CODE]", result.RedactedText);
    }

    [Theory]
    [InlineData("Connect to 10.0.0.5 over SSH")]
    [InlineData("gateway is 192.168.1.10")]
    [InlineData("internal host 172.16.0.1.")]
    public void Redact_PrivateIp_IsRedacted(string input)
    {
        var result = _svc.Redact(input);
        Assert.Contains("[REDACTED:PRIVATE_IP]", result.RedactedText);
        Assert.Contains("IPv4 address (private)", result.RedactedPatterns);
    }

    [Theory]
    [InlineData("\"ts-node\": \"^10.9.2\"")]
    [InlineData("upgraded to v10.9.1 yesterday")]
    [InlineData("Windows build 10.0.19041.1")]
    [InlineData("chain 1.10.2.3.4 is not an address")]
    public void Redact_VersionString_IsNotRedactedAsPrivateIp(string input)
    {
        var result = _svc.Redact(input);
        Assert.Equal(input, result.RedactedText);
    }

    [Fact]
    public void Redact_ConnectionStringPassword_RedactsValueAndKeepsKey()
    {
        var result = _svc.Redact("Server=db;User Id=app;Password=S3cr3tV@lue;Encrypt=true");
        Assert.Contains("Password=[REDACTED:DB_PASSWORD]", result.RedactedText);
        Assert.DoesNotContain("S3cr3tV@lue", result.RedactedText);
    }

    [Fact]
    public void Redact_EnvVarPassword_RedactsValueAndKeepsKey()
    {
        var result = _svc.Redact("DB_PASSWORD=hunter2secret");
        Assert.Equal("DB_PASSWORD=[REDACTED:DB_PASSWORD]", result.RedactedText);
    }

    [Theory]
    [InlineData("DB_PASSWORD=your_password")]
    [InlineData("SMTP_PASSWORD=<password>")]
    [InlineData("Password=${DB_PASSWORD}")]
    [InlineData("PGPASSWORD=changeme")]
    public void Redact_PlaceholderPassword_IsNotRedacted(string input)
    {
        var result = _svc.Redact(input);
        Assert.Equal(input, result.RedactedText);
    }
}
