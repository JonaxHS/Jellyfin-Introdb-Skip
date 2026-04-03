using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.IntroDbSkip.Metadata;
using Jellyfin.Plugin.IntroDbSkip.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IntroDbSkip.ScheduledTasks;

/// <summary>
/// Periodically syncs intro markers from IntroDB.
/// </summary>
public class SyncIntroSegmentsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IntroDbClient _introDbClient;
    private readonly IntroMarkerStore _store;
    private readonly ILogger<SyncIntroSegmentsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncIntroSegmentsTask"/> class.
    /// </summary>
    public SyncIntroSegmentsTask(
        ILibraryManager libraryManager,
        IntroDbClient introDbClient,
        IntroMarkerStore store,
        ILogger<SyncIntroSegmentsTask> logger)
    {
        _libraryManager = libraryManager;
        _introDbClient = introDbClient;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "Sync IntroDB markers";

    /// <inheritdoc/>
    public string Key => "IntroDbSkipSyncTask";

    /// <inheritdoc/>
    public string Description => "Fetches intro segments from IntroDB and stores local marker cache for episodes.";

    /// <inheritdoc/>
    public string Category => "Metadata";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance.PluginConfiguration;
        if (!config.Enabled)
        {
            _logger.LogInformation("IntroDB sync disabled by configuration.");
            return;
        }

        var episodes = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            IsVirtualItem = false,
            Recursive = true
        });

        var index = 0;
        var total = Math.Max(episodes.Count, 1);

        foreach (var item in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item is not Episode episode)
            {
                index++;
                progress.Report(index * 100.0 / total);
                continue;
            }

            await SyncEpisodeAsync(
                episode,
                config.MinimumConfidence,
                config.IntroDbBaseUrl,
                config.IntroDbApiKey,
                cancellationToken).ConfigureAwait(false);

            index++;
            progress.Report(index * 100.0 / total);
        }

        _logger.LogInformation("IntroDB sync finished. Scanned {Count} item(s).", episodes.Count);
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var hours = Math.Max(1, Plugin.Instance.PluginConfiguration.SyncIntervalHours);
        return [new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(hours).Ticks }];
    }

    private async Task SyncEpisodeAsync(
        Episode episode,
        double minConfidence,
        string baseUrl,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        var season = episode.ParentIndexNumber ?? 0;
        var episodeNumber = episode.IndexNumber ?? 0;

        if (season <= 0 || episodeNumber <= 0)
        {
            return;
        }

        var imdbId = ResolveImdbId(episode);
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return;
        }

        IntroDbSegmentsResponse? response;
        try
        {
            response = await _introDbClient
                .GetSegmentsAsync(baseUrl, apiKey, imdbId, season, episodeNumber, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IntroDB request failed for {Series} S{Season:00}E{Episode:00}", episode.SeriesName, season, episodeNumber);
            return;
        }

        var intro = response?.Intro;
        if (intro is null || intro.Confidence < minConfidence)
        {
            return;
        }

        _store.Upsert(new CachedIntroMarker
        {
            ItemId = episode.Id,
            ImdbId = imdbId,
            Season = season,
            Episode = episodeNumber,
            StartMs = intro.StartMs,
            EndMs = intro.EndMs,
            Confidence = intro.Confidence,
            SyncedAt = DateTimeOffset.UtcNow
        });
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
