# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Restore dependencies
dotnet restore BattleshipGame.sln

# Build solution
dotnet build BattleshipGame.sln

# Build for production
dotnet build BattleshipGame.sln --configuration Release

# Run web application
dotnet run --project BattleshipGame/BattleshipGame.csproj

# Run in watch mode (hot reload)
dotnet watch --project BattleshipGame/BattleshipGame.csproj

# Publish for deployment
dotnet publish BattleshipGame/BattleshipGame.csproj --configuration Release --output ./publish
```

## Architecture Overview

This is a standalone ASP.NET Core 8.0 Razor Pages web application for the Battleship multiplayer game.

### Tech Stack
- ASP.NET Core 8.0 with Razor Pages
- SignalR for real-time multiplayer communication
- LiteDB for game state persistence
- Pure JavaScript frontend (no framework)

### Project Structure
- `BattleshipGame/` - Main web application project
  - `Hubs/GameHub.cs` - SignalR hub for real-time game communication
  - `Models/` - Game domain models (Game, Board, Ship, Coordinate, etc.)
  - `Services/` - Game logic services (GameService, GamePersistenceService)
  - `Pages/` - Razor Pages (Index.cshtml is the main game page)
  - `wwwroot/` - Static files
    - `css/styles.css` - Game styling
    - `js/game.js` - Client-side game logic

### Key Features
1. **Multiplayer Mode** - Real-time two-player games via SignalR
2. **Single Player Mode** - Play against AI (Hunt/Target algorithm)
3. **Game Recovery** - Reconnect to interrupted games within 5 minutes
4. **Multiple Board Sizes** - Large (10x10), Medium (9x9), Small (8x8)

### Configuration
- `appsettings.json` - Main configuration
- `PortfolioUrl` - URL to link back to the portfolio site (optional)

### SignalR Hub Methods
- `CreateRoom(size, playerToken)` - Create a new game room
- `JoinRoom(roomCode, playerToken)` - Join an existing room
- `PlaceShips(placements, useRandom)` - Submit ship placements
- `Attack(row, col)` - Fire at a coordinate
- `CheckForActiveGame(token)` - Check for reconnectable games
- `ReconnectToGame(token)` - Reconnect to an active game

## Deployment

See README.md for SmarterASP.NET deployment instructions.
