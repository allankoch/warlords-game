namespace GameServer.Game.Engine;

public interface IMapProvider
{
    GameServer.Maps.LoadedMap Get(string mapId);
}

public readonly record struct GridPoint(int X, int Y);
