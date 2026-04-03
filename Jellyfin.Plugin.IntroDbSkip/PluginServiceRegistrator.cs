using Jellyfin.Plugin.IntroDbSkip.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

namespace Jellyfin.Plugin.IntroDbSkip;

/// <summary>
/// Registers plugin services in Jellyfin DI.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc/>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<HttpClient>();
        serviceCollection.AddSingleton<IntroDbClient>();
        serviceCollection.AddSingleton<IntroMarkerStore>();
        serviceCollection.AddSingleton<EpisodeIntroSyncService>();
        serviceCollection.AddHostedService<PlaybackStartSyncService>();
    }
}
