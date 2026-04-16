using GameServer.Game;
using GameServer.Game.Engine;
using GameServer.Maps;
using GameServer.Networking;
using GameServer.Persistence;
using GameServer.Persistence.Sqlite;
using GameServer.Protocol;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IMapProvider, MapCatalog>();
builder.Services.AddSingleton<IGameEngine, GameEngine>();
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
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapHub<GameHub>("/hubs/game");

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast")
    .WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
