using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartInspect.Relay.AspNetCore.Services;

/// <summary>
/// Hosted service that manages the lifecycle of the log forwarder.
/// </summary>
public class ForwarderHostedService : IHostedService
{
    private readonly ILogForwarder _forwarder;
    private readonly ILogger<ForwarderHostedService> _logger;

    public ForwarderHostedService(
        ILogForwarder forwarder,
        ILogger<ForwarderHostedService> logger)
    {
        _forwarder = forwarder;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting SmartInspect relay forwarder");
        try
        {
            await _forwarder.StartAsync(cancellationToken);
            _logger.LogInformation("SmartInspect relay forwarder started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start SmartInspect relay forwarder");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping SmartInspect relay forwarder");
        try
        {
            await _forwarder.StopAsync();
            _logger.LogInformation("SmartInspect relay forwarder stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping SmartInspect relay forwarder");
        }
    }
}
