namespace BattleshipGame.Models;

public enum CellState
{
    Empty,
    Ship,
    Miss,
    Hit
}

public class Board
{
    public const int DefaultBoardSize = 10;
    public int BoardSize { get; set; }
    public CellState[,] Grid { get; set; }
    public List<Ship> Ships { get; set; }
    public HashSet<Coordinate> Attacks { get; set; }

    public Board(int boardSize = DefaultBoardSize)
    {
        BoardSize = boardSize;
        Grid = new CellState[BoardSize, BoardSize];
        Ships = new List<Ship>();
        Attacks = new HashSet<Coordinate>();
    }

    public bool PlaceShip(Ship ship, Coordinate start, bool isHorizontal)
    {
        ship.IsHorizontal = isHorizontal;
        ship.Coordinates.Clear();

        // Generate coordinates for the ship
        for (int i = 0; i < ship.Size; i++)
        {
            int row = isHorizontal ? start.Row : start.Row + i;
            int col = isHorizontal ? start.Col + i : start.Col;

            // Check if out of bounds
            if (row < 0 || row >= BoardSize || col < 0 || col >= BoardSize)
            {
                return false;
            }

            var coord = new Coordinate(row, col);
            ship.Coordinates.Add(coord);
        }

        // Check for overlaps with existing ships
        foreach (var coord in ship.Coordinates)
        {
            if (Grid[coord.Row, coord.Col] == CellState.Ship)
            {
                ship.Coordinates.Clear();
                return false;
            }
        }

        // Place the ship
        foreach (var coord in ship.Coordinates)
        {
            Grid[coord.Row, coord.Col] = CellState.Ship;
        }

        Ships.Add(ship);
        return true;
    }

    public (bool hit, bool sunk, ShipType? shipType) ProcessAttack(Coordinate coord)
    {
        // Check if already attacked
        if (Attacks.Contains(coord))
        {
            return (false, false, null);
        }

        Attacks.Add(coord);

        // Check if hit a ship
        var hitShip = Ships.FirstOrDefault(s => s.ContainsCoordinate(coord));
        if (hitShip != null)
        {
            hitShip.Hits.Add(coord);
            Grid[coord.Row, coord.Col] = CellState.Hit;
            bool sunk = hitShip.IsSunk();
            return (true, sunk, hitShip.Type);
        }

        Grid[coord.Row, coord.Col] = CellState.Miss;
        return (false, false, null);
    }

    public bool AllShipsSunk()
    {
        return Ships.All(s => s.IsSunk());
    }

    public bool HasShipAt(Coordinate coord)
    {
        return Grid[coord.Row, coord.Col] == CellState.Ship ||
               Grid[coord.Row, coord.Col] == CellState.Hit;
    }
}
