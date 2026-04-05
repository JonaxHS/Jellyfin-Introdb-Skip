using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.IntroDbSkip.Configuration;

/// <summary>
/// Configuración del plugin para la sincronización de intros.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="PluginConfiguration"/>.
    /// </summary>
    public PluginConfiguration()
    {
        IntroDbBaseUrl = "https://api.introdb.app";
        IntroDbApiKey = string.Empty;
        IntroHaterBaseUrl = "https://introhater.com";
        SyncIntervalHours = 24;
        MinimumConfidence = 0.75;
        OverwriteExistingMarkers = false;
        SyncOnPlaybackStart = true;
        AndroidExoAutoSkipFallback = true;
        Enabled = true;
    }


    /// <summary>
    /// URL base de la API de IntroDB.
    /// </summary>
    public string IntroDbBaseUrl { get; set; }

    /// <summary>
    /// Clave de API para IntroDB.
    /// </summary>
    public string IntroDbApiKey { get; set; }

    /// <summary>
    /// URL base de la API de IntroHater.
    /// </summary>
    public string IntroHaterBaseUrl { get; set; }

    /// <summary>
    /// Frecuencia de sincronización (en horas).
    /// </summary>
    public int SyncIntervalHours { get; set; }

    /// <summary>
    /// Confianza mínima aceptada de los marcadores.
    /// </summary>
    public double MinimumConfidence { get; set; }

    /// <summary>
    /// Indica si se deben sobrescribir los marcadores locales existentes.
    /// </summary>
    public bool OverwriteExistingMarkers { get; set; }

    /// <summary>
    /// Indica si se deben buscar marcadores al iniciar la reproducción.
    /// </summary>
    public bool SyncOnPlaybackStart { get; set; }

    /// <summary>
    /// Indica si se debe forzar el salto de intro en clientes Android/Exo.
    /// </summary>
    public bool AndroidExoAutoSkipFallback { get; set; }

    /// <summary>
    /// Indica si el plugin está habilitado.
    /// </summary>
    public bool Enabled { get; set; }
}
