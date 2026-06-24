namespace AiSdlc.RepoIndex.Charter;

/// <summary>The top-level shape of a generated app. Selects the repo template + the scaffold contract.</summary>
public enum StackProfile
{
    Static,
    FullStack
}

/// <summary>
/// The deterministic Static-vs-FullStack gate (responsibility-split phase 1, locked): a build is
/// <see cref="StackProfile.Static"/> iff it needs NONE of auth, email, payments, AI-API, or persistence —
/// otherwise <see cref="StackProfile.FullStack"/>. This outer gate is a HARD RULE with NO LLM/Balanced
/// judgment: an LLM-derived gate risked a marketing one-pager non-deterministically landing FullStack
/// (the v011 regression). Capability axes INSIDE FullStack stay agent-derived (<see cref="CapabilityResolver"/>).
/// </summary>
public static class StackProfileResolver
{
    public static StackProfile Resolve(Charter charter)
    {
        ArgumentNullException.ThrowIfNull(charter);
        var c = charter.Constraints;
        var needsBackend = c.NeedsAuth || c.NeedsEmail || c.NeedsPayments || c.NeedsAIApi || c.NeedsPersistence;
        return needsBackend ? StackProfile.FullStack : StackProfile.Static;
    }

    /// <summary>The string form threaded through the pipeline metadata ("Static" / "FullStack").</summary>
    public static string ResolveName(Charter charter) => Resolve(charter).ToString();
}
