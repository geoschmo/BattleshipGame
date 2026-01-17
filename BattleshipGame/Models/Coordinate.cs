namespace BattleshipGame.Models;

public class Coordinate
{
    public int Row { get; set; }
    public int Col { get; set; }

    public Coordinate(int row, int col)
    {
        Row = row;
        Col = col;
    }

    public override bool Equals(object? obj)
    {
        if (obj is Coordinate other)
        {
            return Row == other.Row && Col == other.Col;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Row, Col);
    }

    public override string ToString()
    {
        return $"{(char)('A' + Col)}{Row + 1}";
    }
}
