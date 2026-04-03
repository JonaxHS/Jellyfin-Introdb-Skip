using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.IntroDbSkip.Metadata;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IntroDbSkip.Services;

/// <summary>
/// Synchronizes intro markers for a single episode.
/// </summary>
public class EpisodeIntroSyncService
{
    private readonly IntroDbClient _introDbClient;
    private readonly IntroMarkerStore _store;
    private readonly ILogger<EpisodeIntroSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EpisodeIntroSyncService"/> class.
    /// </summary>
    public EpisodeIntroSyncService(
        IntroDbClient introDbClient,
        IntroMarkerStore store,
        ILogger<EpisodeIntroSyncService> logger)
    {
        _introDbClient = introDbClient;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Fetches and stores intro marker for one episode.
    /// </summary>
    public async Task SyncEpisodeAsync(Episode episode, CancellationToken cancellationToken)
    {
        _ = await GetOrFetchMarkerAsync(episode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a cached marker for an episode or fetches it from IntroDB.
    /// </summary>
    public async Task<CachedIntroMarker?> GetOrFetchMarkerAsync(Episode episode, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.PluginConfiguration;
        if (!config.Enabled)
        {
            return null;
        }

        if (!config.OverwriteExistingMarkers)
        {
            var existingMarker = _store.Get(episode.Id);
            if (existingMarker is not null)
            {
                return existingMarker;
            }
        }

        var season = episode.ParentIndexNumber ?? 0;
        var episodeNumber = episode.IndexNumber ?? 0;
        if (season <= 0 || episodeNumber <= 0)
        {
            return null;
        }

        var imdbId = ResolveImdbId(episode);
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return null;
        }

        IntroDbSegmentsResponse? response;
        try
        {
            response = await _introDbClient
                .GetSegmentsAsync(config.IntroDbBaseUrl, config.IntroDbApiKey, imdbId, season, episodeNumber, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IntroDB request failed for {Series} S{Season:00}E{Episode:00}", episode.SeriesName, season, episodeNumber);
            return null;
        }

        var intro = response?.Intro;
        if (intro is null || intro.Confidence < config.MinimumConfidence)
        {
            return null;
        }

        var marker = new CachedIntroMarker
        {
            ItemId = episode.Id,
            ImdbId = imdbId,
            Season = season,
            Episode = episodeNumber,
            StartMs = intro.StartMs,
            EndMs = intro.EndMs,
            Confidence = intro.Confidence,
            SyncedAt = DateTimeOffset.UtcNow
        };

        _store.Upsert(marker);

        _logger.LogDebug("Stored IntroDB marker for {Series} S{Season:00}E{Episode:00}", episode.SeriesName, season, episodeNumber);
        return marker;
    }

    private static string? ResolveImdbId(Episode episode)
    {
        var episodeImdb = episode.GetProviderId(MetadataProvider.Imdb);
        if (!string.IsNullOrWhiteSpace(episodeImdb))
        {
            return episodeImdb;
        }

        var seriesImdb = episode.Series?.GetProviderId(MetadataProvider.Imdb);
        if (!string.IsNullOrWhiteSpace(seriesImdb))
        {
            return seriesImdb;
        }

        return null;
    }
}
