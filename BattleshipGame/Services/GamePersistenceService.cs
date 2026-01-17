using BattleshipGame.Models;
using LiteDB;

namespace BattleshipGame.Services;

public class GamePersistenceService : IDisposable
{
    private readonly LiteDatabase? _database;
    private readonly ILiteCollection<PersistedGame>? _games;
    private readonly object _lock = new();

    public GamePersistenceService(string? databasePath = null)
    {
        try
        {
            // Use in-memory mode if no path provided or path is null
            var connectionString = string.IsNullOrEmpty(databasePath)
                ? ":memory:"
                : databasePath;

            _database = new LiteDatabase(connectionString);
            _games = _database.GetCollection<PersistedGame>("games");

            // Create index on tokens for fast lookup
            _games.EnsureIndex(x => x.Player1Token);
            _games.EnsureIndex(x => x.Player2Token);
            _games.EnsureIndex(x => x.Status);
        }
        catch (Exception)
        {
            // If database creation fails, operate without persistence
            _database = null;
            _games = null;
        }
    }

    public void SaveGame(GameRoom room)
    {
        if (_games == null) return;

        try
        {
            lock (_lock)
            {
                var existing = _games.FindById(room.RoomCode);
                if (existing != null)
                {
                    existing.UpdateFromRoom(room);
                    _games.Update(existing);
                }
                else
                {
                    var persisted = new PersistedGame(room);
                    _games.Insert(persisted);
                }
            }
        }
        catch
        {
            // Silently fail - persistence is optional
        }
    }

    public PersistedGame? GetGame(string roomCode)
    {
        if (_games == null) return null;

        try
        {
            lock (_lock)
            {
                return _games.FindById(roomCode);
            }
        }
        catch
        {
            return null;
        }
    }

    public PersistedGame? GetGameByToken(string token)
    {
        if (_games == null) return null;

        try
        {
            lock (_lock)
            {
                return _games.FindOne(g =>
                    (g.Player1Token == token || g.Player2Token == token) &&
                    (g.Status == RoomStatus.Active || g.Status == RoomStatus.WaitingForReconnect));
            }
        }
        catch
        {
            return null;
        }
    }

    public List<PersistedGame> GetActiveGamesForToken(string token)
    {
        if (_games == null) return new List<PersistedGame>();

        try
        {
            lock (_lock)
            {
                return _games.Find(g =>
                    (g.Player1Token == token || g.Player2Token == token) &&
                    (g.Status == RoomStatus.Active || g.Status == RoomStatus.WaitingForReconnect))
                    .ToList();
            }
        }
        catch
        {
            return new List<PersistedGame>();
        }
    }

    public void UpdateGameStatus(string roomCode, RoomStatus status, DateTime? disconnectedAt = null)
    {
        if (_games == null) return;

        try
        {
            lock (_lock)
            {
                var game = _games.FindById(roomCode);
                if (game != null)
                {
                    game.Status = status;
                    game.DisconnectedAt = disconnectedAt;
                    game.LastActivityAt = DateTime.UtcNow;
                    _games.Update(game);
                }
            }
        }
        catch
        {
            // Silently fail
        }
    }

    public void DeleteGame(string roomCode)
    {
        if (_games == null) return;

        try
        {
            lock (_lock)
            {
                _games.Delete(roomCode);
            }
        }
        catch
        {
            // Silently fail
        }
    }

    public void CleanupOldGames(TimeSpan maxAge)
    {
        if (_games == null) return;

        try
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow - maxAge;

                // Delete abandoned games older than maxAge
                _games.DeleteMany(g =>
                    g.Status == RoomStatus.Abandoned && g.LastActivityAt < cutoff);

                // Delete completed games older than maxAge
                _games.DeleteMany(g =>
                    g.Status == RoomStatus.Completed && g.LastActivityAt < cutoff);

                // Mark games waiting for reconnect as abandoned if too old
                var staleGames = _games.Find(g =>
                    g.Status == RoomStatus.WaitingForReconnect &&
                    g.DisconnectedAt.HasValue &&
                    g.DisconnectedAt.Value < cutoff).ToList();

                foreach (var game in staleGames)
                {
                    game.Status = RoomStatus.Abandoned;
                    game.LastActivityAt = DateTime.UtcNow;
                    _games.Update(game);
                }
            }
        }
        catch
        {
            // Silently fail
        }
    }

    public GameRoom? RestoreGameRoom(PersistedGame persisted)
    {
        var room = new GameRoom(persisted.Id, persisted.Size)
        {
            Player1Token = persisted.Player1Token,
            Player2Token = persisted.Player2Token,
            Player1ConnectionId = persisted.Player1ConnectionId,
            Player2ConnectionId = persisted.Player2ConnectionId,
            Status = persisted.Status,
            CreatedAt = persisted.CreatedAt,
            DisconnectedAt = persisted.DisconnectedAt
        };

        room.Game = persisted.DeserializeGameState();

        return room;
    }

    public void Dispose()
    {
        _database?.Dispose();
    }
}
