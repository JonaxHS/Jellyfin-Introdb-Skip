using System;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.IntroDbSkip.Metadata;

namespace Jellyfin.Plugin.IntroDbSkip.Services;

/// <summary>
/// HTTP client wrapper for IntroDB API.
/// </summary>
public class IntroDbClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntroDbClient"/> class.
    /// </summary>
    public IntroDbClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Fetches episode segments from IntroDB.
    /// </summary>
    public async Task<IntroDbSegmentsResponse?> GetSegmentsAsync(
        string baseUrl,
        string? apiKey,
        string imdbId,
        int season,
        int episode,
        CancellationToken cancellationToken)
    {
        var safeBaseUrl = baseUrl.TrimEnd('/');
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{safeBaseUrl}/segments?imdb_id={Uri.EscapeDataString(imdbId)}&season={season}&episode={episode}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", apiKey.Trim());
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<IntroDbSegmentsResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
