using System.Text.Json;

namespace BattleshipGame.Models;

public enum RoomStatus
{
    WaitingForPlayer,
    Active,
    WaitingForReconnect,
    Abandoned,
    Completed
}

public class PersistedGame
{
    public string Id { get; set; } = string.Empty;  // Room code
    public string? Player1Token { get; set; }
    public string? Player2Token { get; set; }
    public string? Player1ConnectionId { get; set; }
    public string? Player2ConnectionId { get; set; }
    public GameSize Size { get; set; }
    public RoomStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }

    // Serialized game state
    public string? GameStateJson { get; set; }

    public PersistedGame() { }

    public PersistedGame(GameRoom room)
    {
        Id = room.RoomCode;
        Player1Token = room.Player1Token;
        Player2Token = room.Player2Token;
        Player1ConnectionId = room.Player1ConnectionId;
        Player2ConnectionId = room.Player2ConnectionId;
        Size = room.Size;
        Status = room.Status;
        CreatedAt = room.CreatedAt;
        LastActivityAt = DateTime.UtcNow;

        if (room.Game != null)
        {
            GameStateJson = SerializeGameState(room.Game);
        }
    }

    public void UpdateFromRoom(GameRoom room)
    {
        Player1Token = room.Player1Token;
        Player2Token = room.Player2Token;
        Player1ConnectionId = room.Player1ConnectionId;
        Player2ConnectionId = room.Player2ConnectionId;
        Status = room.Status;
        LastActivityAt = DateTime.UtcNow;
        DisconnectedAt = room.DisconnectedAt;

        if (room.Game != null)
        {
            GameStateJson = SerializeGameState(room.Game);
        }
    }

    private static string SerializeGameState(Game game)
    {
        var state = new GameStateDto
        {
            GameId = game.GameId,
            Player1Id = game.Player1Id,
            Player2Id = game.Player2Id,
            CurrentTurnPlayerId = game.CurrentTurnPlayerId,
            State = game.State,
            WinnerId = game.WinnerId,
            Player1ShipsPlaced = game.Player1ShipsPlaced,
            Player2ShipsPlaced = game.Player2ShipsPlaced,
            Size = game.Size,
            Player1Board = SerializeBoard(game.Player1Board),
            Player2Board = SerializeBoard(game.Player2Board)
        };

        return JsonSerializer.Serialize(state);
    }

    private static BoardDto SerializeBoard(Board board)
    {
        return new BoardDto
        {
            BoardSize = board.BoardSize,
            Ships = board.Ships.Select(s => new ShipDto
            {
                Type = s.Type,
                Size = s.Size,
                IsHorizontal = s.IsHorizontal,
                Coordinates = s.Coordinates.Select(c => new CoordinateDto { Row = c.Row, Col = c.Col }).ToList(),
                Hits = s.Hits.Select(c => new CoordinateDto { Row = c.Row, Col = c.Col }).ToList()
            }).ToList(),
            Attacks = board.Attacks.Select(c => new CoordinateDto { Row = c.Row, Col = c.Col }).ToList()
        };
    }

    public Game? DeserializeGameState()
    {
        if (string.IsNullOrEmpty(GameStateJson)) return null;

        try
        {
            var state = JsonSerializer.Deserialize<GameStateDto>(GameStateJson);
            if (state == null) return null;

            var game = new Game(state.GameId, state.Player1Id, state.Player2Id, state.Size)
            {
                CurrentTurnPlayerId = state.CurrentTurnPlayerId,
                State = state.State,
                WinnerId = state.WinnerId,
                Player1ShipsPlaced = state.Player1ShipsPlaced,
                Player2ShipsPlaced = state.Player2ShipsPlaced
            };

            // Restore boards
            game.Player1Board = DeserializeBoard(state.Player1Board);
            game.Player2Board = DeserializeBoard(state.Player2Board);

            return game;
        }
        catch
        {
            return null;
        }
    }

    private static Board DeserializeBoard(BoardDto dto)
    {
        var board = new Board(dto.BoardSize);

        foreach (var shipDto in dto.Ships)
        {
            var ship = new Ship(shipDto.Type)
            {
                IsHorizontal = shipDto.IsHorizontal
            };

            foreach (var coord in shipDto.Coordinates)
            {
                var c = new Coordinate(coord.Row, coord.Col);
                ship.Coordinates.Add(c);
                board.Grid[coord.Row, coord.Col] = CellState.Ship;
            }

            foreach (var hit in shipDto.Hits)
            {
                ship.Hits.Add(new Coordinate(hit.Row, hit.Col));
                board.Grid[hit.Row, hit.Col] = CellState.Hit;
            }

            board.Ships.Add(ship);
        }

        foreach (var attack in dto.Attacks)
        {
            var coord = new Coordinate(attack.Row, attack.Col);
            board.Attacks.Add(coord);

            // Mark misses on the grid
            if (board.Grid[attack.Row, attack.Col] == CellState.Empty)
            {
                board.Grid[attack.Row, attack.Col] = CellState.Miss;
            }
        }

        return board;
    }
}

// DTOs for JSON serialization
public class GameStateDto
{
    public string GameId { get; set; } = string.Empty;
    public string Player1Id { get; set; } = string.Empty;
    public string Player2Id { get; set; } = string.Empty;
    public string CurrentTurnPlayerId { get; set; } = string.Empty;
    public GameState State { get; set; }
    public string? WinnerId { get; set; }
    public bool Player1ShipsPlaced { get; set; }
    public bool Player2ShipsPlaced { get; set; }
    public GameSize Size { get; set; }
    public BoardDto Player1Board { get; set; } = new();
    public BoardDto Player2Board { get; set; } = new();
}

public class BoardDto
{
    public int BoardSize { get; set; }
    public List<ShipDto> Ships { get; set; } = new();
    public List<CoordinateDto> Attacks { get; set; } = new();
}

public class ShipDto
{
    public ShipType Type { get; set; }
    public int Size { get; set; }
    public bool IsHorizontal { get; set; }
    public List<CoordinateDto> Coordinates { get; set; } = new();
    public List<CoordinateDto> Hits { get; set; } = new();
}

public class CoordinateDto
{
    public int Row { get; set; }
    public int Col { get; set; }
}
