using Microsoft.AspNetCore.SignalR;
using BattleshipGame.Models;
using BattleshipGame.Services;

namespace BattleshipGame.Hubs;

public class GameHub : Hub
{
    private readonly GameService _gameService;

    public GameHub(GameService gameService)
    {
        _gameService = gameService;
    }

    public async Task<object> CreateRoom(string size = "Large", string? playerToken = null)
    {
        var gameSize = size switch
        {
            "Medium" => GameSize.Medium,
            "Small" => GameSize.Small,
            _ => GameSize.Large
        };

        var room = _gameService.CreateRoom(Context.ConnectionId, playerToken, gameSize);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomCode);

        return new
        {
            success = true,
            roomCode = room.RoomCode,
            size = room.Size.ToString(),
            boardSize = GameSizeConfig.GetBoardSize(room.Size),
            shipTypes = GameSizeConfig.GetShipTypes(room.Size).Select(s => s.ToString()),
            playerToken = room.Player1Token,
            message = "Room created successfully"
        };
    }

    public async Task<object> JoinRoom(string roomCode, string? playerToken = null)
    {
        var room = _gameService.JoinRoom(roomCode.ToUpper(), Context.ConnectionId, playerToken);

        if (room == null)
        {
            return new
            {
                success = false,
                message = "Room not found or is full"
            };
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomCode);

        // Notify the other player
        var otherPlayer = room.GetOtherPlayer(Context.ConnectionId);
        if (otherPlayer != null)
        {
            await Clients.Client(otherPlayer).SendAsync("OpponentJoined");
        }

        return new
        {
            success = true,
            roomCode = room.RoomCode,
            size = room.Size.ToString(),
            boardSize = GameSizeConfig.GetBoardSize(room.Size),
            shipTypes = GameSizeConfig.GetShipTypes(room.Size).Select(s => s.ToString()),
            playerToken = room.Player2Token,
            message = "Joined room successfully"
        };
    }

    public async Task<object> CheckForActiveGame(string playerToken)
    {
        var persisted = _gameService.GetActiveGameForToken(playerToken);

        if (persisted == null)
        {
            return new { hasActiveGame = false };
        }

        return new
        {
            hasActiveGame = true,
            roomCode = persisted.Id,
            size = persisted.Size.ToString(),
            status = persisted.Status.ToString()
        };
    }

    public async Task<object> ReconnectToGame(string playerToken)
    {
        var room = _gameService.RestoreAndReconnect(playerToken, Context.ConnectionId);

        if (room == null)
        {
            return new
            {
                success = false,
                message = "Could not reconnect to game"
            };
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomCode);

        // Notify the other player if they're connected
        var otherPlayer = room.GetOtherPlayer(Context.ConnectionId);
        if (otherPlayer != null)
        {
            await Clients.Client(otherPlayer).SendAsync("OpponentReconnected");
        }

        // Prepare game state for the reconnecting player
        var isPlayer1 = room.Player1Token == playerToken;
        var game = room.Game;

        object? gameState = null;
        if (game != null)
        {
            var playerBoard = isPlayer1 ? game.Player1Board : game.Player2Board;
            var opponentBoard = isPlayer1 ? game.Player2Board : game.Player1Board;

            gameState = new
            {
                state = game.State.ToString(),
                isYourTurn = game.CurrentTurnPlayerId == Context.ConnectionId,
                yourShips = playerBoard.Ships.Select(s => new
                {
                    type = s.Type.ToString(),
                    size = s.Size,
                    coordinates = s.Coordinates.Select(c => new { row = c.Row, col = c.Col }),
                    hits = s.Hits.Select(c => new { row = c.Row, col = c.Col }),
                    isSunk = s.IsSunk(),
                    isHorizontal = s.IsHorizontal
                }),
                yourAttacks = opponentBoard.Attacks.Select(a => new
                {
                    row = a.Row,
                    col = a.Col,
                    hit = opponentBoard.Grid[a.Row, a.Col] == CellState.Hit
                }),
                opponentAttacks = playerBoard.Attacks.Select(a => new
                {
                    row = a.Row,
                    col = a.Col,
                    hit = playerBoard.Grid[a.Row, a.Col] == CellState.Hit
                }),
                shipsPlaced = isPlayer1 ? game.Player1ShipsPlaced : game.Player2ShipsPlaced,
                opponentShipsPlaced = isPlayer1 ? game.Player2ShipsPlaced : game.Player1ShipsPlaced,
                winnerId = game.WinnerId
            };
        }

        return new
        {
            success = true,
            roomCode = room.RoomCode,
            size = room.Size.ToString(),
            boardSize = GameSizeConfig.GetBoardSize(room.Size),
            shipTypes = GameSizeConfig.GetShipTypes(room.Size).Select(s => s.ToString()),
            gameState,
            opponentConnected = otherPlayer != null,
            message = "Reconnected successfully"
        };
    }

    public async Task<object> PlaceShips(List<ShipPlacementDto>? shipPlacements, bool useRandomPlacement)
    {
        try
        {
            var room = _gameService.GetRoomByConnection(Context.ConnectionId);

            if (room?.Game == null)
            {
                return new { success = false, message = "Game not found" };
            }

            var game = room.Game;
            var gameSize = game.Size;
            var expectedShipCount = GameSizeConfig.GetShipCount(gameSize);
            var boardSize = GameSizeConfig.GetBoardSize(gameSize);
            var playerBoard = game.GetPlayerBoard(Context.ConnectionId);

            List<Ship> ships;

            if (useRandomPlacement)
            {
                ships = _gameService.GenerateRandomShips(gameSize);
            }
            else
            {
                // Validate ship placements array
                if (shipPlacements == null || shipPlacements.Count == 0)
                {
                    return new { success = false, message = "No ship placements provided" };
                }

                if (shipPlacements.Count != expectedShipCount)
                {
                    return new { success = false, message = $"Expected {expectedShipCount} ships but received {shipPlacements.Count}" };
                }

                // Validate and place ships manually
                ships = new List<Ship>();
                var tempBoard = new Board(boardSize);

                foreach (var placement in shipPlacements)
                {
                    var ship = new Ship(placement.Type);
                    var startCoord = new Coordinate(placement.StartRow, placement.StartCol);

                    if (!tempBoard.PlaceShip(ship, startCoord, placement.IsHorizontal))
                    {
                        return new
                        {
                            success = false,
                            message = $"Invalid placement for {placement.Type}"
                        };
                    }

                    ships.Add(ship);
                }
            }

            // Place ships on the actual board
            foreach (var ship in ships)
            {
                var startCoord = ship.Coordinates[0];
                playerBoard.PlaceShip(ship, startCoord, ship.IsHorizontal);
            }

            game.SetShipsPlaced(Context.ConnectionId);

            // Save game state after ships placed
            _gameService.SaveGame(room);

            // Notify player
            await Clients.Caller.SendAsync("ShipsPlaced");

            // If both players have placed ships, start the game
            if (game.State == GameState.Playing)
            {
                await Clients.Group(room.RoomCode).SendAsync("GameStarted", game.CurrentTurnPlayerId);
            }

            return new { success = true, message = "Ships placed successfully" };
        }
        catch (Exception)
        {
            return new { success = false, message = "An error occurred while placing ships" };
        }
    }

    public async Task<object> Attack(int row, int col)
    {
        var room = _gameService.GetRoomByConnection(Context.ConnectionId);

        if (room?.Game == null)
        {
            return new { success = false, message = "Game not found" };
        }

        var game = room.Game;

        if (game.State != GameState.Playing)
        {
            return new { success = false, message = "Game is not in playing state" };
        }

        if (!game.IsPlayerTurn(Context.ConnectionId))
        {
            return new { success = false, message = "Not your turn" };
        }

        var coord = new Coordinate(row, col);
        var opponentBoard = game.GetOpponentBoard(Context.ConnectionId);

        // Check if already attacked
        if (opponentBoard.Attacks.Contains(coord))
        {
            return new { success = false, message = "Already attacked this position" };
        }

        var (hit, sunk, shipType) = opponentBoard.ProcessAttack(coord);

        // Save game state after attack
        _gameService.SaveGame(room);

        // Notify both players of the attack result
        await Clients.Caller.SendAsync("AttackResult", new
        {
            row,
            col,
            hit,
            sunk,
            shipType = shipType?.ToString(),
            isAttacker = true
        });

        var opponentId = game.GetOpponentId(Context.ConnectionId);
        await Clients.Client(opponentId).SendAsync("AttackResult", new
        {
            row,
            col,
            hit,
            sunk,
            shipType = shipType?.ToString(),
            isAttacker = false
        });

        // Check win condition
        game.CheckWinCondition();

        if (game.State == GameState.Finished)
        {
            _gameService.MarkGameCompleted(room.RoomCode);
            await Clients.Group(room.RoomCode).SendAsync("GameOver", game.WinnerId);
            return new { success = true, message = "Game over", gameOver = true };
        }

        // Switch turn
        game.SwitchTurn();
        _gameService.SaveGame(room);
        await Clients.Group(room.RoomCode).SendAsync("TurnChanged", game.CurrentTurnPlayerId);

        return new { success = true, message = "Attack processed", hit, sunk };
    }

    public async Task RequestRandomShips()
    {
        var room = _gameService.GetRoomByConnection(Context.ConnectionId);
        var gameSize = room?.Size ?? GameSize.Large;

        var ships = _gameService.GenerateRandomShips(gameSize);

        var shipData = ships.Select(s => new
        {
            type = s.Type.ToString(),
            size = s.Size,
            startRow = s.Coordinates[0].Row,
            startCol = s.Coordinates[0].Col,
            isHorizontal = s.IsHorizontal
        });

        await Clients.Caller.SendAsync("RandomShipsGenerated", shipData);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var room = _gameService.GetRoomByConnection(Context.ConnectionId);

        if (room != null)
        {
            var otherPlayer = room.GetOtherPlayer(Context.ConnectionId);

            // Get player token before disconnecting
            var token = room.GetTokenForConnection(Context.ConnectionId);

            _gameService.RemovePlayerFromRoom(Context.ConnectionId);

            // Check if game is waiting for reconnect
            var updatedRoom = _gameService.GetRoomByCode(room.RoomCode);
            var isWaitingForReconnect = updatedRoom?.Status == RoomStatus.WaitingForReconnect;

            if (otherPlayer != null)
            {
                if (isWaitingForReconnect)
                {
                    await Clients.Client(otherPlayer).SendAsync("OpponentDisconnected", new { canReconnect = true });
                }
                else
                {
                    await Clients.Client(otherPlayer).SendAsync("OpponentDisconnected", new { canReconnect = false });
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}

public class ShipPlacementDto
{
    public ShipType Type { get; set; }
    public int StartRow { get; set; }
    public int StartCol { get; set; }
    public bool IsHorizontal { get; set; }
}
