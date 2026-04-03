using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IntroDbSkip.Services;

/// <summary>
/// Fetches IntroDB markers on playback start events.
/// </summary>
public class PlaybackStartSyncService : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly EpisodeIntroSyncService _episodeSyncService;
    private readonly ILogger<PlaybackStartSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackStartSyncService"/> class.
    /// </summary>
    public PlaybackStartSyncService(
        ISessionManager sessionManager,
        EpisodeIntroSyncService episodeSyncService,
        ILogger<PlaybackStartSyncService> logger)
    {
        _sessionManager = sessionManager;
        _episodeSyncService = episodeSyncService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += SessionManagerOnPlaybackStart;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= SessionManagerOnPlaybackStart;
        return Task.CompletedTask;
    }

    private void SessionManagerOnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (!Plugin.Instance.PluginConfiguration.SyncOnPlaybackStart)
        {
            return;
        }

        if (e.Item is not Episode episode)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _episodeSyncService.SyncEpisodeAsync(episode, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed syncing IntroDB marker on playback start for item {ItemId}", episode.Id);
            }
        });
    }
}
