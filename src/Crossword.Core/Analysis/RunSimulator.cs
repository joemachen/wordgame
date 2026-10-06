using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Run;

namespace Crossword.Core.Analysis;

public sealed record SimulatedRunRound(int RoundIndex, string Kind, string? Boss, long Target, long Score, bool Won, int Submissions)
{
    /// <summary>The paycheck for this round (null if it was lost).</summary>
    public Payout? Payout { get; init; }

    /// <summary>Money earned during the round's plays (Gilded tiles, Syndication, …).</summary>
    public int InRoundMoney { get; init; }

    /// <summary>What the shop after this round cost (null when no shop followed).</summary>
    public ShopSpend? Shop { get; init; }
}

/// <summary>
/// One shop visit's spending by category. Desk Items, Style Guides and Stationery are priced from what was added;
/// <see cref="DeckEdits"/> is the rest of the net spend. <see cref="Sold"/> is money back from selling.
/// </summary>
public sealed record ShopSpend(int MoneyBefore, int MoneyAfter, int DeskItems, int StyleGuides, int Stationery, int Rerolls, int Sold)
{
    public int Net => MoneyBefore - MoneyAfter;

    public int DeckEdits => Net + Sold - DeskItems - StyleGuides - Stationery - Rerolls;

    public bool BoughtNothing => Net + Sold == 0;
}

