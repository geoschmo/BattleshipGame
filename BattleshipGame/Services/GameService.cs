using BattleshipGame.Models;

namespace BattleshipGame.Services;

public class GameService
{
    private readonly Dictionary<string, GameRoom> _rooms = new();
    private readonly Dictionary<string, string> _connectionToRoom = new();
    private readonly Dictionary<string, string> _tokenToRoom = new();
    private readonly Random _random = new();
    private readonly object _lock = new();
    private readonly GamePersistenceService _persistence;

    public GameService(GamePersistenceService persistence)
    {
        _persistence = persistence;
    }

    public string GenerateRoomCode()
    {
        lock (_lock)
        {
            string code;
            do
            {
                code = GenerateRandomCode(6);
            } while (_rooms.ContainsKey(code));
            return code;
        }
    }

    public string GeneratePlayerToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    private string GenerateRandomCode(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Excluding ambiguous characters
        return new string(Enumerable.Range(0, length)
            .Select(_ => chars[_random.Next(chars.Length)])
            .ToArray());
    }

    public GameRoom CreateRoom(string connectionId, string? playerToken, GameSize size = GameSize.Large)
    {
        lock (_lock)
        {
            var roomCode = GenerateRoomCode();
            var room = new GameRoom(roomCode, size);

            // Generate token if not provided
            var token = playerToken ?? GeneratePlayerToken();
            room.AddPlayer(connectionId, token);

            _rooms[roomCode] = room;
            _connectionToRoom[connectionId] = roomCode;
            _tokenToRoom[token] = roomCode;

            // Persist the game
            _persistence.SaveGame(room);

            return room;
        }
    }

