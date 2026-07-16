using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy;

namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// AI module plugin — registers AI services when enabled.
/// The AI module does not add middleware to the proxy pipeline;
/// it only provides API endpoints and services for the Dashboard ChatBot.
/// </summary>
public class AIPlugin : IGatewayPlugin
{
    public string PluginId => "ai";
    public string DisplayName => "AI Assistant";
    public string Version => "1.0";
    public string Description => "AI Assistant: intelligent chatbot, log analysis, and smart notifications powered by LLM.";

    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null)
    {
        // AI services are registered by DashboardServiceCollectionExtensions.AddDashboardAI()
    }

    public void ConfigureMiddleware(IApplicationBuilder app)
    {
        // AI module has no middleware — it works via API endpoints only
    }

    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline)
    {
        // AI does not intercept proxy traffic
    }
}
