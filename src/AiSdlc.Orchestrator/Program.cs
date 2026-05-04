using AiSdlc.Agents;
using AiSdlc.Agents.Personas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IAgent, ProductStrategistAgent>();
        services.AddSingleton<IAgent, ProductOwnerAgent>();
        services.AddSingleton<IAgent, BusinessAnalystAgent>();
        services.AddSingleton<IAgentRunner, AgentRunner>();
    })
    .Build();

host.Run();
