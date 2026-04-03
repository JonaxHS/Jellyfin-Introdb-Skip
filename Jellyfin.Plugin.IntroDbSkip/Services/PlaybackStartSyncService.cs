using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.IntroDbSkip.Metadata;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IntroDbSkip.Services;

/// <summary>
/// Fetches IntroDB markers on playback start events.
/// </summary>
public class PlaybackStartSyncService : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSegmentManager _mediaSegmentManager;
    private readonly EpisodeIntroSyncService _episodeIntroSyncService;
    private readonly ILogger<PlaybackStartSyncService> _logger;
    private readonly ConcurrentDictionary<string, (long StartTicks, long EndTicks)> _pendingAndroidSkips = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackStartSyncService"/> class.
    /// </summary>
    public PlaybackStartSyncService(
        ISessionManager sessionManager,
        ILibraryManager libraryManager,
        IMediaSegmentManager mediaSegmentManager,
        EpisodeIntroSyncService episodeIntroSyncService,
        ILogger<PlaybackStartSyncService> logger)
    {
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _mediaSegmentManager = mediaSegmentManager;
        _episodeIntroSyncService = episodeIntroSyncService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += SessionManagerOnPlaybackStart;
        _sessionManager.PlaybackProgress += SessionManagerOnPlaybackProgress;
        _sessionManager.PlaybackStopped += SessionManagerOnPlaybackStopped;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= SessionManagerOnPlaybackStart;
        _sessionManager.PlaybackProgress -= SessionManagerOnPlaybackProgress;
        _sessionManager.PlaybackStopped -= SessionManagerOnPlaybackStopped;
        _pendingAndroidSkips.Clear();
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
                var options = _libraryManager.GetLibraryOptions(episode);
                await _mediaSegmentManager
                    .RunSegmentPluginProviders(episode, options, false, CancellationToken.None)
                    .ConfigureAwait(false);

                if (Plugin.Instance.PluginConfiguration.AndroidExoAutoSkipFallback && IsAndroidClient(e))
                {
                    CachedIntroMarker? marker = await _episodeIntroSyncService
                        .GetOrFetchMarkerAsync(episode, CancellationToken.None)
                        .ConfigureAwait(false);

                    if (marker is not null && marker.EndMs > marker.StartMs && !string.IsNullOrWhiteSpace(e.PlaySessionId))
                    {
                        _pendingAndroidSkips[e.PlaySessionId] =
                            (marker.StartMs * TimeSpan.TicksPerMillisecond, marker.EndMs * TimeSpan.TicksPerMillisecond);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh media segments on playback start for item {ItemId}", episode.Id);
            }
        });
    }

    private void SessionManagerOnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        if (!Plugin.Instance.PluginConfiguration.AndroidExoAutoSkipFallback || !IsAndroidClient(e) || string.IsNullOrWhiteSpace(e.PlaySessionId))
        {
            return;
        }

        if (!_pendingAndroidSkips.TryGetValue(e.PlaySessionId, out var marker))
        {
            return;
        }

        var positionTicks = e.PlaybackPositionTicks ?? e.Session?.PlayState?.PositionTicks;
        if (!positionTicks.HasValue)
        {
            return;
        }

        if (positionTicks.Value < marker.StartTicks || positionTicks.Value >= marker.EndTicks)
        {
            return;
        }

        var sessionId = e.Session?.Id;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _pendingAndroidSkips.TryRemove(e.PlaySessionId, out _);

        _ = Task.Run(async () =>
        {
            try
            {
                await _sessionManager.SendPlaystateCommand(
                    sessionId,
                    sessionId,
                    new PlaystateRequest
                    {
                        Command = PlaystateCommand.Seek,
                        SeekPositionTicks = marker.EndTicks
                    },
                    CancellationToken.None).ConfigureAwait(false);

                _logger.LogDebug("Applied Android Exo fallback skip for playSession {PlaySessionId}", e.PlaySessionId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed Android Exo fallback skip for playSession {PlaySessionId}", e.PlaySessionId);
            }
        });
    }

    private void SessionManagerOnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.PlaySessionId))
        {
            _pendingAndroidSkips.TryRemove(e.PlaySessionId, out _);
        }
    }

    private static bool IsAndroidClient(PlaybackProgressEventArgs e)
    {
        var clientName = e.ClientName ?? e.Session?.Client;
        return !string.IsNullOrWhiteSpace(clientName)
            && clientName.Contains("android", StringComparison.OrdinalIgnoreCase);
    }
}
