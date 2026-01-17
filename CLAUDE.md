# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
dotnet restore BattleshipGame.sln        # Restore dependencies
dotnet build BattleshipGame.sln          # Build solution
dotnet run --project BattleshipGame/BattleshipGame.csproj   # Run application
dotnet watch --project BattleshipGame/BattleshipGame.csproj # Hot reload mode
dotnet publish BattleshipGame/BattleshipGame.csproj --configuration Release --output ./publish
```

App runs at `https://localhost:5001` or `http://localhost:5000`.

## Architecture Overview

ASP.NET Core 8.0 Razor Pages application for multiplayer Battleship with SignalR real-time communication.

### Core Components

**Backend (C#)**
- `Hubs/GameHub.cs` - SignalR hub handling all game events (room creation/joining, ship placement, attacks, reconnection)
- `Services/GameService.cs` - In-memory game state management with thread-safe room/connection tracking
- `Services/GamePersistenceService.cs` - LiteDB persistence for game recovery
- `Models/` - Domain models: `Game`, `GameRoom`, `Board`, `Ship`, `Coordinate`, `PersistedGame`

**Frontend (JavaScript)**
- `wwwroot/js/game.js` - Single-file client with SignalR handlers, board rendering, and AI logic
- `wwwroot/css/styles.css` - CSS with CSS variables for theming

### Game Flow
1. Player creates/joins room via SignalR → `GameRoom` created with 6-char code
2. Second player joins → `Game` object created, both enter ship placement phase
3. Both place ships → `GameState.Playing`, turn-based attacks begin
4. All ships sunk → `GameState.Finished`, winner determined

### State Management
- `GameService` uses three dictionaries: `_rooms` (by code), `_connectionToRoom`, `_tokenToRoom`
- Player tokens (GUIDs) stored in localStorage enable reconnection within 5 minutes
- `RoomStatus`: `WaitingForPlayer` → `Active` → `WaitingForReconnect` → `Completed`

### Game Sizes
- Large: 10x10, 5 ships (Carrier, Battleship, Cruiser, Submarine, Destroyer)
- Medium: 9x9, 4 ships (no Carrier)
- Small: 8x8, 3 ships (no Carrier, Submarine)

### Single Player AI
Client-side Hunt/Target algorithm in `BattleshipAI` object:
- Hunt mode: Random attacks in checkerboard pattern
- Target mode: After hit, attacks adjacent cells to sink ship

### SignalR Events
**Client → Server:** `CreateRoom`, `JoinRoom`, `PlaceShips`, `Attack`, `CheckForActiveGame`, `ReconnectToGame`, `RequestRandomShips`

**Server → Client:** `OpponentJoined`, `ShipsPlaced`, `GameStarted`, `AttackResult`, `TurnChanged`, `GameOver`, `OpponentDisconnected`, `OpponentReconnected`, `RandomShipsGenerated`

## Deployment

See README.md for SmarterASP.NET deployment instructions. LiteDB database auto-creates in `App_Data/battleship.db`.
