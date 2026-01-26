using Microsoft.Extensions.DependencyInjection;
using SmartInspect.Relay.AspNetCore.Configuration;
using SmartInspect.Relay.AspNetCore.Services;

namespace SmartInspect.Relay.AspNetCore;

/// <summary>
/// Extension methods for configuring SmartInspect relay services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SmartInspect relay services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // Basic usage - WebSocket to console
    /// builder.Services.AddSmartInspectRelay(options =>
    /// {
    ///     options.ConsoleHost = "localhost";
    ///     options.ConsolePort = 4229;
    /// });
    ///
    /// // Callback mode - Forward through your own logging
    /// builder.Services.AddSmartInspectRelay(options =>
    /// {
    ///     options.Mode = RelayMode.Callback;
    ///     options.OnLogEntry = (level, title, data, viewer) =>
    ///     {
    ///         // Forward to your SmartInspect session
    ///         mySession.LogMessage(title);
    ///     };
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddSmartInspectRelay(
        this IServiceCollection services,
        Action<SmartInspectRelayOptions>? configure = null)
    {
        // Register options
        var optionsBuilder = services.AddOptions<SmartInspectRelayOptions>();
        if (configure != null)
        {
            optionsBuilder.Configure(configure);
        }

        // Register the appropriate forwarder based on mode
        // We need to resolve this at runtime since mode is in options
        services.AddSingleton<ILogForwarder>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmartInspectRelayOptions>>();
            var mode = options.Value.Mode;

            return mode switch
            {
                RelayMode.Callback => ActivatorUtilities.CreateInstance<CallbackForwarder>(sp),
                _ => ActivatorUtilities.CreateInstance<WebSocketForwarder>(sp)
            };
        });

        // Register hosted service for lifecycle management
        services.AddHostedService<ForwarderHostedService>();

        return services;
    }

    /// <summary>
    /// Adds SmartInspect relay services with WebSocket mode (connect to console).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="consoleHost">Console host address. Default: localhost.</param>
    /// <param name="consolePort">Console WebSocket port. Default: 4229.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSmartInspectRelay(
        this IServiceCollection services,
        string consoleHost,
        int consolePort = 4229)
    {
        return services.AddSmartInspectRelay(options =>
        {
            options.Mode = RelayMode.WebSocket;
            options.ConsoleHost = consoleHost;
            options.ConsolePort = consolePort;
        });
    }

    /// <summary>
    /// Adds SmartInspect relay services with callback mode for custom forwarding.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="onLogEntry">Callback for log entries. Parameters: level, title, data, viewerType.</param>
    /// <param name="onWatch">Optional callback for watch messages. Parameters: name, value, watchType.</param>
    /// <param name="onProcessFlow">Optional callback for process flow. Parameters: flowType, title.</param>
    /// <param name="onControl">Optional callback for control commands. Parameters: command.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // Forward browser logs through SmartInspect
    /// var browserSession = SiAuto.Si.AddSession("Browser");
    /// builder.Services.AddSmartInspectRelay(
    ///     onLogEntry: (level, title, data, viewer) =>
    ///     {
    ///         switch (level)
    ///         {
    ///             case "error": browserSession.LogError(title); break;
    ///             case "warning": browserSession.LogWarning(title); break;
    ///             default: browserSession.LogMessage(title); break;
    ///         }
    ///     },
    ///     onWatch: (name, value, type) => browserSession.WatchString(name, value)
    /// );
    /// </code>
    /// </example>
    public static IServiceCollection AddSmartInspectRelay(
        this IServiceCollection services,
        Action<string, string, string?, string?> onLogEntry,
        Action<string, string, string>? onWatch = null,
        Action<string, string>? onProcessFlow = null,
        Action<string>? onControl = null)
    {
        return services.AddSmartInspectRelay(options =>
        {
            options.Mode = RelayMode.Callback;
            options.OnLogEntry = onLogEntry;
            options.OnWatch = onWatch;
            options.OnProcessFlow = onProcessFlow;
            options.OnControl = onControl;
        });
    }
}
