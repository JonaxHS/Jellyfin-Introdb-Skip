using System;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.IntroDbSkip.Metadata;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IntroDbSkip.Services;

/// <summary>
/// HTTP client wrapper for IntroDB API.
/// </summary>
public class IntroDbClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger<IntroDbClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntroDbClient"/> class.
    /// </summary>
    public IntroDbClient(HttpClient httpClient, ILogger<IntroDbClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Fetches episode segments from IntroDB.
    /// </summary>
    public async Task<IntroDbSegmentsResponse?> GetIntroDbSegmentsAsync(
        string baseUrl,
        string? apiKey,
        string imdbId,
        int season,
        int episode,
        CancellationToken cancellationToken)
    {
        var safeBaseUrl = NormalizeBaseUrl(baseUrl);
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{safeBaseUrl}/segments?imdb_id={Uri.EscapeDataString(imdbId)}&season={season}&episode={episode}");

        var response = await SendRequestAsync(url, apiKey, AuthHeaderMode.XApiKey, cancellationToken).ConfigureAwait(false);

        // Read endpoints are public. If configured API key is invalid, retry without it.
        if ((response?.StatusCode == System.Net.HttpStatusCode.Unauthorized || response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
            && !string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("IntroDB rejected configured API key for read endpoint. Retrying without API key.");
            response.Dispose();
            response = await SendRequestAsync(url, null, AuthHeaderMode.XApiKey, cancellationToken).ConfigureAwait(false);
        }

        if (response is null)
        {
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("IntroDB request failed with status code {StatusCode} for {Url}", (int)response.StatusCode, url);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return (IntroDbSegmentsResponse?)await JsonSerializer.DeserializeAsync(stream, typeof(IntroDbSegmentsResponse), JsonOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fetches episode segments from TheIntroDB.
    /// </summary>
    public async Task<TheIntroDbMediaResponse?> GetTheIntroDbMediaAsync(
        string baseUrl,
        string? apiKey,
        int tmdbId,
        int? season,
        int? episode,
        CancellationToken cancellationToken)
    {
        var safeBaseUrl = NormalizeBaseUrl(baseUrl, defaultUrl: "https://api.theintrodb.org/v2");
        var url = season.HasValue && episode.HasValue
            ? $"{safeBaseUrl}/media?tmdb_id={tmdbId}&season={season}&episode={episode}"
            : $"{safeBaseUrl}/media?tmdb_id={tmdbId}";
 
        var config = Plugin.Instance.PluginConfiguration;
        var response = await SendRequestAsync(url, apiKey, AuthHeaderMode.Bearer, cancellationToken, config.TheIntroDbCookie, config.TheIntroDbUserAgent).ConfigureAwait(false);

        if ((response?.StatusCode == System.Net.HttpStatusCode.Unauthorized || response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
            && !string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("TheIntroDB rejected configured API key for read endpoint. Retrying without API key.");
            response.Dispose();
            response = await SendRequestAsync(url, null, AuthHeaderMode.Bearer, cancellationToken, config.TheIntroDbCookie, config.TheIntroDbUserAgent).ConfigureAwait(false);
        }

        if (response is null)
        {
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("TheIntroDB request failed with status code {StatusCode} for {Url}", (int)response.StatusCode, url);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("TheIntroDB response for {Url}: {JsonSnippet}", url, json.Length > 200 ? json.Substring(0, 200) + "..." : json);

            return JsonSerializer.Deserialize<TheIntroDbMediaResponse>(json, JsonOptions);
        }
    }

    private async Task<HttpResponseMessage?> SendRequestAsync(
        string url,
        string? apiKey,
        AuthHeaderMode authHeaderMode,
        CancellationToken cancellationToken,
        string? cookie = null,
        string? userAgentOverride = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var token = apiKey.Trim();
            if (authHeaderMode == AuthHeaderMode.XApiKey)
            {
                request.Headers.TryAddWithoutValidation("X-API-Key", token);
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
 
        var ua = !string.IsNullOrWhiteSpace(userAgentOverride) ? userAgentOverride : "Jellyfin.Plugin.IntroDbSkip/1.6.3.0";
        request.Headers.TryAddWithoutValidation("User-Agent", ua);
 
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
 
        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeBaseUrl(string baseUrl, string? defaultUrl = null)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url))
        {
            return defaultUrl ?? "https://api.introdb.app";
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (url.Contains("introdb.app/docs", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.introdb.app";
        }

        if (url.Contains("theintrodb.org/docs", StringComparison.OrdinalIgnoreCase))
        {
            return defaultUrl ?? "https://api.theintrodb.org/v2";
        }

        return url;
    }

    private enum AuthHeaderMode
    {
        XApiKey,
        Bearer
    }
}
