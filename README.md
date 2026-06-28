# Battleship Game

A standalone multiplayer Battleship game built with ASP.NET Core 8.0 and SignalR.

## Play Online

🎮 **Play Battleship:** [Launch the game](https://georgeperley.com/battleship)

## Features

- **Multiplayer Mode**: Real-time two-player games with room codes
- **Single Player Mode**: Play against AI with Hunt/Target algorithm
- **Game Recovery**: Reconnect to interrupted games within 5 minutes
- **Multiple Board Sizes**: Large (10x10), Medium (9x9), Small (8x8)
- **Responsive Design**: Works on desktop and mobile

## Local Development

```bash
# Run the application
dotnet run --project BattleshipGame/BattleshipGame.csproj

# Run with hot reload
dotnet watch --project BattleshipGame/BattleshipGame.csproj
```

The game will be available at `https://localhost:5001` or `http://localhost:5000`.

## Deployment

Publish the application with the .NET CLI:

```bash
dotnet publish BattleshipGame/BattleshipGame.csproj --configuration Release --output ./publish
```

The published output is written to `./publish`. Deploy that folder to an ASP.NET Core-compatible host.

An optional portfolio backlink can be configured in `appsettings.json`:

```json
{
  "PortfolioUrl": "https://example.com"
}
```

## Architecture Notes

### SignalR Hub

The game uses SignalR for real-time communication. The hub is mapped at `/gameHub`.

### Data Persistence

Game state is stored using LiteDB. The database file is created in one of these locations (in order of preference):
1. `App_Data/battleship.db` (in the application directory)
2. `%LOCALAPPDATA%/BattleshipGame/battleship.db`
3. Temp directory
4. In-memory (if all else fails)

### Single Player AI

The AI uses a Hunt/Target algorithm:
- **Hunt Mode**: Random attacks in a checkerboard pattern
- **Target Mode**: After a hit, attacks adjacent cells to find the rest of the ship

## Troubleshooting

### SignalR Connection Issues

If SignalR fails to connect:
1. Ensure WebSockets are enabled in your hosting panel
2. Check that the `/gameHub` endpoint is accessible
3. Verify CORS settings if accessing from a different domain

### Database Permission Errors

If you see database errors:
1. Ensure the `App_Data` folder exists and is writable
2. The app will fall back to in-memory mode if file persistence fails
3. Check the deployed application's file permissions
