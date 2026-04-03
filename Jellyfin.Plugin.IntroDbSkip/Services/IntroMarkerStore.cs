using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.IntroDbSkip.Metadata;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.IntroDbSkip.Services;

/// <summary>
/// Persists synchronized intro markers in plugin data.
/// </summary>
public class IntroMarkerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly object _sync = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="IntroMarkerStore"/> class.
    /// </summary>
    public IntroMarkerStore(IApplicationPaths applicationPaths)
    {
        var dataDir = Path.Combine(applicationPaths.DataPath, "introdbskip");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "markers.json");
    }

    /// <summary>
    /// Upserts an intro marker for a media item.
    /// </summary>
    public void Upsert(CachedIntroMarker marker)
    {
        lock (_sync)
        {
            var items = LoadAllInternal();
            items[marker.ItemId] = marker;
            SaveAllInternal(items);
        }
    }

    /// <summary>
    /// Gets a marker for an item id.
    /// </summary>
    public CachedIntroMarker? Get(Guid itemId)
    {
        lock (_sync)
        {
            var items = LoadAllInternal();
            return items.GetValueOrDefault(itemId);
        }
    }

    /// <summary>
    /// Gets all cached markers.
    /// </summary>
    public IReadOnlyCollection<CachedIntroMarker> GetAll()
    {
        lock (_sync)
        {
            return LoadAllInternal().Values.ToArray();
        }
    }

    private Dictionary<Guid, CachedIntroMarker> LoadAllInternal()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<Guid, CachedIntroMarker>();
        }

        var json = File.ReadAllText(_filePath);
        var values = JsonSerializer.Deserialize<List<CachedIntroMarker>>(json, JsonOptions) ?? [];
        return values.ToDictionary(v => v.ItemId, v => v);
    }

    private void SaveAllInternal(Dictionary<Guid, CachedIntroMarker> markers)
    {
        var json = JsonSerializer.Serialize(markers.Values.OrderBy(v => v.ItemId), JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
