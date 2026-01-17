namespace BattleshipGame.Models;

public enum GameState
{
    WaitingForPlayers,
    PlacingShips,
    Playing,
    Finished
}

public enum GameSize
{
    Large,  // 10x10, 5 ships
    Medium, // 9x9, 4 ships (no Carrier)
    Small   // 8x8, 3 ships (no Carrier, no Submarine)
}

public static class GameSizeConfig
{
    public static int GetBoardSize(GameSize size) => size switch
    {
        GameSize.Large => 10,
        GameSize.Medium => 9,
        GameSize.Small => 8,
        _ => 10
    };

    public static ShipType[] GetShipTypes(GameSize size) => size switch
    {
        GameSize.Large => new[] { ShipType.Carrier, ShipType.Battleship, ShipType.Cruiser, ShipType.Submarine, ShipType.Destroyer },
        GameSize.Medium => new[] { ShipType.Battleship, ShipType.Cruiser, ShipType.Submarine, ShipType.Destroyer },
        GameSize.Small => new[] { ShipType.Battleship, ShipType.Cruiser, ShipType.Destroyer },
        _ => new[] { ShipType.Carrier, ShipType.Battleship, ShipType.Cruiser, ShipType.Submarine, ShipType.Destroyer }
    };

    public static int GetShipCount(GameSize size) => size switch
    {
        GameSize.Large => 5,
        GameSize.Medium => 4,
        GameSize.Small => 3,
        _ => 5
    };
}

public class Game
{
    public string GameId { get; set; }
    public string Player1Id { get; set; }
    public string Player2Id { get; set; }
    public Board Player1Board { get; set; }
    public Board Player2Board { get; set; }
    public string CurrentTurnPlayerId { get; set; }
    public GameState State { get; set; }
    public string? WinnerId { get; set; }
    public bool Player1ShipsPlaced { get; set; }
    public bool Player2ShipsPlaced { get; set; }
    public GameSize Size { get; set; }

    public Game(string gameId, string player1Id, string player2Id, GameSize size = GameSize.Large)
    {
        GameId = gameId;
        Player1Id = player1Id;
        Player2Id = player2Id;
        Size = size;
        var boardSize = GameSizeConfig.GetBoardSize(size);
        Player1Board = new Board(boardSize);
        Player2Board = new Board(boardSize);
        CurrentTurnPlayerId = player1Id;
        State = GameState.PlacingShips;
        Player1ShipsPlaced = false;
        Player2ShipsPlaced = false;
    }

    public Board GetPlayerBoard(string playerId)
    {
        return playerId == Player1Id ? Player1Board : Player2Board;
    }

    public Board GetOpponentBoard(string playerId)
    {
        return playerId == Player1Id ? Player2Board : Player1Board;
    }

    public string GetOpponentId(string playerId)
    {
        return playerId == Player1Id ? Player2Id : Player1Id;
    }

    public void SetShipsPlaced(string playerId)
    {
        if (playerId == Player1Id)
        {
            Player1ShipsPlaced = true;
        }
        else
        {
            Player2ShipsPlaced = true;
        }

        // Start game if both players have placed ships
        if (Player1ShipsPlaced && Player2ShipsPlaced)
        {
            State = GameState.Playing;
        }
    }

    public void SwitchTurn()
    {
        CurrentTurnPlayerId = GetOpponentId(CurrentTurnPlayerId);
    }

    public bool IsPlayerTurn(string playerId)
    {
        return CurrentTurnPlayerId == playerId;
    }

    public void CheckWinCondition()
    {
        if (Player1Board.AllShipsSunk())
        {
            WinnerId = Player2Id;
            State = GameState.Finished;
        }
        else if (Player2Board.AllShipsSunk())
        {
            WinnerId = Player1Id;
            State = GameState.Finished;
        }
    }
}
