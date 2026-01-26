using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartInspect.Relay.AspNetCore.Internal;
using SmartInspect.Relay.AspNetCore.Services;

namespace SmartInspect.Relay.AspNetCore;

/// <summary>
/// Extension methods for mapping SmartInspect relay endpoints.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the SmartInspect relay endpoints to the application.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="prefix">URL prefix for the endpoints. Default: "/api/v1".</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    /// <example>
    /// <code>
    /// app.MapSmartInspectRelay();           // Maps to /api/v1/logs, /api/v1/health, /api/v1/status
    /// app.MapSmartInspectRelay("/logs");    // Maps to /logs/logs, /logs/health, /logs/status
    /// </code>
    /// </example>
    public static IEndpointRouteBuilder MapSmartInspectRelay(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/api/v1")
    {
        // Normalize prefix
        prefix = prefix.TrimEnd('/');

        // POST /logs - Accept log messages
        endpoints.MapPost($"{prefix}/logs", async (HttpContext context) =>
        {
            var forwarder = context.RequestServices.GetRequiredService<ILogForwarder>();
            var logger = context.RequestServices.GetRequiredService<ILogger<RelayEndpointLogger>>();
            return await LogEndpoints.PostLogs(context, forwarder, logger);
        })
        .WithName("SmartInspectRelay_PostLogs")
        .WithTags("SmartInspect Relay");

        // GET /health - Health check
        endpoints.MapGet($"{prefix}/health", (HttpContext context) =>
        {
            var forwarder = context.RequestServices.GetRequiredService<ILogForwarder>();
            return LogEndpoints.GetHealth(forwarder);
        })
        .WithName("SmartInspectRelay_Health")
        .WithTags("SmartInspect Relay");

        // GET /status - Detailed status
        endpoints.MapGet($"{prefix}/status", (HttpContext context) =>
        {
            var forwarder = context.RequestServices.GetRequiredService<ILogForwarder>();
            return LogEndpoints.GetStatus(forwarder);
        })
        .WithName("SmartInspectRelay_Status")
        .WithTags("SmartInspect Relay");

        return endpoints;
    }
}
