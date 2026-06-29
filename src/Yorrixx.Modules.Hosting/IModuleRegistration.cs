using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Yorrixx.Shared;

// Re-homed from Yorrixx.Shared (ADR-XR-0001 sever): HostingModule is the only consumer in the moved code,
// so the one interface travels here rather than dragging yorrixx-app's shared kernel across the boundary.
public interface IModuleRegistration
{
    void Register(IServiceCollection services, IConfiguration configuration);
}
