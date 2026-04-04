using System;
using System.Linq;
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
        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            try
            {
                var response = await _introDbClient
                    .GetIntroDbSegmentsAsync(config.IntroDbBaseUrl, config.IntroDbApiKey, imdbId, season, episodeNumber, cancellationToken)
                    .ConfigureAwait(false);

                var intro = response?.Intro;
                if (intro is not null && intro.Confidence >= config.MinimumConfidence)
                {
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IntroDB request failed for {Series} S{Season:00}E{Episode:00}", episode.SeriesName, season, episodeNumber);
            }
        }

        var tmdbId = ResolveTmdbId(episode);
        if (tmdbId is null)
        {
            return null;
        }

        try
        {
            var response = await _introDbClient
                .GetTheIntroDbMediaAsync(config.TheIntroDbBaseUrl, config.TheIntroDbApiKey, tmdbId.Value, season, episodeNumber, cancellationToken)
                .ConfigureAwait(false);

            var intro = GetBestSegment(response?.Intro);
            if (intro is null)
            {
                return null;
            }

            var marker = new CachedIntroMarker
            {
                ItemId = episode.Id,
                ImdbId = imdbId ?? string.Empty,
                Season = season,
                Episode = episodeNumber,
                StartMs = intro.Value.StartMs,
                EndMs = intro.Value.EndMs,
                Confidence = 1.0,
                SyncedAt = DateTimeOffset.UtcNow
            };

            _store.Upsert(marker);
            _logger.LogDebug("Stored TheIntroDB marker for {Series} S{Season:00}E{Episode:00}", episode.SeriesName, season, episodeNumber);
            return marker;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TheIntroDB request failed for {Series} S{Season:00}E{Episode:00}", episode.SeriesName, season, episodeNumber);
            return null;
        }
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

    private static int? ResolveTmdbId(Episode episode)
    {
        var episodeTmdb = episode.GetProviderId(MetadataProvider.Tmdb);
        if (int.TryParse(episodeTmdb, out var episodeTmdbId))
        {
            return episodeTmdbId;
        }

        var seriesTmdb = episode.Series?.GetProviderId(MetadataProvider.Tmdb);
        if (int.TryParse(seriesTmdb, out var seriesTmdbId))
        {
            return seriesTmdbId;
        }

        return null;
    }

    private static (int StartMs, int EndMs)? GetBestSegment(TheIntroDbSegmentInfo[]? segments)
    {
        if (segments is null || segments.Length == 0)
        {
            return null;
        }

        var normalizedSegments = segments
            .Where(segment => segment is not null)
            .Select(segment => NormalizeSegment(segment!))
            .Where(segment => segment.HasValue)
            .Select(segment => segment!.Value)
            .OrderBy(segment => segment.StartMs)
            .ToArray();

        if (normalizedSegments.Length == 0)
        {
            return null;
        }

        return normalizedSegments[0];
    }

    private static (int StartMs, int EndMs)? NormalizeSegment(TheIntroDbSegmentInfo segment)
    {
        var startMs = segment.StartMs ?? (segment.StartSec.HasValue ? (int)Math.Round(segment.StartSec.Value * 1000.0) : 0);
        var endMs = segment.EndMs ?? (segment.EndSec.HasValue ? (int)Math.Round(segment.EndSec.Value * 1000.0) : 0);

        if (endMs <= startMs)
        {
            return null;
        }

        return (startMs, endMs);
    }
}
