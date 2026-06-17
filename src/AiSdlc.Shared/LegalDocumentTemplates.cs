namespace AiSdlc.Shared;

/// <summary>
/// Static, reusable legal-document templates injected into every generated user-app.
/// These are committed verbatim by the platform (no AI generation per build — one generic
/// set, reused everywhere, to save tokens) so every app ships a Privacy Policy and Terms of
/// Service. Each carries a prominent disclaimer that it is a non-production template requiring
/// legal review before the app goes public. Their guaranteed presence is what lets the risk
/// gate stop blocking greenfield builds on "no privacy policy / no GDPR framework".
/// </summary>
public static class LegalDocumentTemplates
{
    // Vite's publicDir for the user-app frontend (the frontend root is src/frontend). Only files
    // placed here are served at the site root, e.g. /privacy-policy.html — which is what the shell's
    // AppShell footer links to. Injecting at repo-root public/ was never served (#135). The template
    // ships placeholders here so it builds and the footer resolves out-of-the-box; this per-app
    // injection overwrites them with the real disclaimer + UK/EU-GDPR content. Legal pages are not
    // in the immutable shell, so drift-restore does not revert the injection.
    public const string FrontendPublicDir = "src/frontend/public/";

    public const string PrivacyPolicyPath = FrontendPublicDir + "privacy-policy.html";
    public const string TermsOfServicePath = FrontendPublicDir + "terms-of-service.html";

    /// <summary>Relative URLs the generated site must link to (served from FrontendPublicDir at root).</summary>
    public const string PrivacyPolicyUrl = "/privacy-policy.html";
    public const string TermsOfServiceUrl = "/terms-of-service.html";

    /// <summary>(repo-relative path, file content) pairs to commit into every build.</summary>
    public static IReadOnlyList<(string Path, string Content)> All =>
    [
        (PrivacyPolicyPath, PrivacyPolicyHtml),
        (TermsOfServicePath, TermsOfServiceHtml),
    ];

    private const string DisclaimerBanner = """
            <div style="border:2px solid #b91c1c;background:#fef2f2;color:#7f1d1d;padding:16px 20px;margin:0 0 28px;border-radius:8px;font-size:15px;line-height:1.5">
              <strong>⚠️ Template document — not production ready.</strong>
              This is a generic starting template generated automatically. It has <strong>not</strong> been
              reviewed by a lawyer and is almost certainly incomplete for your jurisdiction and business.
              <br><br>
              <strong>Before this app goes public you must:</strong>
              <ol style="margin:8px 0 0 18px;padding:0">
                <li>Have this document reviewed and adapted by a qualified legal professional.</li>
                <li>Replace every <code>[bracketed placeholder]</code> (company/operator name, contact email, governing jurisdiction, data-retention periods).</li>
                <li>Confirm it accurately describes the personal data you actually collect, why, how long you keep it, and who you share it with.</li>
                <li>Add your data-subject request process (access, export, deletion) and a real contact route for privacy enquiries.</li>
              </ol>
            </div>
""";

    private static string Page(string title, string body) =>
        $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="robots" content="noindex">
          <title>{{title}}</title>
          <style>
            body{font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;max-width:820px;margin:0 auto;padding:40px 24px;color:#1f2937;line-height:1.6}
            h1{font-size:28px;margin:0 0 8px}h2{font-size:20px;margin:28px 0 8px}
            a{color:#2563eb}code{background:#f3f4f6;padding:1px 4px;border-radius:4px}
            footer{margin-top:40px;padding-top:16px;border-top:1px solid #e5e7eb;font-size:13px;color:#6b7280}
          </style>
        </head>
        <body>
        {{DisclaimerBanner}}
          <h1>{{title}}</h1>
          <p><em>Last updated: [date] — Operated by [Company / Operator name].</em></p>
        {{body}}
          <footer>This is a template document and does not constitute legal advice.</footer>
        </body>
        </html>
        """;

    public static readonly string PrivacyPolicyHtml = Page("Privacy Policy", """
          <p>This Privacy Policy explains how [Company / Operator name] ("we", "us") collects, uses,
          and protects personal data when you use this application (the "Service").</p>

          <h2>1. Data we collect</h2>
          <p>Depending on how you use the Service we may collect: account identifiers and profile
          details you provide (e.g. name, email), authentication data managed by our identity
          provider, content you submit, and basic technical/usage data needed to operate the Service.</p>

          <h2>2. How we use it</h2>
          <p>We use personal data to provide and secure the Service, to authenticate you, to deliver
          the features you request, and to comply with legal obligations. We do not sell your
          personal data.</p>

          <h2>3. Legal basis (UK GDPR / EU GDPR)</h2>
          <p>We process personal data on the bases of performing our contract with you, your consent
          where applicable, our legitimate interests in operating the Service, and compliance with
          legal obligations.</p>

          <h2>4. Storage and retention</h2>
          <p>Data is stored with our cloud infrastructure provider and retained only as long as
          necessary for the purposes above or as required by law. [Specify retention periods.]</p>

          <h2>5. Sharing</h2>
          <p>We share data only with processors that help us run the Service (e.g. hosting and
          authentication providers) under appropriate agreements, or where required by law.</p>

          <h2>6. Your rights</h2>
          <p>Subject to applicable law you may request access to, correction of, export of, or
          deletion of your personal data, and may object to or restrict certain processing. To make a
          request, contact [privacy contact email]. You may also complain to your local data
          protection authority.</p>

          <h2>7. Contact</h2>
          <p>Questions about this policy: [privacy contact email].</p>
        """);

    public static readonly string TermsOfServiceHtml = Page("Terms of Service", """
          <p>These Terms govern your use of this application (the "Service") operated by
          [Company / Operator name]. By using the Service you agree to these Terms.</p>

          <h2>1. Use of the Service</h2>
          <p>You must use the Service lawfully and not misuse it, attempt to disrupt it, or access it
          other than through the interfaces we provide. You are responsible for activity under your
          account.</p>

          <h2>2. Accounts</h2>
          <p>You must provide accurate account information and keep your credentials secure.
          Authentication is handled by our identity provider.</p>

          <h2>3. Content</h2>
          <p>You retain ownership of content you submit and grant us the rights necessary to operate
          the Service. You are responsible for ensuring you have the right to submit it.</p>

          <h2>4. Availability and changes</h2>
          <p>The Service is provided on an "as is" and "as available" basis. We may modify or
          discontinue features at any time.</p>

          <h2>5. Limitation of liability</h2>
          <p>To the maximum extent permitted by law, [Company / Operator name] is not liable for
          indirect or consequential losses arising from use of the Service. [Adapt to your
          jurisdiction.]</p>

          <h2>6. Governing law</h2>
          <p>These Terms are governed by the laws of [governing jurisdiction].</p>

          <h2>7. Contact</h2>
          <p>Questions about these Terms: [contact email].</p>
        """);
}
