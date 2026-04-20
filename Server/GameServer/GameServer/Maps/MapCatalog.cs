using System.Collections.Concurrent;
using System.Text.Json;
using GameServer.Game.Engine;
using Microsoft.Extensions.Hosting;

namespace GameServer.Maps;

public sealed class MapCatalog(IHostEnvironment environment) : IMapProvider
{
    private readonly ConcurrentDictionary<string, LoadedMap> _cache = new(StringComparer.OrdinalIgnoreCase);

    public LoadedMap Get(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new InvalidOperationException("MapIdRequired");
        }

        return _cache.GetOrAdd(mapId, Load);
    }

    private LoadedMap Load(string mapId)
    {
        var mapsDir = Path.Combine(environment.ContentRootPath, "Maps");
        var mapPath = Path.Combine(mapsDir, $"{mapId}.json");
        var tileTypesPath = Path.Combine(mapsDir, $"{mapId}-tiletypes.json");

        if (!File.Exists(mapPath))
        {
            throw new FileNotFoundException($"Map file not found: {mapPath}", mapPath);
        }

        if (!File.Exists(tileTypesPath))
        {
            throw new FileNotFoundException($"Map tiletypes file not found: {tileTypesPath}", tileTypesPath);
        }

        var map = JsonSerializer.Deserialize<MapJson>(File.ReadAllText(mapPath), JsonOptions) ??
                  throw new InvalidOperationException("InvalidMapJson");
        var tileTypes = JsonSerializer.Deserialize<TileTypesJson>(File.ReadAllText(tileTypesPath), JsonOptions) ??
                        throw new InvalidOperationException("InvalidTileTypesJson");

        ValidateMap(map);
        var tileInfo = BuildTileInfo(tileTypes);
        var blocked = BuildBlocked(map, tileInfo);
        var spawnsByOwner = FindCastleSpawns(map, tileInfo);

        var definitions = tileInfo
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new TileDefinition(kvp.Key, kvp.Value.Type, kvp.Value.Owner, IsBlockedType(kvp.Value.Type)));

        return new LoadedMap(mapId, map.Width, map.Height, map.Data, blocked, spawnsByOwner, definitions);
    }

    private static void ValidateMap(MapJson map)
    {
        if (map.Width <= 0 || map.Height <= 0)
        {
            throw new InvalidOperationException("InvalidMapDimensions");
        }

        if (map.Data.Length != map.Width * map.Height)
        {
            throw new InvalidOperationException("InvalidMapDataLength");
        }
    }

    private static Dictionary<int, TileInfo> BuildTileInfo(TileTypesJson tileTypes)
    {
        var dict = new Dictionary<int, TileInfo>();
        foreach (var entry in tileTypes.Types)
        {
            if (string.IsNullOrWhiteSpace(entry.Type))
            {
                continue;
            }

            foreach (var id in entry.Ids)
            {
                dict[id] = new TileInfo(entry.Type, entry.Owner);
            }
        }

        return dict;
    }

    private static bool[] BuildBlocked(MapJson map, Dictionary<int, TileInfo> tileInfo)
    {
        var blocked = new bool[map.Data.Length];
        for (var i = 0; i < map.Data.Length; i++)
        {
            var tileId = map.Data[i];
            if (!tileInfo.TryGetValue(tileId, out var info))
            {
                blocked[i] = true;
                continue;
            }

            blocked[i] = IsBlockedType(info.Type);
        }

        return blocked;
    }

    private static bool IsBlockedType(string type) =>
        type switch
        {
            "water" => true,
            "mountain" => true,
            _ => false
        };

    private static IReadOnlyDictionary<string, GridPoint> FindCastleSpawns(MapJson map, Dictionary<int, TileInfo> tileInfo)
    {
        // Owners for the 8 factions; used to compute initial spawns.
        var owners = new[]
        {
            "white", "red", "green", "black", "orange", "lightblue", "darkblue", "yellow"
        };

        var ownerToIds = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, info) in tileInfo)
        {
            if (!string.Equals(info.Type, "castle", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(info.Owner))
            {
                continue;
            }

            if (!ownerToIds.TryGetValue(info.Owner, out var set))
            {
                set = new HashSet<int>();
                ownerToIds[info.Owner] = set;
            }
            set.Add(id);
        }

        var results = new Dictionary<string, GridPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var owner in owners)
        {
            if (!ownerToIds.TryGetValue(owner, out var ids) || ids.Count == 0)
            {
                continue;
            }

            var spawn = FindTopLeftOfFirstComponent(map, ids);
            results[owner] = spawn;
        }

        return results;
    }

    private static GridPoint FindTopLeftOfFirstComponent(MapJson map, HashSet<int> ids)
    {
        var visited = new bool[map.Data.Length];
        GridPoint? best = null;

        for (var index = 0; index < map.Data.Length; index++)
        {
            if (visited[index])
            {
                continue;
            }

            if (!ids.Contains(map.Data[index]))
            {
                continue;
            }

            var component = Flood(map, ids, index, visited);
            if (component.Count == 0)
            {
                continue;
            }

            var minX = int.MaxValue;
            var minY = int.MaxValue;
            foreach (var c in component)
            {
                var x = c % map.Width;
                var y = c / map.Width;
                if (y < minY || (y == minY && x < minX))
                {
                    minY = y;
                    minX = x;
                }
            }

            var candidate = new GridPoint(minX, minY);
            if (best is null || candidate.Y < best.Value.Y || (candidate.Y == best.Value.Y && candidate.X < best.Value.X))
            {
                best = candidate;
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException("CastleSpawnNotFound");
        }

        return best.Value;
    }

    private static List<int> Flood(MapJson map, HashSet<int> ids, int startIndex, bool[] visited)
    {
        var list = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(startIndex);
        visited[startIndex] = true;

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            if (!ids.Contains(map.Data[index]))
            {
                continue;
            }

            list.Add(index);
            var x = index % map.Width;
            var y = index / map.Width;

            void TryEnqueue(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height) return;
                var ni = ny * map.Width + nx;
                if (visited[ni]) return;
                visited[ni] = true;
                if (ids.Contains(map.Data[ni]))
                {
                    queue.Enqueue(ni);
                }
            }

            TryEnqueue(x - 1, y);
            TryEnqueue(x + 1, y);
            TryEnqueue(x, y - 1);
            TryEnqueue(x, y + 1);
        }

        return list;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly record struct TileInfo(string Type, string? Owner);
}

public sealed record LoadedMap(
    string MapId,
    int Width,
    int Height,
    int[] Tiles,
    bool[] Blocked,
    IReadOnlyDictionary<string, GridPoint> SpawnTopLeftByOwner,
    IReadOnlyDictionary<int, TileDefinition> TileDefinitions);

public sealed record TileDefinition(
    int TileId,
    string Type,
    string? Owner,
    bool IsBlocked);