public sealed record SimulatedRun(
    ulong Seed,
    ImmutableArray<SimulatedRunRound> Rounds,
    bool Victory,
    int FinalMoney,
    ImmutableArray<string> DeskItems)
{
    public int RoundsCleared => Rounds.Count(r => r.Won);

    /// <summary>Ids of the Stationery used during the run, in order.</summary>
    public ImmutableArray<string> StationeryUsed { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Balance tooling: plays whole runs with a bot. In rounds it uses <see cref="RoundSimulator"/>'s skill model;
/// in the shop it follows <see cref="ShopStrategy"/> — by default <see cref="EvaluatingShopBot"/>, which values
/// purchases by re-scoring the plays it recently faced.
/// </summary>
public static class RunSimulator
{
    /// <param name="pressRun">The run's Press Run, applied to <paramref name="config"/> (pass the base config).</param>
    public static SimulatedRun PlayRun(ulong seed, RunConfig config, IWordGraph lexicon, double skill = 1.0,
        ShopStrategy strategy = ShopStrategy.Evaluating, ShopBotConfig? bot = null, SkillModel model = SkillModel.Percentile,
        int pressRun = PressRuns.Lowest)
    {
        bot ??= ShopBotConfig.Default;
        var session = RunRules.NewGame(seed, config, lexicon, pressRun: pressRun);
        var rounds = ImmutableArray.CreateBuilder<SimulatedRunRound>();
        var history = ShopHistory.Empty;
        var stationeryUsed = ImmutableArray.CreateBuilder<string>();

        while (session.Phase is RunPhase.InRound or RunPhase.Shop)
        {
            if (session.Phase == RunPhase.Shop)
            {
                var before = session;
                session = strategy == ShopStrategy.Naive ? NaiveShopBot.Shop(session) : EvaluatingShopBot.Shop(session, history, bot);
                if (rounds.Count > 0)
                    rounds[^1] = rounds[^1] with { Shop = Spend(before, session) };
                session = RunRules.LeaveShop(session, lexicon).Value;
                continue;
            }

            var startIndex = session.Run.RoundIndex;
            int moneyBefore = session.Run.Money;
            int submissions;
            (session, history, submissions) = PlayRound(session, lexicon, skill, model,
                strategy == ShopStrategy.Evaluating ? bot : null, history, stationeryUsed);
            var round = session.Round;
            var payout = round.Status == RoundStatus.Won ? session.LastPayout : null;
            rounds.Add(new SimulatedRunRound(startIndex, config.KindOf(startIndex).Name, round.Config.Boss?.Name,
                round.Config.TargetScore, round.Score, round.Status == RoundStatus.Won, submissions)
            {
                Payout = payout,
                InRoundMoney = session.Run.Money - moneyBefore - (payout?.Total ?? 0),
            });
        }

        return new SimulatedRun(seed, rounds.ToImmutable(), session.Phase == RunPhase.Victory, session.Run.Money,
            session.Run.DeskItems.Select(d => d.Id).ToImmutableArray())
        {
            StationeryUsed = stationeryUsed.ToImmutable(),
        };
    }

    /// <summary>
    /// Plays the round, using held Stationery via <see cref="StationeryBot"/>; when <paramref name="bot"/> is set,
    /// records each decision for the shop bot. Returns the number of submissions made.
    /// </summary>
    private static (GameSession, ShopHistory, int) PlayRound(GameSession session, IWordGraph lexicon, double skill,
        SkillModel model, ShopBotConfig? bot, ShopHistory history, ImmutableArray<string>.Builder stationeryUsed)
    {
        int submissions = 0;
        while (session.Phase == RunPhase.InRound)
        {
            var round = session.Round;
            var scoring = round.Config.EffectiveScoring(session.Scoring);
            var env = RoundRules.Environment(round, session.Run.Money);
            var ranked = MoveRanker.Rank(round.Board, round.Hand, lexicon, session.Run.DeskItems, scoring,
                round.Config.MinWordLength, env, round.Config.CensoredLetter);

            if (ranked.Count == 0)
            {
                if (round.DiscardsLeft == 0)
                {
                    // Only reachable while holding Scissors / White-Out (otherwise the round is already lost).
                    var escaped = StationeryBot.Escape(session, lexicon);
                    if (escaped is null)
                        break;
                    RecordUse(session, escaped, stationeryUsed);
                    session = escaped;
                    continue;
                }
                var discarded = RunRules.Discard(session, round.Hand.Tiles.Select(t => t.Id).ToArray(), lexicon);
                if (!discarded.IsOk)
                    break;
                session = discarded.Value;
                continue;
            }

            var choice = PlayChooser.Choose(ranked, skill, model);
            var (afterStationery, revealed) = StationeryBot.BeforePlay(session, ranked, choice, lexicon);
            if (!ReferenceEquals(afterStationery, session))
            {
                RecordUse(session, afterStationery, stationeryUsed);
                session = afterStationery;
                if (revealed is null)
                    continue; // re-rank under the new round state (e.g. Red Ink's bonus)
                choice = revealed;
            }

            if (bot is not null)
                history = history.Add(ShopHistory.Capture(session.Run.RoundIndex, round, env, ranked, scoring, choice.Play,
                    bot.CandidatePlays), bot.HistoryWindow);
            session = RunRules.Submit(session, choice.Play.Placed, lexicon).Value.Session;
            submissions++;
        }
        return (session, history, submissions);
    }

    /// <summary>Prices a shop visit from the state before and after it (see <see cref="ShopSpend"/>).</summary>
    private static ShopSpend Spend(GameSession before, GameSession after)
    {
        var shop = before.Config.Shop;
        var oldItems = before.Run.DeskItems.Select(d => d.Id).ToHashSet();
        var newItems = after.Run.DeskItems.Select(d => d.Id).ToHashSet();
        int desk = after.Run.DeskItems.Where(d => !oldItems.Contains(d.Id)).Sum(shop.PriceOf);
        int sold = before.Run.DeskItems.Where(d => !newItems.Contains(d.Id)).Sum(shop.SellValueOf);
        int guides = (after.Run.TierUpgrades.Values.Sum() - before.Run.TierUpgrades.Values.Sum()) * shop.StyleGuidePrice;
        var heldBefore = before.Run.Stationery.Select(s => s.Id).ToList();
        int stationery = 0;
        foreach (var item in after.Run.Stationery)
        {
            if (!heldBefore.Remove(item.Id))
                stationery += shop.PriceOf(item);
        }
        int rerolls = 0;
        for (int cost = before.Shop!.RerollCost; cost < after.Shop!.RerollCost; cost += shop.RerollStep)
            rerolls += cost;
        return new ShopSpend(before.Run.Money, after.Run.Money, desk, guides, stationery, rerolls, sold);
    }

    /// <summary>Records which Stationery item a transition used up (the one no longer held).</summary>
    private static void RecordUse(GameSession before, GameSession after, ImmutableArray<string>.Builder used)
    {
        var remaining = after.Run.Stationery.Select(s => s.Id).ToList();
        foreach (var item in before.Run.Stationery)
        {
            if (!remaining.Remove(item.Id))
            {
                used.Add(item.Id);
                return;
            }
        }
    }
}