    public GameRoom? JoinRoom(string roomCode, string connectionId, string? playerToken)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                return null;
            }

            if (room.IsFull())
            {
                return null;
            }

            // Generate token if not provided
            var token = playerToken ?? GeneratePlayerToken();
            room.AddPlayer(connectionId, token);
            _connectionToRoom[connectionId] = roomCode;
            _tokenToRoom[token] = roomCode;

            // Persist the game
            _persistence.SaveGame(room);

            return room;
        }
    }

    public GameRoom? GetRoomByCode(string roomCode)
    {
        lock (_lock)
        {
            _rooms.TryGetValue(roomCode, out var room);
            return room;
        }
    }

    public GameRoom? GetRoomByConnection(string connectionId)
    {
        lock (_lock)
        {
            if (_connectionToRoom.TryGetValue(connectionId, out var roomCode))
            {
                return GetRoomByCode(roomCode);
            }
            return null;
        }
    }

    public PersistedGame? GetActiveGameForToken(string token)
    {
        return _persistence.GetGameByToken(token);
    }

    public GameRoom? RestoreAndReconnect(string token, string connectionId)
    {
        lock (_lock)
        {
            // First check if we have the room in memory
            if (_tokenToRoom.TryGetValue(token, out var roomCode))
            {
                if (_rooms.TryGetValue(roomCode, out var existingRoom))
                {
                    if (existingRoom.ReconnectPlayer(connectionId, token))
                    {
                        _connectionToRoom[connectionId] = roomCode;
                        _persistence.SaveGame(existingRoom);
                        return existingRoom;
                    }
                }
            }

            // Try to restore from persistence
            var persisted = _persistence.GetGameByToken(token);
            if (persisted == null)
            {
                return null;
            }

            // Check if game can be reconnected
            if (persisted.Status != RoomStatus.WaitingForReconnect && persisted.Status != RoomStatus.Active)
            {
                return null;
            }

            // Restore the game room
            var room = _persistence.RestoreGameRoom(persisted);
            if (room == null)
            {
                return null;
            }

            // Reconnect the player
            if (!room.ReconnectPlayer(connectionId, token))
            {
                return null;
            }

            // Store in memory
            _rooms[room.RoomCode] = room;
            _connectionToRoom[connectionId] = room.RoomCode;

            if (room.Player1Token != null)
                _tokenToRoom[room.Player1Token] = room.RoomCode;
            if (room.Player2Token != null)
                _tokenToRoom[room.Player2Token] = room.RoomCode;

            // Save updated state
            _persistence.SaveGame(room);

            return room;
        }
    }

    public void SaveGame(GameRoom room)
    {
        _persistence.SaveGame(room);
    }

    public void HandlePlayerDisconnect(string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionToRoom.TryGetValue(connectionId, out var roomCode))
            {
                return;
            }

            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                _connectionToRoom.Remove(connectionId);
                return;
            }

            // Check if game is in progress (has started playing or ships are placed)
            var hasGameInProgress = room.Game != null &&
                (room.Game.State == GameState.Playing ||
                 room.Game.Player1ShipsPlaced ||
                 room.Game.Player2ShipsPlaced);

            if (hasGameInProgress)
            {
                // Mark for potential reconnection
                room.Status = RoomStatus.WaitingForReconnect;
                room.DisconnectedAt = DateTime.UtcNow;
                room.RemovePlayer(connectionId);

                _persistence.SaveGame(room);
                _connectionToRoom.Remove(connectionId);

                // Don't remove from _rooms - keep for reconnection
            }
            else
            {
                // No game in progress, clean up completely
                room.RemovePlayer(connectionId);
                _connectionToRoom.Remove(connectionId);

                if (room.IsEmpty())
                {
                    _rooms.Remove(roomCode);
                    _persistence.DeleteGame(roomCode);

                    // Clean up token mappings
                    if (room.Player1Token != null)
                        _tokenToRoom.Remove(room.Player1Token);
                    if (room.Player2Token != null)
                        _tokenToRoom.Remove(room.Player2Token);
                }
                else
                {
                    _persistence.SaveGame(room);
                }
            }
        }
    }

    public void MarkGameCompleted(string roomCode)
    {
        lock (_lock)
        {
            if (_rooms.TryGetValue(roomCode, out var room))
            {
                room.Status = RoomStatus.Completed;
                _persistence.SaveGame(room);
            }
        }
    }

    public void RemovePlayerFromRoom(string connectionId)
    {
        HandlePlayerDisconnect(connectionId);
    }

    public List<Ship> GenerateRandomShips(GameSize size = GameSize.Large)
    {
        var shipTypes = GameSizeConfig.GetShipTypes(size);
        var boardSize = GameSizeConfig.GetBoardSize(size);
        var ships = shipTypes.Select(t => new Ship(t)).ToList();

        var board = new Board(boardSize);

        foreach (var ship in ships)
        {
            bool placed = false;
            int attempts = 0;

            while (!placed && attempts < 100)
            {
                int row = _random.Next(boardSize);
                int col = _random.Next(boardSize);
                bool isHorizontal = _random.Next(2) == 0;

                placed = board.PlaceShip(ship, new Coordinate(row, col), isHorizontal);
                attempts++;
            }

            if (!placed)
            {
                // If we couldn't place a ship, start over
                return GenerateRandomShips(size);
            }
        }

        return ships;
    }

    public void CleanupOldRooms(TimeSpan maxAge)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var oldRooms = _rooms.Where(r => now - r.Value.CreatedAt > maxAge)
                                 .Select(r => r.Key)
                                 .ToList();

            foreach (var roomCode in oldRooms)
            {
                var room = _rooms[roomCode];
                if (room.Player1ConnectionId != null)
                {
                    _connectionToRoom.Remove(room.Player1ConnectionId);
                }
                if (room.Player2ConnectionId != null)
                {
                    _connectionToRoom.Remove(room.Player2ConnectionId);
                }
                if (room.Player1Token != null)
                {
                    _tokenToRoom.Remove(room.Player1Token);
                }
                if (room.Player2Token != null)
                {
                    _tokenToRoom.Remove(room.Player2Token);
                }
                _rooms.Remove(roomCode);
            }

            // Also cleanup persistence
            _persistence.CleanupOldGames(maxAge);
        }
    }
}
