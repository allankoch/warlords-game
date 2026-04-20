using GameServer.Game.Engine;
using GameServer.Maps;

namespace GameServer.Tests;

public sealed class TestMapProvider : IMapProvider
{
    private readonly LoadedMap _map;

    public TestMapProvider(int width = 10, int height = 10)
    {
        var tiles = new int[width * height];
        var blocked = new bool[tiles.Length];
        var spawns = new Dictionary<string, GridPoint>(StringComparer.OrdinalIgnoreCase)
        {
            ["white"] = new GridPoint(0, 0),
            ["red"] = new GridPoint(1, 0),
            ["green"] = new GridPoint(2, 0),
            ["black"] = new GridPoint(3, 0),
            ["orange"] = new GridPoint(4, 0),
            ["lightblue"] = new GridPoint(5, 0),
            ["darkblue"] = new GridPoint(6, 0),
            ["yellow"] = new GridPoint(7, 0)
        };

        var definitions = new Dictionary<int, TileDefinition>
        {
            [0] = new(0, "grassland", null, false)
        };

        _map = new LoadedMap("test-map", width, height, tiles, blocked, spawns, definitions);
    }

    public LoadedMap Get(string mapId) => _map;
}
