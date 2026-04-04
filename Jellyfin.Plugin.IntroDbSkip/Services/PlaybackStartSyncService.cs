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
    private readonly ConcurrentDictionary<string, byte> _androidSeekInFlight = new();

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
        _androidSeekInFlight.Clear();
        return Task.CompletedTask;
    }

    private void SessionManagerOnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (!Plugin.Instance.PluginConfiguration.SyncOnPlaybackStart)
        {
            _logger.LogDebug("SyncOnPlaybackStart is disabled. Skipping marker refresh.");
            return;
        }

        if (e.Item is not Episode episode)
        {
            _logger.LogDebug("Playback started for non-episode item {ItemName}. Skipping marker sync.", e.Item?.Name);
            return;
        }

        _logger.LogInformation("Playback started for {SeriesName} S{Season:00}E{Episode:00}. Triggering marker refresh.", episode.SeriesName, episode.ParentIndexNumber, episode.IndexNumber);

        _ = Task.Run(async () =>
        {
            try
            {
                var options = _libraryManager.GetLibraryOptions(episode);
                _logger.LogDebug("Running segment plugin providers for item {ItemId}", episode.Id);
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

                        _logger.LogDebug(
                            "Prepared Android Exo fallback window for playSession {PlaySessionId}: {StartMs}ms-{EndMs}ms",
                            e.PlaySessionId,
                            marker.StartMs,
                            marker.EndMs);
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

        if (!_androidSeekInFlight.TryAdd(e.PlaySessionId, 0))
        {
            return;
        }

        _ = ScheduleAndroidFallbackSeekAsync(sessionId, e.PlaySessionId, marker.EndTicks);
    }

    private void SessionManagerOnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.PlaySessionId))
        {
            _pendingAndroidSkips.TryRemove(e.PlaySessionId, out _);
            _androidSeekInFlight.TryRemove(e.PlaySessionId, out _);
        }
    }

    private async Task ScheduleAndroidFallbackSeekAsync(string sessionId, string playSessionId, long seekPositionTicks)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), CancellationToken.None).ConfigureAwait(false);

            if (!_pendingAndroidSkips.TryGetValue(playSessionId, out var pendingWindow))
            {
                return;
            }

            seekPositionTicks = pendingWindow.EndTicks;

            await _sessionManager.SendPlaystateCommand(
                sessionId,
                sessionId,
                new PlaystateRequest
                {
                    Command = PlaystateCommand.Seek,
                    SeekPositionTicks = seekPositionTicks
                },
                CancellationToken.None).ConfigureAwait(false);

            _pendingAndroidSkips.TryRemove(playSessionId, out _);
            _logger.LogDebug("Applied Android Exo fallback seek for playSession {PlaySessionId}", playSessionId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed Android Exo fallback seek for playSession {PlaySessionId}", playSessionId);
        }
        finally
        {
            _androidSeekInFlight.TryRemove(playSessionId, out _);
        }
    }

    private static bool IsAndroidClient(PlaybackProgressEventArgs e)
    {
        var clientName = e.ClientName ?? e.Session?.Client;
        return !string.IsNullOrWhiteSpace(clientName)
            && clientName.Contains("android", StringComparison.OrdinalIgnoreCase);
    }
}
