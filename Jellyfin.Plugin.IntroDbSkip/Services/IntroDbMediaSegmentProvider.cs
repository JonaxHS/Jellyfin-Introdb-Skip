using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IntroDbSkip.Services;

/// <summary>
/// Media segment provider that resolves Intro segments from IntroDB using episode metadata.
/// </summary>
public class IntroDbMediaSegmentProvider : IMediaSegmentProvider
{
    private readonly ILibraryManager _libraryManager;
    private readonly EpisodeIntroSyncService _episodeIntroSyncService;
    private readonly IntroMarkerStore _markerStore;
    private readonly ILogger<IntroDbMediaSegmentProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntroDbMediaSegmentProvider"/> class.
    /// </summary>
    public IntroDbMediaSegmentProvider(
        ILibraryManager libraryManager,
        EpisodeIntroSyncService episodeIntroSyncService,
        IntroMarkerStore markerStore,
        ILogger<IntroDbMediaSegmentProvider> logger)
    {
        _libraryManager = libraryManager;
        _episodeIntroSyncService = episodeIntroSyncService;
        _markerStore = markerStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "IntroDB Skip";

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(MediaSegmentGenerationRequest request, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(request.ItemId);
        if (item is not Episode episode)
        {
            return [];
        }

        var marker = await _episodeIntroSyncService.GetOrFetchMarkerAsync(episode, cancellationToken).ConfigureAwait(false);
        if (marker is null)
        {
            return [];
        }

        var segments = new List<MediaSegmentDto>();
        TryAddSegment(segments, request.ItemId, marker.StartMs, marker.EndMs, MediaSegmentType.Intro);
        TryAddSegment(segments, request.ItemId, marker.RecapStartMs, marker.RecapEndMs, MediaSegmentType.Recap);
        TryAddSegment(segments, request.ItemId, marker.CreditsStartMs, marker.CreditsEndMs, MediaSegmentType.Outro);

        if (segments.Count == 0)
        {
            _logger.LogDebug("No valid segments available for item {ItemId}", request.ItemId);
            return [];
        }

        return segments;
    }

    /// <inheritdoc />
    public ValueTask<bool> Supports(BaseItem item)
    {
        return ValueTask.FromResult(item is Episode);
    }

    /// <inheritdoc />
    public Task CleanupExtractedData(Guid itemId, CancellationToken cancellationToken)
    {
        _markerStore.Remove(itemId);
        return Task.CompletedTask;
    }

    private static void TryAddSegment(
        ICollection<MediaSegmentDto> segments,
        Guid itemId,
        int? startMs,
        int? endMs,
        MediaSegmentType type)
    {
        if (!startMs.HasValue || !endMs.HasValue)
        {
            return;
        }

        var startTicks = startMs.Value * TimeSpan.TicksPerMillisecond;
        var endTicks = endMs.Value * TimeSpan.TicksPerMillisecond;
        if (endTicks <= startTicks)
        {
            return;
        }

        segments.Add(new MediaSegmentDto
        {
            Id = Guid.NewGuid(),
            ItemId = itemId,
            StartTicks = startTicks,
            EndTicks = endTicks,
            Type = type
        });
    }
}
