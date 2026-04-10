using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.IntroDbSkip.Metadata;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IntroDbSkip.Services;

/// <summary>
/// Synchronizes intro markers for a single episode.
/// </summary>
public class EpisodeIntroSyncService
{
    private readonly IntroDbClient _introDbClient;
    private readonly IntroMarkerStore _store;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<EpisodeIntroSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EpisodeIntroSyncService"/> class.
    /// </summary>
    public EpisodeIntroSyncService(
        IntroDbClient introDbClient,
        IntroMarkerStore store,
        ILibraryManager libraryManager,
        ILogger<EpisodeIntroSyncService> logger)
    {
        _introDbClient = introDbClient;
        _store = store;
        _libraryManager = libraryManager;
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

        var season = episode.ParentIndexNumber ?? 0;
        var episodeNumber = episode.IndexNumber ?? 0;
        if (season <= 0 || episodeNumber <= 0)
        {
            _store.Remove(episode.Id);
            _logger.LogDebug("Episode {SeriesName} S{Season:00}E{Episode:00} has invalid numbers. Skipping marker sync.", episode.SeriesName, season, episodeNumber);
            return null;
        }

        var (imdbId, tmdbId) = ResolveMediaIds(episode);
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            _store.Remove(episode.Id);
            _logger.LogWarning("No se encontró IMDb ID para {SeriesName} S{Season:00}E{Episode:00}. Imposible buscar intros.", 
                episode.SeriesName, episode.ParentIndexNumber, episode.IndexNumber);
            return null;
        }

        if (!config.OverwriteExistingMarkers)
        {
            var existingMarker = _store.Get(episode.Id);
            if (existingMarker is not null)
            {
                if (IsMarkerForEpisode(existingMarker, imdbId, season, episodeNumber))
                {
                    return existingMarker;
                }

                _logger.LogWarning(
                    "Discarding stale cached marker for item {ItemId}. Cached={CachedImdb} S{CachedSeason:00}E{CachedEpisode:00}, Current={CurrentImdb} S{CurrentSeason:00}E{CurrentEpisode:00}",
                    episode.Id,
                    existingMarker.ImdbId,
                    existingMarker.Season,
                    existingMarker.Episode,
                    imdbId,
                    season,
                    episodeNumber);
                _store.Remove(episode.Id);
            }
        }

        _logger.LogInformation("Buscando intros para {SeriesName} S{Season:00}E{Episode:00} (IMDb: {ImdbId})", 
            episode.SeriesName, episode.ParentIndexNumber, episode.IndexNumber, imdbId);

