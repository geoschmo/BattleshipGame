namespace BattleshipGame.Models;

public class GameRoom
{
    public string RoomCode { get; set; }
    public string? Player1ConnectionId { get; set; }
    public string? Player2ConnectionId { get; set; }
    public string? Player1Token { get; set; }
    public string? Player2Token { get; set; }
    public Game? Game { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public GameSize Size { get; set; }
    public RoomStatus Status { get; set; }

    public GameRoom(string roomCode, GameSize size = GameSize.Large)
    {
        RoomCode = roomCode;
        Size = size;
        Status = RoomStatus.WaitingForPlayer;
        CreatedAt = DateTime.UtcNow;
    }

    public bool IsFull()
    {
        return Player1ConnectionId != null && Player2ConnectionId != null;
    }

    public bool IsEmpty()
    {
        return Player1ConnectionId == null && Player2ConnectionId == null;
    }

    public bool AddPlayer(string connectionId, string? token = null)
    {
        if (Player1ConnectionId == null)
        {
            Player1ConnectionId = connectionId;
            Player1Token = token;
            return true;
        }
        else if (Player2ConnectionId == null)
        {
            Player2ConnectionId = connectionId;
            Player2Token = token;
            Status = RoomStatus.Active;
            // Create game when both players are connected
            Game = new Game(RoomCode, Player1ConnectionId, Player2ConnectionId, Size);
            return true;
        }
        return false;
    }

    public void RemovePlayer(string connectionId)
    {
        if (Player1ConnectionId == connectionId)
        {
            Player1ConnectionId = null;
        }
        else if (Player2ConnectionId == connectionId)
        {
            Player2ConnectionId = null;
        }
    }

    public bool CanReconnect(string token)
    {
        return (Player1Token == token || Player2Token == token) &&
               Status == RoomStatus.WaitingForReconnect &&
               DisconnectedAt.HasValue &&
               DateTime.UtcNow - DisconnectedAt.Value < TimeSpan.FromMinutes(5);
    }

    public bool ReconnectPlayer(string connectionId, string token)
    {
        if (Player1Token == token)
        {
            Player1ConnectionId = connectionId;
            if (Game != null) Game.Player1Id = connectionId;
            CheckReconnectionComplete();
            return true;
        }
        else if (Player2Token == token)
        {
            Player2ConnectionId = connectionId;
            if (Game != null) Game.Player2Id = connectionId;
            CheckReconnectionComplete();
            return true;
        }
        return false;
    }

    private void CheckReconnectionComplete()
    {
        if (Player1ConnectionId != null && Player2ConnectionId != null)
        {
            Status = RoomStatus.Active;
            DisconnectedAt = null;
        }
    }

    public string? GetTokenForConnection(string connectionId)
    {
        if (Player1ConnectionId == connectionId) return Player1Token;
        if (Player2ConnectionId == connectionId) return Player2Token;
        return null;
    }

    public bool IsPlayerByToken(string token)
    {
        return Player1Token == token || Player2Token == token;
    }

    public bool HasPlayer(string connectionId)
    {
        return Player1ConnectionId == connectionId || Player2ConnectionId == connectionId;
    }

    public string? GetOtherPlayer(string connectionId)
    {
        if (Player1ConnectionId == connectionId)
        {
            return Player2ConnectionId;
        }
        else if (Player2ConnectionId == connectionId)
        {
            return Player1ConnectionId;
        }
        return null;
    }
}
