namespace GameServer.Maps;

public sealed record MapTileDto(
    int TileId,
    string Type,
    string? Owner,
    bool IsBlocked);

public sealed record SpawnPointDto(
    string Owner,
    int X,
    int Y);

public sealed record MapViewDto(
    string MapId,
    int Width,
    int Height,
    IReadOnlyList<int> Tiles,
    IReadOnlyList<MapTileDto> TilePalette,
    IReadOnlyList<SpawnPointDto> SpawnPoints);