        (int StartMs, int EndMs)? introSegment = null;
        (int StartMs, int EndMs)? recapSegment = null;
        (int StartMs, int? EndMs)? creditsSegment = null;

        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            try
            {
                _logger.LogInformation("Attempting IntroDB sync for {Series} S{Season:00}E{Episode:00} using IMDb {ImdbId}", episode.SeriesName, season, episodeNumber, imdbId);
                var response = await _introDbClient
                    .GetIntroDbSegmentsAsync(config.IntroDbBaseUrl, config.IntroDbApiKey, imdbId, season, episodeNumber, cancellationToken)
                    .ConfigureAwait(false);

                introSegment = NormalizeIntroDbSegment(response?.Intro);
                recapSegment = NormalizeIntroDbSegment(response?.Recap);
                var introDbCredits = NormalizeIntroDbSegment(response?.Outro);
                if (introDbCredits is not null)
                {
                    creditsSegment = (introDbCredits.Value.StartMs, introDbCredits.Value.EndMs);
                }
                
                if (response != null && response.Intro != null)
                {
                    _logger.LogInformation("IntroDB encontró un marcador para {SeriesName} S{Season:00}E{Episode:00}", 
                        episode.SeriesName, episode.ParentIndexNumber, episode.IndexNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IntroDB request failed for {Series} S{Season:00}E{Episode:00}", episode.SeriesName, season, episodeNumber);
            }
        }

        // If intro was already found in IntroDB, do not query IntroHater.
        if (!string.IsNullOrWhiteSpace(imdbId) && introSegment is null)
        {
            try
            {
                _logger.LogInformation("Attempting IntroHater sync for {Series} S{Season:00}E{Episode:00} using IMDb {ImdbId}", episode.SeriesName, season, episodeNumber, imdbId);
                var response = await _introDbClient
                    .GetIntroHaterSegmentsAsync(config.IntroHaterBaseUrl, imdbId, season, episodeNumber, cancellationToken)
                    .ConfigureAwait(false);

                if (response is not null)
                {
                    var intro = response.FirstOrDefault(s => s.Label?.Equals("Intro", StringComparison.OrdinalIgnoreCase) == true);
                    var recap = response.FirstOrDefault(s => s.Label?.Equals("Recap", StringComparison.OrdinalIgnoreCase) == true);
                    var credits = response.FirstOrDefault(s => s.Label?.Contains("Credits", StringComparison.OrdinalIgnoreCase) == true
                                                               || s.Label?.Contains("Outro", StringComparison.OrdinalIgnoreCase) == true);

                    introSegment ??= NormalizeIntroHaterSegment(intro);
                    recapSegment ??= NormalizeIntroHaterSegment(recap);
                    var haterCredits = NormalizeIntroHaterSegment(credits);
                    if (haterCredits is not null)
                    {
                        creditsSegment ??= (haterCredits.Value.StartMs, haterCredits.Value.EndMs);
                    }
                    
                    if (intro != null)
                    {
                        _logger.LogInformation("IntroHater encontró un marcador para {SeriesName} S{Season:00}E{Episode:00}", 
                            episode.SeriesName, episode.ParentIndexNumber, episode.IndexNumber);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IntroHater request failed for {Series} S{Season:00}E{Episode:00}", episode.SeriesName, season, episodeNumber);
            }
        }

        if (introSegment is null && recapSegment is null && creditsSegment is null)
        {
            _store.Remove(episode.Id);
            _logger.LogInformation("No se encontraron intros en ninguna fuente para {SeriesName} S{Season:00}E{Episode:00}", 
                episode.SeriesName, episode.ParentIndexNumber, episode.IndexNumber);
            return null;
        }

        var marker = new CachedIntroMarker
        {
            ItemId = episode.Id,
            ImdbId = imdbId ?? string.Empty,
            Season = season,
            Episode = episodeNumber,
            StartMs = introSegment?.StartMs ?? 0,
            EndMs = introSegment?.EndMs ?? 0,
            RecapStartMs = recapSegment?.StartMs,
            RecapEndMs = recapSegment?.EndMs,
            CreditsStartMs = creditsSegment?.StartMs,
            CreditsEndMs = creditsSegment?.EndMs,
            Confidence = introSegment is not null ? 1.0 : 0,
            SyncedAt = DateTimeOffset.UtcNow
        };

        _store.Upsert(marker);
        _logger.LogDebug("Stored marker(s) for {Series} S{Season:00}E{Episode:00} intro={HasIntro} recap={HasRecap} credits={HasCredits}",
            episode.SeriesName,
            season,
            episodeNumber,
            introSegment is not null,
            recapSegment is not null,
            creditsSegment is not null);
        return marker;
    }

    private (string? ImdbId, int? TmdbId) ResolveMediaIds(Episode episode)
    {
        var series = episode.Series ?? _libraryManager.GetItemById(episode.SeriesId) as Series;
        if (series is null)
        {
            _logger.LogWarning("Could not resolve series for episode {SeriesName} ({EpisodeId})", episode.SeriesName, episode.Id);
            
            // Fallback to episode IDs if series is missing (better than nothing)
            var episodeImdb = episode.GetProviderId(MetadataProvider.Imdb);
            var episodeTmdbStr = episode.GetProviderId(MetadataProvider.Tmdb);
            int.TryParse(episodeTmdbStr, out var episodeTmdbParsed);
            int? episodeTmdb = episodeTmdbParsed > 0 ? episodeTmdbParsed : null;
            return (episodeImdb, episodeTmdb);
        }

        var seriesImdb = series.GetProviderId(MetadataProvider.Imdb);
        var seriesTmdbStr = series.GetProviderId(MetadataProvider.Tmdb);
        int.TryParse(seriesTmdbStr, out var seriesTmdbParsed);
        int? seriesTmdb = seriesTmdbParsed > 0 ? seriesTmdbParsed : null;

        return (seriesImdb, seriesTmdb);
    }

    private static (int StartMs, int EndMs)? NormalizeIntroHaterSegment(IntroHaterSegment? segment)
    {
        if (segment is null)
        {
            return null;
        }

        var startMs = (int)Math.Round(segment.Start * 1000.0);
        var endMs = (int)Math.Round(segment.End * 1000.0);

        if (endMs <= startMs)
        {
            return null;
        }

        return (startMs, endMs);
    }

    private static (int StartMs, int EndMs)? NormalizeIntroDbSegment(SegmentInfo? segment)
    {
        if (segment is null)
        {
            return null;
        }

        var startMs = segment.StartMs > 0
            ? segment.StartMs
            : (int)Math.Round(segment.StartSec * 1000.0);
        var endMs = segment.EndMs > 0
            ? segment.EndMs
            : (int)Math.Round(segment.EndSec * 1000.0);

        if (endMs <= startMs)
        {
            return null;
        }

        return (startMs, endMs);
    }

    private static bool IsMarkerForEpisode(CachedIntroMarker marker, string imdbId, int season, int episode)
    {
        return marker.Season == season
               && marker.Episode == episode
               && string.Equals(marker.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase);
    }
}
