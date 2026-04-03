using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.IntroDbSkip.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.IntroDbSkip;

/// <summary>
/// Main plugin class.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer)
        : base(appPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin Instance { get; private set; } = null!;

    /// <inheritdoc/>
    public override string Name => "IntroDB Skip";

    /// <inheritdoc/>
    public override string Description => "Sync intro markers from IntroDB to support skip intro workflows.";

    /// <inheritdoc/>
    public override Guid Id => new("a6c05116-3d5b-4419-b5be-52de81f7c8d9");

    /// <summary>
    /// Gets strongly typed plugin configuration.
    /// </summary>
    public PluginConfiguration PluginConfiguration => Configuration;

    /// <inheritdoc/>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
