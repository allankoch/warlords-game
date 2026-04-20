using GameServer.Game;
using GameServer.Game.Engine;
using GameServer.Maps;
using GameServer.Networking;
using GameServer.Persistence;
using GameServer.Persistence.Sqlite;
using GameServer.Protocol;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
                     ["http://localhost:3000", "http://localhost:4173", "http://localhost:5173"];

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Client", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<IMapProvider, MapCatalog>();
builder.Services.AddSingleton<IGameEngine, GameEngine>();
builder.Services.AddSingleton<ILobbyChatService, LobbyChatService>();
builder.Services.AddSingleton<GameService>();
builder.Services.AddSingleton<IGameSessionService>(sp => sp.GetRequiredService<GameService>());
builder.Services.AddHostedService<MatchTimeoutService>();

var dbPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "gameserver.db");
builder.Services.AddSingleton(new SqliteStorageOptions(dbPath));
builder.Services.AddSingleton<SqliteGameRepository>();
builder.Services.AddSingleton<IIdentityStore>(sp => sp.GetRequiredService<SqliteGameRepository>());
builder.Services.AddSingleton<IMatchStore>(sp => sp.GetRequiredService<SqliteGameRepository>());
builder.Services.AddSingleton<IMatchActionLog>(sp => sp.GetRequiredService<SqliteGameRepository>());
builder.Services.AddSingleton<IGamePersistence>(sp => sp.GetRequiredService<SqliteGameRepository>());
builder.Services.AddHostedService<SqliteDatabaseInitializer>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("Client");

app.MapHub<GameHub>("/hubs/game");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/maps/{mapId}", (string mapId, IMapProvider maps) =>
{
    var map = maps.Get(mapId);
    var palette = map.TileDefinitions.Values
        .OrderBy(tile => tile.TileId)
        .Select(tile => new MapTileDto(tile.TileId, tile.Type, tile.Owner, tile.IsBlocked))
        .ToArray();
    var spawns = map.SpawnTopLeftByOwner
        .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
        .Select(kvp => new SpawnPointDto(kvp.Key, kvp.Value.X, kvp.Value.Y))
        .ToArray();

    return Results.Ok(new MapViewDto(map.MapId, map.Width, map.Height, map.Tiles, palette, spawns));
});

app.Run();
