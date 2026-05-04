namespace AiSdlc.ModelProviders;

public interface IModelProvider
{
    string ProviderName { get; }
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}
