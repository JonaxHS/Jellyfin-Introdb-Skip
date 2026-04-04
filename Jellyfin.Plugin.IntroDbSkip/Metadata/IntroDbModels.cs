using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.IntroDbSkip.Metadata;

/// <summary>
/// Segment payload for intro/recap/outro.
/// </summary>
public sealed class SegmentInfo
{
    [JsonPropertyName("start_ms")]
    public int StartMs { get; set; }

    [JsonPropertyName("end_ms")]
    public int EndMs { get; set; }

    [JsonPropertyName("start_sec")]
    public double StartSec { get; set; }

    [JsonPropertyName("end_sec")]
    public double EndSec { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("submission_count")]
    public int SubmissionCount { get; set; }
}

/// <summary>
/// IntroDB response for GET /segments.
/// </summary>
public sealed class IntroDbSegmentsResponse
{
    [JsonPropertyName("imdb_id")]
    public string ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("episode")]
    public int Episode { get; set; }

    [JsonPropertyName("intro")]
    public SegmentInfo? Intro { get; set; }

    [JsonPropertyName("recap")]
    public SegmentInfo? Recap { get; set; }

    [JsonPropertyName("outro")]
    public SegmentInfo? Outro { get; set; }
}

/// <summary>
/// Segment payload returned by TheIntroDB.
/// </summary>
public sealed class TheIntroDbSegmentInfo
{
    [JsonPropertyName("start_ms")]
    public int? StartMs { get; set; }

    [JsonPropertyName("end_ms")]
    public int? EndMs { get; set; }

    [JsonPropertyName("start_sec")]
    public double? StartSec { get; set; }

    [JsonPropertyName("end_sec")]
    public double? EndSec { get; set; }
}

/// <summary>
/// TheIntroDB response for GET /media.
/// </summary>
public sealed class TheIntroDbMediaResponse
{
    [JsonPropertyName("tmdb_id")]
    public int? TmdbId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("intro")]
    public TheIntroDbSegmentInfo[]? Intro { get; set; }

    [JsonPropertyName("recap")]
    public TheIntroDbSegmentInfo[]? Recap { get; set; }

    [JsonPropertyName("credits")]
    public TheIntroDbSegmentInfo[]? Credits { get; set; }

    [JsonPropertyName("preview")]
    public TheIntroDbSegmentInfo[]? Preview { get; set; }
}

/// <summary>
/// Cached marker entry mapped to a Jellyfin item.
/// </summary>
public sealed class CachedIntroMarker
{
    public Guid ItemId { get; set; }

    public string ImdbId { get; set; } = string.Empty;

    public int Season { get; set; }

    public int Episode { get; set; }

    public int StartMs { get; set; }

    public int EndMs { get; set; }

    public double Confidence { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
