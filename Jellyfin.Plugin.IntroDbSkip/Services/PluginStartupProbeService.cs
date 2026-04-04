using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IntroDbSkip.Services;

/// <summary>
/// Logs a startup probe when the plugin assembly is loaded by Jellyfin.
/// </summary>
public class PluginStartupProbeService : IHostedService
{
    private readonly ILogger<PluginStartupProbeService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginStartupProbeService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public PluginStartupProbeService(ILogger<PluginStartupProbeService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IntroDB Skip plugin loaded and hosted services started at {TimeUtc}", DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
