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
        // Value-only (lookbehind keeps the key intact so generated .env/config files stay parseable),
        // and skip obvious placeholder/template values — redacting "your_password" only corrupts examples.
        new("Connection string with password", @"(?<=Password=)(?!your_|change|placeholder|example|<|\$\{|\$\(|%)[^;'""\s]{4,}", "[REDACTED:DB_PASSWORD]"),

        // PII
        new("Email address",         @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", "[REDACTED:EMAIL]"),
        new("UK National Insurance", @"\b[A-CEGHJ-PR-TW-Z]{1}[A-CEGHJ-NPR-TW-Z]{1}[0-9]{6}[A-DFM]{1}\b", "[REDACTED:NINO]"),
        // Sort code: exactly 6 consecutive digits OR two-digit groups with hyphen/space separators.
        // The optional-separator form (\d{2}[-\s]?\d{2}[-\s]?\d{2}) matches ISO dates like 2024-01-15
        // because it greedily treats '20'+'24'+'-01' as three groups — this explicit alternation avoids that.
        new("UK sort code",          @"(?<!\d)(?:\d{6}|\d{2}-\d{2}-\d{2}|\d{2}\s\d{2}\s\d{2})(?!\d)", "[REDACTED:SORT_CODE]"),
        new("Credit card number",    @"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|3[47][0-9]{13}|3(?:0[0-5]|[68][0-9])[0-9]{11}|6(?:011|5[0-9]{2})[0-9]{12})\b", "[REDACTED:CARD_NUMBER]"),
        // Requires all four octets in the 0-255 range: three-part semver ("10.9.2") and version strings
        // with large segments ("10.0.19041.1") must not match. Lookarounds reject longer dotted runs
        // ("1.10.2.3.4") while still allowing a sentence-ending full stop after a real address.
        new("IPv4 address (private)",@"(?<![\d.])(?:10(?:\.(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}|172\.(?:1[6-9]|2\d|3[01])(?:\.(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){2}|192\.168(?:\.(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){2})(?!\.?\d)", "[REDACTED:PRIVATE_IP]"),
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
