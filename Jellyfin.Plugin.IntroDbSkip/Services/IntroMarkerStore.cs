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
    private Dictionary<Guid, CachedIntroMarker> _markers;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntroMarkerStore"/> class.
    /// </summary>
    public IntroMarkerStore(IApplicationPaths applicationPaths)
    {
        var dataDir = Path.Combine(applicationPaths.DataPath, "introdbskip");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "markers.json");
        _markers = LoadAllInternal();
    }

    /// <summary>
    /// Upserts an intro marker for a media item.
    /// </summary>
    public void Upsert(CachedIntroMarker marker)
    {
        lock (_sync)
        {
            _markers[marker.ItemId] = marker;
            SaveAllInternal(_markers);
        }
    }

    /// <summary>
    /// Gets a marker for an item id.
    /// </summary>
    public CachedIntroMarker? Get(Guid itemId)
    {
        lock (_sync)
        {
            return _markers.GetValueOrDefault(itemId);
        }
    }

    /// <summary>
    /// Gets all cached markers.
    /// </summary>
    public IReadOnlyCollection<CachedIntroMarker> GetAll()
    {
        lock (_sync)
        {
            return _markers.Values.ToArray();
        }
    }

    /// <summary>
    /// Removes a marker by item id.
    /// </summary>
    public void Remove(Guid itemId)
    {
        lock (_sync)
        {
            if (_markers.Remove(itemId))
            {
                SaveAllInternal(_markers);
            }
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
