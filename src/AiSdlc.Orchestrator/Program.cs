using AiSdlc.Agents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IAgent, BusinessAnalystAgent>();
        services.AddSingleton<IAgentRunner, AgentRunner>();
    })
    .Build();

host.Run();
