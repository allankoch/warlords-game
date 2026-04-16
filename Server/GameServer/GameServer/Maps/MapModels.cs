using System.Text.Json.Serialization;

namespace GameServer.Maps;

public sealed class MapJson
{
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("data")]
    public int[] Data { get; set; } = Array.Empty<int>();
}

public sealed class TileTypesJson
{
    [JsonPropertyName("types")]
    public List<TileTypeEntryJson> Types { get; set; } = new();
}

public sealed class TileTypeEntryJson
{
    [JsonPropertyName("ids")]
    public int[] Ids { get; set; } = Array.Empty<int>();

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }
}

