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

/// <summary>Flat Mult on every play for the rest of the round; uses stack.</summary>
public sealed record RedInkBottle(int Mult = 3) : IStationery
{
    public string Id => "red-ink-bottle";
    public string Name => "Red Ink Bottle";
    public string Description => $"Use during a round: +{Mult} mult on every play for the rest of the round.";
    public StationeryTarget Target => StationeryTarget.None;
}

public static class StationeryCatalog
{
    public static ImmutableArray<IStationery> All { get; } =
        [new AnswerKey(), new MarginClip(), new Scissors(), new WhiteOut(), new RedInkBottle()];

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
            _ => false,
        });
}
