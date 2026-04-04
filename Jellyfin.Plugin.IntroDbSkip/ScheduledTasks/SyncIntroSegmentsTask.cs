using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.IntroDbSkip.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IntroDbSkip.ScheduledTasks;

/// <summary>
/// Backfills intro markers from the configured intro sources.
/// </summary>
public class SyncIntroSegmentsTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly EpisodeIntroSyncService _episodeSyncService;
    private readonly ILogger<SyncIntroSegmentsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncIntroSegmentsTask"/> class.
    /// </summary>
    public SyncIntroSegmentsTask(
        ILibraryManager libraryManager,
        EpisodeIntroSyncService episodeSyncService,
        ILogger<SyncIntroSegmentsTask> logger)
    {
        _libraryManager = libraryManager;
        _episodeSyncService = episodeSyncService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "Sync IntroDB markers";

    /// <inheritdoc/>
    public string Key => "IntroDbSkipSyncTask";

    /// <inheritdoc/>
    public string Description => "Backfills intro markers for all episodes from IntroDB and TheIntroDB.";

    /// <inheritdoc/>
    public string Category => "Metadata";

    /// <inheritdoc/>
    public bool IsHidden => false;

    /// <inheritdoc/>
    public bool IsEnabled => true;

    /// <inheritdoc/>
    public bool IsLogged => true;

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

        var episodeItems = episodes.OfType<Episode>().ToArray();
        var total = Math.Max(episodeItems.Length, 1);

        var completed = 0;
        await Parallel.ForEachAsync(
            episodeItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            },
            async (episode, token) =>
            {
                await _episodeSyncService.SyncEpisodeAsync(episode, token).ConfigureAwait(false);

                var finished = Interlocked.Increment(ref completed);
                progress.Report(finished * 100.0 / total);
            }).ConfigureAwait(false);

        _logger.LogInformation("IntroDB sync finished. Scanned {Count} episode(s).", episodeItems.Length);
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.StartupTrigger
            },
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(24).Ticks
            }
        ];
    }
}
