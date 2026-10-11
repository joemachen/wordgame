using System.Collections.Immutable;
using Crossword.Core.Domain;

namespace Crossword.Core.Stationery;

/// <summary>What a Stationery item needs the player to point at when it is used.</summary>
public enum StationeryTarget
{
    /// <summary>Takes effect immediately.</summary>
    None,

    /// <summary>Acts on tiles chosen from the hand.</summary>
    HandTiles,

    /// <summary>Acts on one occupied board cell.</summary>
    BoardTile,

    /// <summary>Acts on one empty, unblocked board cell.</summary>
    EmptyCell,

    /// <summary>Acts on a word on the board, pointed at by any of its cells.</summary>
    BoardWord,
}

/// <summary>
/// One-shot items held in <see cref="Domain.RunState.Stationery"/> slots and used during a round (Balatro's Tarot
/// cards). Their effects live in <see cref="Run.RunRules.UseStationery"/>.
/// </summary>
public interface IStationery
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    StationeryTarget Target { get; }
}

/// <summary>Reveals the best play for the current hand, scored with your Desk Items.</summary>
public sealed record AnswerKey : IStationery
{
    public string Id => "answer-key";
    public string Name => "Answer Key";
    public string Description => "Use during a round: reveals the best play for your current hand.";
    public StationeryTarget Target => StationeryTarget.None;
}

/// <summary>Extra submissions for the current round.</summary>
public sealed record MarginClip(int Submissions = 1) : IStationery
{
    public string Id => "margin-clip";
    public string Name => "Margin Clip";
    public string Description => $"Use during a round: +{Submissions} submission{(Submissions == 1 ? "" : "s")} this round.";
    public StationeryTarget Target => StationeryTarget.None;
}

/// <summary>Redraws chosen hand tiles without spending a discard.</summary>
public sealed record Scissors(int MaxTiles = 2) : IStationery
{
    public string Id => "scissors";
    public string Name => "Scissors";
    public string Description => $"Select up to {MaxTiles} hand tiles, then use: redraws them without spending a discard.";
    public StationeryTarget Target => StationeryTarget.HandTiles;
}

/// <summary>Removes one tile from the board for the rest of the round.</summary>
public sealed record WhiteOut : IStationery
{
    public string Id => "white-out";
    public string Name => "White-Out";
    public string Description => "Use, then click a board tile: removes it for the rest of the round.";
    public StationeryTarget Target => StationeryTarget.BoardTile;
}

/// <summary>Turns one hand tile wild for the rest of the round (the deck copy is unchanged).</summary>
public sealed record FountainPen : IStationery
{
    public string Id => "fountain-pen";
    public string Name => "Fountain Pen";
    public string Description => "Select a hand tile, then use: it becomes a wild tile (any letter, 0 chips) this round.";
    public StationeryTarget Target => StationeryTarget.HandTiles;
}

/// <summary>Flat Mult on every play for the rest of the round; uses stack.</summary>
public sealed record RedInkBottle(int Mult = 3) : IStationery
{
    public string Id => "red-ink-bottle";
    public string Name => "Red Ink Bottle";
    public string Description => $"Use during a round: +{Mult} mult on every play for the rest of the round.";
    public StationeryTarget Target => StationeryTarget.None;
}

/// <summary>Marks one hand tile: its letter value counts <see cref="Factor"/> times on the next play it is part of.</summary>
public sealed record Highlighter(int Factor = 3) : IStationery
{
    public string Id => "highlighter";
    public string Name => "Highlighter";
    public string Description => $"Select a hand tile, then use: its letter value counts ×{Factor} on your next play.";
    public StationeryTarget Target => StationeryTarget.HandTiles;
}

/// <summary>Sticks a premium square onto an empty cell for the rest of the round.</summary>
public sealed record GoldStar(Premium Premium = Premium.DoubleWord) : IStationery
{
    public string Id => "gold-star";
    public string Name => "Gold Star";
    public string Description => $"Use, then click an empty square: it becomes a {Label(Premium)} square for the rest of the round.";
    public StationeryTarget Target => StationeryTarget.EmptyCell;

    private static string Label(Premium premium) => premium switch
    {
        Premium.DoubleLetter => "2L",
        Premium.TripleLetter => "3L",
        Premium.DoubleWord => "2W",
        Premium.TripleWord => "3W",
        _ => "plain",
    };
}

/// <summary>Reprints a word already on the board: its letter chips count again on the next play.</summary>
public sealed record Clipping : IStationery
{
    public string Id => "clipping";
    public string Name => "Clipping";
    public string Description => "Use, then click a word on the board: its letter chips are scored again on your next play.";
    public StationeryTarget Target => StationeryTarget.BoardWord;
}

/// <summary>Breaks the rules once: the next play may contain one word that isn't in the dictionary, scored in full.</summary>
public sealed record PoeticLicense : IStationery
{
    public string Id => "poetic-license";
    public string Name => "Poetic License";
    public string Description => "Use during a round: one of your next plays may contain a word that isn't in the dictionary, scored in full.";
    public StationeryTarget Target => StationeryTarget.None;
}

/// <summary>Takes back the play just made: its tiles return to the hand and the submission is refunded.</summary>
public sealed record CorrectionTape : IStationery
{
    public string Id => "correction-tape";
    public string Name => "Correction Tape";
    public string Description => "Use right after a play: takes it back — the tiles return to your hand and you get the submission back.";
    public StationeryTarget Target => StationeryTarget.None;
}

public static class StationeryCatalog
{
    public static ImmutableArray<IStationery> All { get; } =
    [
        new AnswerKey(), new MarginClip(), new Scissors(), new WhiteOut(), new RedInkBottle(), new FountainPen(),
        new Highlighter(), new GoldStar(), new Clipping(), new PoeticLicense(), new CorrectionTape(),
    ];

    public static IStationery? Find(string id) => All.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True if a held item could still change the hand or board, so having no legal play and no discards left
    /// is not yet a lost round.
    /// </summary>
    public static bool CanEscape(IEnumerable<IStationery> held, RoundState round) =>
        held.Any(s => s switch
        {
            Scissors => !round.Bag.IsEmpty,
            WhiteOut => !round.Board.IsEmpty,
            FountainPen => round.Hand.Tiles.Any(t => !t.IsWild),
            PoeticLicense => round.Hand.Count > 0,
            CorrectionTape => round.Undo is not null,
            _ => false,
        });
}
