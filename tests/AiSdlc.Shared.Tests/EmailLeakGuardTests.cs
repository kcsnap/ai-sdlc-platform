using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Shared.Tests;

public sealed class EmailLeakGuardTests
{
    [Fact]
    public void Flags_a_literal_email_in_a_mailto()
    {
        var files = new[]
        {
            new FileChange("index.html", "<a href=\"mailto:coach@sport121.co.uk?subject=Coaching\">Email</a>")
        };

        var violations = EmailLeakGuard.Scan(files);

        var v = Assert.Single(violations);
        Assert.Equal("index.html", v.Path);
        Assert.Equal("coach@sport121.co.uk", v.Email);
    }

    [Fact]
    public void Allows_the_deploy_substituted_placeholder_token()
    {
        var files = new[]
        {
            new FileChange("index.html", "<a href=\"mailto:__CONTACT_EMAIL__?subject=Hi\">Email</a>"),
            new FileChange("app.js", "access_key: \"__WEB3FORMS_ACCESS_KEY__\"")
        };

        Assert.Empty(EmailLeakGuard.Scan(files));
    }

    // Pins the agreed Platform↔Yorrixx token (__CONTACT_EMAIL__, substituted at deploy) as guard-safe
    // across every mailto context the seeded contract emits — hero, footer, and the coach/team/staff/
    // agent cards that leaked a literal on 2026-06-25. A future regex change must never flag this token.
    [Theory]
    [InlineData("<a class=\"hero-cta\" href=\"mailto:__CONTACT_EMAIL__?subject=Coaching enquiry — Tennis\">Get in touch</a>")]
    [InlineData("<footer><a href=\"mailto:__CONTACT_EMAIL__\">Contact us</a></footer>")]
    [InlineData("<div class=\"coach-card\"><a href=\"mailto:__CONTACT_EMAIL__?subject=Coach\">Email coach</a></div>")]
    public void Allows_contact_email_token_in_every_mailto_context(string html)
        => Assert.Empty(EmailLeakGuard.Scan(new[] { new FileChange("index.html", html) }));

    [Theory]
    [InlineData("name@example.com")]
    [InlineData("hello@example.org")]
    [InlineData("test@EXAMPLE.NET")]
    public void Allows_rfc2606_reserved_example_domains(string email)
    {
        var files = new[] { new FileChange("acceptance.spec.ts", $"await page.fill('#email', '{email}');") };

        Assert.Empty(EmailLeakGuard.Scan(files));
    }

    [Fact]
    public void Does_not_match_npm_scoped_packages_or_urls()
    {
        var files = new[]
        {
            new FileChange("package.json", "\"@clerk/clerk-react\": \"^5.0.0\""),
            new FileChange("app.js", "fetch('https://api.web3forms.com/submit')")
        };

        Assert.Empty(EmailLeakGuard.Scan(files));
    }

    [Fact]
    public void Reports_every_distinct_leak_across_files()
    {
        var files = new[]
        {
            new FileChange("index.html", "mailto:a@real.com and mailto:b@real.com"),
            new FileChange("about.html", "mailto:a@real.com")  // same address, different file → distinct
        };

        var violations = EmailLeakGuard.Scan(files);

        Assert.Equal(3, violations.Count);
    }

    [Fact]
    public void Dedupes_repeats_of_the_same_address_within_one_file()
    {
        var files = new[]
        {
            new FileChange("index.html", "mailto:a@real.com header ... a@real.com footer")
        };

        Assert.Single(EmailLeakGuard.Scan(files));
    }

    [Fact]
    public void Ignores_empty_content()
        => Assert.Empty(EmailLeakGuard.Scan(new[] { new FileChange("styles.css", "") }));
}
