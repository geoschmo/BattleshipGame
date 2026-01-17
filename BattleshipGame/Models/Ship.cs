namespace BattleshipGame.Models;

public enum ShipType
{
    Carrier,
    Battleship,
    Cruiser,
    Submarine,
    Destroyer
}

public class Ship
{
    public ShipType Type { get; set; }
    public int Size { get; set; }
    public List<Coordinate> Coordinates { get; set; }
    public HashSet<Coordinate> Hits { get; set; }
    public bool IsHorizontal { get; set; }

    public Ship(ShipType type)
    {
        Type = type;
        Size = GetShipSize(type);
        Coordinates = new List<Coordinate>();
        Hits = new HashSet<Coordinate>();
        IsHorizontal = true;
    }

    public static int GetShipSize(ShipType type)
    {
        return type switch
        {
            ShipType.Carrier => 5,
            ShipType.Battleship => 4,
            ShipType.Cruiser => 3,
            ShipType.Submarine => 3,
            ShipType.Destroyer => 2,
            _ => 0
        };
    }

    public bool IsSunk()
    {
        return Hits.Count == Size;
    }

    public bool ContainsCoordinate(Coordinate coord)
    {
        return Coordinates.Any(c => c.Equals(coord));
    }
}
