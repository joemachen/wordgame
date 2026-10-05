using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Run;

namespace Crossword.Core.Analysis;

public sealed record SimulatedRunRound(int RoundIndex, string Kind, string? Boss, long Target, long Score, bool Won);

public sealed record SimulatedRun(
    ulong Seed,
    ImmutableArray<SimulatedRunRound> Rounds,
    bool Victory,
    int FinalMoney,
    ImmutableArray<string> DeskItems)
{
    public int RoundsCleared => Rounds.Count(r => r.Won);
}

/// <summary>
/// Balance tooling: plays whole runs with a bot. In rounds it uses <see cref="RoundSimulator"/>'s skill model;
/// in the shop it buys the most expensive affordable Desk Item, then enhances its most common letter, strikes
/// awkward letters (J, Q, X, Z, V, K) and buys enhanced tiles. Deliberately naive — a floor on what a thoughtful
/// player gets out of the shop.
/// </summary>
public static class RunSimulator
{
    private const string AwkwardLetters = "QZXJVK";

    public static SimulatedRun PlayRun(ulong seed, RunConfig config, IWordGraph lexicon, double skill = 1.0)
    {
        var session = RunRules.NewGame(seed, config, lexicon);
        var rounds = ImmutableArray.CreateBuilder<SimulatedRunRound>();

        while (session.Phase is RunPhase.InRound or RunPhase.Shop)
        {
            if (session.Phase == RunPhase.Shop)
            {
                session = Shop(session);
                session = RunRules.LeaveShop(session, lexicon).Value;
                continue;
            }

            var startIndex = session.Run.RoundIndex;
            session = PlayRound(session, lexicon, skill);
            var round = session.Round;
            rounds.Add(new SimulatedRunRound(startIndex, config.KindOf(startIndex).Name, round.Config.Boss?.Name,
                round.Config.TargetScore, round.Score, round.Status == RoundStatus.Won));
        }

        return new SimulatedRun(seed, rounds.ToImmutable(), session.Phase == RunPhase.Victory, session.Run.Money,
            session.Run.DeskItems.Select(d => d.Id).ToImmutableArray());
    }

    private static GameSession PlayRound(GameSession session, IWordGraph lexicon, double skill)
    {
        while (session.Phase == RunPhase.InRound)
        {
            var round = session.Round;
            var ranked = MoveRanker.Rank(round.Board, round.Hand, lexicon, session.Run.DeskItems,
                round.Config.EffectiveScoring(session.Config.Scoring), round.Config.MinWordLength);

            if (ranked.Count == 0)
            {
                var discarded = RunRules.Discard(session, round.Hand.Tiles.Select(t => t.Id).ToArray(), lexicon);
                if (!discarded.IsOk)
                    break;
                session = discarded.Value;
                continue;
            }

            var choice = ranked[(int)((1 - skill) * (ranked.Count - 1))];
            session = RunRules.Submit(session, choice.Play.Placed, lexicon).Value.Session;
        }
        return session;
    }

    private static GameSession Shop(GameSession session)
    {
        bool bought = true;
        while (bought)
        {
            bought = false;
            var offers = session.Shop!.Offers;
            var candidates = Enumerable.Range(0, offers.Length)
                .Where(i => offers[i] is { } o && o.Price <= session.Run.Money)
                .OrderBy(i => Priority(offers[i]!))
                .ThenByDescending(i => offers[i]!.Price);

            foreach (int i in candidates)
            {
                var tiles = TilesFor(offers[i]!, session);
                if (tiles is null)
                    continue;
                var result = ShopRules.Buy(session, i, tiles);
                if (result.IsOk)
                {
                    session = result.Value;
                    bought = true;
                    break;
                }
            }
        }
        return session;
    }

    private static int Priority(ShopOffer offer) => offer switch
    {
        DeskItemOffer => 0,
        EnhanceOffer => 1,
        StrikeOffer => 2,
        AddTileOffer { Enhancement: not TileEnhancement.None } => 3,
        _ => 9,
    };

    /// <summary>Tile choice for edits, or null to skip the offer.</summary>
    private static IReadOnlyCollection<int>? TilesFor(ShopOffer offer, GameSession session)
    {
        var deck = session.Run.Deck;
        switch (offer)
        {
            case EnhanceOffer:
                var target = deck.Where(t => t.Enhancement == TileEnhancement.None)
                    .GroupBy(t => t.Letter.Char)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.First();
                return target is null ? null : [target.Id];
            case StrikeOffer strike:
                var awkward = deck.Where(t => AwkwardLetters.Contains(t.Letter.Char) && t.Enhancement == TileEnhancement.None)
                    .Take(strike.MaxTiles).Select(t => t.Id).ToList();
                return awkward.Count == 0 ? null : awkward;
            case AddTileOffer { Enhancement: TileEnhancement.None }:
                return null;
            default:
                return [];
        }
    }
}
