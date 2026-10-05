namespace Crossword.Core.Domain;

public enum Direction
{
    Across,
    Down,
}

public static class DirectionExtensions
{
    public static Direction Perpendicular(this Direction direction) =>
        direction == Direction.Across ? Direction.Down : Direction.Across;
}

/// <summary>Zero-based grid coordinate. Row grows downward, Col grows rightward.</summary>
public readonly record struct Position(int Row, int Col)
{
    public Position Step(Direction direction, int amount = 1) =>
        direction == Direction.Across ? this with { Col = Col + amount } : this with { Row = Row + amount };

    public IEnumerable<Position> Neighbours()
    {
        yield return this with { Row = Row - 1 };
        yield return this with { Row = Row + 1 };
        yield return this with { Col = Col - 1 };
        yield return this with { Col = Col + 1 };
    }

    /// <summary>Spreadsheet-style label, e.g. (0,0) → "A1".</summary>
    public override string ToString() => $"{(char)('A' + Col)}{Row + 1}";
}
