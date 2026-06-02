namespace AiSdlc.Events.Contract;

/// <summary>
/// Discriminator for the per-event <see cref="EventData"/> payload carried inside an <see cref="EventEnvelope"/>.
/// New values may be added in a contract minor version bump; unknown values deserialize as <see cref="Unknown"/>.
/// </summary>
public enum EventType
{
    /// <summary>
    /// Reserved for unrecognized discriminators from a newer contract version. See <see cref="UnknownEventData"/>.
    /// </summary>
    Unknown = 0,

    /// <summary>GitHub webhook landed on the orchestrator (issues / issue_comment / pull_request).</summary>
    WebhookReceived,

    /// <summary>Orchestrator instance created for the run.</summary>
    WorkflowStarted,

    /// <summary>Persona agent began executing.</summary>
    AgentStarted,

    /// <summary>Persona agent finished successfully.</summary>
    AgentCompleted,

    /// <summary>Persona agent raised an exception.</summary>
    AgentFailed,

    /// <summary>Orchestrator posted a markdown comment to the issue or PR.</summary>
    CommentPosted,

    /// <summary>Terminal: workflow released (PR merged, deployment recorded).</summary>
    WorkflowReleased,

    /// <summary>Terminal: workflow stopped (human stop, gate failure, abort).</summary>
    WorkflowStopped,

    /// <summary>Terminal: workflow failed with an unrecoverable exception.</summary>
    WorkflowFailed,

    /// <summary>Bootstrap-mode-only completion signal mirroring the HTML-comment marker introduced in PR #51.</summary>
    BootstrapTerminalMarker,
}
