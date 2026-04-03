using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.IntroDbSkip.Configuration;

/// <summary>
/// Plugin configuration for IntroDB synchronization.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        IntroDbBaseUrl = "https://api.introdb.app";
        IntroDbApiKey = string.Empty;
        SyncIntervalHours = 24;
        MinimumConfidence = 0.75;
        OverwriteExistingMarkers = false;
        SyncOnPlaybackStart = true;
        Enabled = true;
    }

    /// <summary>
    /// Gets or sets the IntroDB API base url.
    /// </summary>
    public string IntroDbBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the IntroDB API key.
    /// </summary>
    public string IntroDbApiKey { get; set; }

    /// <summary>
    /// Gets or sets how often to run synchronization.
    /// </summary>
    public int SyncIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets the minimum confidence accepted from IntroDB.
    /// </summary>
    public double MinimumConfidence { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the task overwrites existing local markers.
    /// </summary>
    public bool OverwriteExistingMarkers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether markers are fetched when playback starts.
    /// </summary>
    public bool SyncOnPlaybackStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether synchronization is enabled.
    /// </summary>
    public bool Enabled { get; set; }
}
