using System.Text.RegularExpressions;

namespace AiSdlc.Shared.Redaction;

/// <summary>
/// Regex-based redaction of secrets and common PII patterns.
/// Redaction is conservative: it prefers false positives over missed secrets.
/// </summary>
public sealed class RegexRedactionService : IRedactionService
{
    private static readonly IReadOnlyList<RedactionRule> Rules =
    [
        // Secrets
        new("Anthropic API key",     @"sk-ant-api\d{2}-[A-Za-z0-9_\-]{40,}",        "[REDACTED:ANTHROPIC_API_KEY]"),
        new("OpenAI API key",        @"sk-[A-Za-z0-9]{20,}",                         "[REDACTED:OPENAI_API_KEY]"),
        new("GitHub PAT (classic)",  @"ghp_[A-Za-z0-9]{36,}",                        "[REDACTED:GITHUB_PAT]"),
        new("GitHub fine-grained PAT", @"github_pat_[A-Za-z0-9_]{82,}",             "[REDACTED:GITHUB_PAT]"),
        new("Azure SAS token",       @"[?&]sig=[A-Za-z0-9%+/=]{20,}",               "[REDACTED:AZURE_SAS_SIG]"),
        new("Azure storage key",     @"AccountKey=[A-Za-z0-9+/=]{44,}",             "[REDACTED:AZURE_STORAGE_KEY]"),
        new("Bearer token",          @"Bearer\s+[A-Za-z0-9\-._~+/]+=*",             "[REDACTED:BEARER_TOKEN]"),
        new("JWT",                   @"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", "[REDACTED:JWT]"),
        new("Private key header",    @"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----", "[REDACTED:PRIVATE_KEY]"),
        new("Connection string with password", @"Password=[^;'""\s]{4,}",            "[REDACTED:DB_PASSWORD]"),

        // PII
        new("Email address",         @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", "[REDACTED:EMAIL]"),
        new("UK National Insurance", @"\b[A-CEGHJ-PR-TW-Z]{1}[A-CEGHJ-NPR-TW-Z]{1}[0-9]{6}[A-DFM]{1}\b", "[REDACTED:NINO]"),
        new("UK sort code",          @"\b\d{2}[-\s]?\d{2}[-\s]?\d{2}\b",            "[REDACTED:SORT_CODE]"),
        new("Credit card number",    @"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|3[47][0-9]{13}|3(?:0[0-5]|[68][0-9])[0-9]{11}|6(?:011|5[0-9]{2})[0-9]{12})\b", "[REDACTED:CARD_NUMBER]"),
        new("IPv4 address (private)",@"\b(?:10|172\.(?:1[6-9]|2\d|3[01])|192\.168)\.[0-9]{1,3}\.[0-9]{1,3}\b", "[REDACTED:PRIVATE_IP]"),
    ];

    public RedactionResult Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
            return new RedactionResult { RedactedText = input, RedactionCount = 0 };

        var result     = input;
        var count      = 0;
        var triggered  = new List<string>();

        foreach (var rule in Rules)
        {
            var replaced = rule.Pattern.Replace(result, m =>
            {
                count++;
                if (!triggered.Contains(rule.Name)) triggered.Add(rule.Name);
                return rule.Replacement;
            });
            result = replaced;
        }

        return new RedactionResult
        {
            RedactedText     = result,
            RedactionCount   = count,
            RedactedPatterns = triggered
        };
    }

    private sealed record RedactionRule(string Name, string PatternString, string Replacement)
    {
        public Regex Pattern { get; } = new(PatternString,
            RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    }
}
