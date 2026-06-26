namespace AiSdlc.Shared.Templates;

/// <summary>A loaded static template: its manifest plus the raw contents of each source file (keyed by source file name).</summary>
public sealed record StaticTemplate(TemplateManifest Manifest, IReadOnlyDictionary<string, string> Files);

/// <summary>
/// The slot values the content model produced for one build — markup-free. Scalar tokens (brand, content,
/// platform) and the per-block arrays for repeatables. Deploy tokens (e.g. <c>__CONTACT_EMAIL__</c>) are NOT
/// here: they are not <c>{{ }}</c>-shaped and are substituted at deploy, never at assembly.
/// </summary>
public sealed record TemplateAssemblyInput
{
    public IReadOnlyDictionary<string, string> Brand { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Content { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Platform { get; init; } = new Dictionary<string, string>();

    /// <summary>block name → ordered items, each item a token→value map for that block's tokens.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> Repeat { get; init; }
        = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>();
}

/// <summary>Thrown when a template cannot be assembled (unresolved tokens, bad repeat counts, missing source).</summary>
public sealed class TemplateAssemblyException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public TemplateAssemblyException(string templateId, IReadOnlyList<string> problems)
        : base($"Template '{templateId}' failed assembly: {string.Join(" ", problems)}")
        => Problems = problems;

    public TemplateAssemblyException(string message) : base(message)
        => Problems = new[] { message };
}
