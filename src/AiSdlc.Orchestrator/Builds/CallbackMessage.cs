namespace AiSdlc.Orchestrator.Builds;

/// <summary>
/// A platform→Yorrixx callback to send: POST {CallbackBaseUrl}/apps/{AppId}/{Kind} with the pre-serialized
/// JSON body. Kind is "status" | "runtime" | "verification".
/// </summary>
public sealed record CallbackMessage(string CallbackBaseUrl, string AppId, string Kind, string PayloadJson);
