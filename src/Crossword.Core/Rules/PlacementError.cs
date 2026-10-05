using System.Collections.Immutable;
using Crossword.Core.Domain;

namespace Crossword.Core.Rules;

/// <summary>Why a placement was rejected. <see cref="Message"/> is player-facing.</summary>
public abstract record PlacementError
{
    public abstract string Message { get; }

    public sealed record NoTiles : PlacementError
    {
        public override string Message => "Place at least one tile.";
    }

    public sealed record TileNotInHand(Tile Tile) : PlacementError
    {
        public override string Message => $"Tile {Tile} is not in your hand.";
    }

    public sealed record DuplicateTile(Tile Tile) : PlacementError
    {
        public override string Message => $"Tile {Tile} was placed more than once.";
    }

    public sealed record OutOfBounds(Position Position) : PlacementError
    {
        public override string Message => $"{Position} is off the board.";
    }

    public sealed record DuplicatePosition(Position Position) : PlacementError
    {
        public override string Message => $"Two tiles placed on {Position}.";
    }

    public sealed record Occupied(Position Position) : PlacementError
    {
        public override string Message => $"{Position} is already occupied.";
    }

    public sealed record NotInLine : PlacementError
    {
        public override string Message => "Tiles must be placed in a single row or column.";
    }

    public sealed record Gap(Position Position) : PlacementError
    {
        public override string Message => $"Placement has a gap at {Position}.";
    }

    public sealed record NotConnected : PlacementError
    {
        public override string Message => "Placement must connect to tiles already on the board.";
    }

    public sealed record NoWordFormed : PlacementError
    {
        public override string Message => "Placement must form a word of at least two letters.";
    }

    public sealed record InvalidWords(ImmutableArray<string> Words) : PlacementError
    {
        public override string Message => $"Not in dictionary: {string.Join(", ", Words)}.";
    }
}
