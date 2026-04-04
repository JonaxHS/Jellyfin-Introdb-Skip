using System;
using System.Collections.Generic;
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

            await _episodeSyncService.SyncEpisodeAsync(episode, cancellationToken).ConfigureAwait(false);

            index++;
            progress.Report(index * 100.0 / total);
        }

        _logger.LogInformation("IntroDB sync finished. Scanned {Count} item(s).", episodes.Count);
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
