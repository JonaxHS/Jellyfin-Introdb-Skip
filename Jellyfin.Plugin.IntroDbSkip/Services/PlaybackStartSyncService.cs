using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.IntroDbSkip.Metadata;
using MediaBrowser.Controller.Entities;
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
    private readonly ConcurrentDictionary<string, (string SessionId, long StartTicks, long EndTicks)> _pendingAndroidSkips = new();
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
        if (e.Session?.Id is not null)
        {
            var sessionId = e.Session.Id;
            var staleKeys = _pendingAndroidSkips.Where(kvp => kvp.Value.SessionId == sessionId).Select(kvp => kvp.Key).ToList();
            foreach (var key in staleKeys)
            {
                _pendingAndroidSkips.TryRemove(key, out _);
                _androidSeekInFlight.TryRemove(key, out _);
            }
        }

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
                            (e.Session.Id, marker.StartMs * TimeSpan.TicksPerMillisecond, marker.EndMs * TimeSpan.TicksPerMillisecond);

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

        PreFetchNextEpisode(episode);
    }

    private void PreFetchNextEpisode(Episode currentEpisode)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var seriesId = currentEpisode.SeriesId;
                if (seriesId == Guid.Empty) return;

                var nextEpisode = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ParentId = seriesId,
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    IsVirtualItem = false
                })
                .OfType<Episode>()
                .Where(e => (e.ParentIndexNumber > currentEpisode.ParentIndexNumber) ||
                           (e.ParentIndexNumber == currentEpisode.ParentIndexNumber && e.IndexNumber > currentEpisode.IndexNumber))
                .OrderBy(e => e.ParentIndexNumber)
                .ThenBy(e => e.IndexNumber)
                .FirstOrDefault();

                if (nextEpisode != null)
                {
                    _logger.LogInformation("Pre-fetching markers for next episode: {SeriesName} S{Season:00}E{Episode:00}",
                        nextEpisode.SeriesName, nextEpisode.ParentIndexNumber, nextEpisode.IndexNumber);
                    await _episodeIntroSyncService.SyncEpisodeAsync(nextEpisode, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to pre-fetch next episode for {SeriesName}", currentEpisode.SeriesName);
            }
        });
    }

    private void SessionManagerOnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        if (!Plugin.Instance.PluginConfiguration.AndroidExoAutoSkipFallback) return;

        bool isAndroid = IsAndroidClient(e);
        if (!isAndroid) return;

        if (string.IsNullOrWhiteSpace(e.PlaySessionId))
        {
            return;
        }

        if (!_pendingAndroidSkips.TryGetValue(e.PlaySessionId, out var marker))
        {
            // If not prepared yet, we don't try to sync here to avoid blocking progress handling
            return;
        }

        var positionTicks = e.PlaybackPositionTicks ?? e.Session?.PlayState?.PositionTicks;
        if (!positionTicks.HasValue) return;

        // Only skip if we are INSIDE the intro window
        if (positionTicks.Value < marker.StartTicks || positionTicks.Value >= marker.EndTicks)
        {
            return;
        }

        var sessionId = e.Session?.Id;
        if (string.IsNullOrWhiteSpace(sessionId)) return;

        if (!_androidSeekInFlight.TryAdd(e.PlaySessionId, 0))
        {
            return;
        }

        _logger.LogInformation("Android Exo detected in intro window ({Pos}ms). Scheduling fallback seek to {Target}ms.", 
            TimeSpan.FromTicks(positionTicks.Value).TotalMilliseconds, 
            TimeSpan.FromTicks(marker.EndTicks).TotalMilliseconds);

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
            // The Android app (ExoPlayer) often ignores Seek commands during the initial buffering phase.
            // We'll try up to 3 times with increasing delays until it works or we exit the window.
            for (int i = 0; i < 3; i++)
            {
                // Delay: 1.5s, 3.5s, 5.5s
                await Task.Delay(TimeSpan.FromMilliseconds(1500 + (i * 2000))).ConfigureAwait(false);

                if (!_pendingAndroidSkips.TryGetValue(playSessionId, out var pendingWindow))
                {
                    return;
                }

                // Check if the session is still active and valid
                var session = _sessionManager.Sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session == null || session.PlayState == null)
                {
                    _logger.LogDebug("Android session {SessionId} no longer active. Stopping retry.", sessionId);
                    return;
                }

                // If the user already skipped or the video moved past the intro, we stop.
                var currentPos = session.PlayState.PositionTicks ?? 0;
                if (currentPos >= pendingWindow.EndTicks)
                {
                    _logger.LogInformation("Android session {SessionId} already past intro ({Pos}ms). Auto-skip complete.", 
                        sessionId, TimeSpan.FromTicks(currentPos).TotalMilliseconds);
                    _pendingAndroidSkips.TryRemove(playSessionId, out _);
                    return;
                }

                _logger.LogInformation("Attempt {Num} to FORCE Seek Android session {SessionId} to {Target}ms", 
                    i + 1, sessionId, TimeSpan.FromTicks(pendingWindow.EndTicks).TotalMilliseconds);

                // Using PlaystateCommand.Seek with null controller to bypass permission/state blocks
                await _sessionManager.SendPlaystateCommand(
                    null, // System command
                    sessionId,
                    new PlaystateRequest
                    {
                        Command = PlaystateCommand.Seek,
                        SeekPositionTicks = pendingWindow.EndTicks
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }

            _pendingAndroidSkips.TryRemove(playSessionId, out _);
            _logger.LogInformation("Finished aggressive skip attempts for {PlaySessionId}", playSessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed inside aggressive skip logic for {PlaySessionId}", playSessionId);
        }
        finally
        {
            _androidSeekInFlight.TryRemove(playSessionId, out _);
        }
    }

    private bool IsAndroidClient(PlaybackProgressEventArgs e)
    {
        var clientName = e.ClientName ?? e.Session?.Client;
        var deviceName = e.Session?.DeviceName;
        
        bool isAndroid = (!string.IsNullOrWhiteSpace(clientName) && clientName.Contains("android", StringComparison.OrdinalIgnoreCase))
                      || (!string.IsNullOrWhiteSpace(deviceName) && deviceName.Contains("android", StringComparison.OrdinalIgnoreCase));

        if (isAndroid)
        {
            _logger.LogDebug("Identified Android client: Client={Client}, Device={Device}", clientName, deviceName);
        }
        return isAndroid;
    }
}
