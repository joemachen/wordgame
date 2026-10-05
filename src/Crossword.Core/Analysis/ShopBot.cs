using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Scoring;

namespace Crossword.Core.Analysis;

public enum ShopStrategy
{
    /// <summary>Buys the priciest affordable Desk Item, then deck edits by fixed priority.</summary>
    Naive,

    /// <summary>Values each purchase by re-scoring the plays it recently faced (<see cref="EvaluatingShopBot"/>).</summary>
    Evaluating,
}

/// <summary>
/// Thresholds for <see cref="EvaluatingShopBot"/>. Gains are fractional score increases (0.1 = +10%).
/// Defaults picked by sweeping configs (150 runs, skill 0.7): a money reserve for interest lowered win rate
/// (−6 pts), and so did buying deck edits at any fixed estimated gain (−4 pts), so both are off by default;
/// rerolling with a full desk is neutral but spends money that would otherwise end the run unused.
/// </summary>
public sealed record ShopBotConfig(
    int CandidatePlays = 20,
    int HistoryWindow = 9,
    double MinGain = 0.03,
    double MustBuyGain = 0.25,
    int Reserve = 0,
    int MaxRerolls = 5,
    int RerollSlack = 2,
    bool RerollWhenFull = true,
    double EarningsHorizon = 0.5,
    double EnhanceGain = 0,
    double EnhancedTileGain = 0,
    double StrikeGain = 0)
{
    public static ShopBotConfig Default { get; } = new();
}

/// <summary>
/// One turn the bot faced: the strongest legal plays (trimmed), the environment and round rules they were scored
/// under, and the play actually made.
/// </summary>
public sealed record DecisionPoint(int RoundIndex, RoundConfig Round, ScoreEnvironment Env,
    ImmutableArray<PlayAnalysis> Plays, PlayAnalysis Chosen);

/// <summary>Rolling window of recent decisions, oldest first.</summary>
public sealed record ShopHistory(ImmutableList<DecisionPoint> Decisions)
{
    public static ShopHistory Empty { get; } = new(ImmutableList<DecisionPoint>.Empty);

    public ShopHistory Add(DecisionPoint decision, int window)
    {
        var decisions = Decisions.Add(decision);
        return new ShopHistory(decisions.Count > window ? decisions.RemoveRange(0, decisions.Count - window) : decisions);
    }

    /// <summary>
    /// Keeps the top <paramref name="count"/> plays under the current Desk Items plus the top <paramref name="count"/>
    /// with no Desk Items, so plays a new item would promote (e.g. long words for Broadsheet) stay available.
    /// </summary>
    public static DecisionPoint Capture(int roundIndex, RoundState round, ScoreEnvironment env, IReadOnlyList<RankedPlay> ranked,
        ScoringConfig effectiveScoring, PlayAnalysis chosen, int count)
    {
        var raw = ranked
            .Select(r => (r.Play, Total: ScoringEngine.Score(r.Play, ImmutableArray<IDeskItem>.Empty, effectiveScoring, env).Total))
            .OrderByDescending(x => x.Total)
            .Take(count)
            .Select(x => x.Play);
        var plays = ranked.Take(count).Select(r => r.Play).Concat(raw).Distinct(ReferenceEqualityComparer.Instance)
            .Cast<PlayAnalysis>().ToImmutableArray();
        return new DecisionPoint(roundIndex, round.Config, env, plays, chosen);
    }
}

/// <summary>
/// Shop bot that values purchases empirically: for each option it re-scores the best play of every recent decision
/// with the resulting Desk Items / Style Guide upgrades and compares against the current build. Scaling items are
/// projected to the middle of the remaining run; money effects reduce an item's effective price. Deck edits use
/// fixed estimated gains. Valuation uses the best play per decision (a strong player's view of the shop), so the
/// round-play <c>skill</c> stays the only difference between simulated players.
/// </summary>
public static class EvaluatingShopBot
{
    private sealed record Candidate(int Offer, double Gain, double EffectivePrice, int CashOut, Func<GameSession, Result<GameSession, string>> Apply);

    public static GameSession Shop(GameSession session, ShopHistory history, ShopBotConfig? config = null)
    {
        config ??= ShopBotConfig.Default;
        if (history.Decisions.IsEmpty)
            return NaiveShopBot.Shop(session);

        var evaluator = new Evaluator(session, history, config);
        var failed = new HashSet<int>();
        int rerolls = 0;

        while (true)
        {
            int remaining = session.Config.TotalRounds - 1 - session.Run.RoundIndex;
            int reserve = remaining <= session.Config.RoundsPerWeek ? 0 : config.Reserve;
            var best = Candidates(session, evaluator, config, failed)
                .Where(c => c.Gain >= config.MinGain || (c.EffectivePrice <= 0 && c.Gain >= 0))
                .Where(c => session.Run.Money - c.CashOut >= reserve || c.Gain >= config.MustBuyGain)
                .OrderByDescending(c => c.Gain / Math.Max(c.EffectivePrice, 1))
                .FirstOrDefault();

            if (best is not null)
            {
                var applied = best.Apply(session);
                if (applied.IsOk)
                    session = applied.Value;
                else
                    failed.Add(best.Offer); // e.g. a strike that would shrink the deck below the minimum
                continue;
            }

            if (rerolls < config.MaxRerolls && (config.RerollWhenFull || session.Run.DeskItems.Length < RunState.MaxDeskSlots)
                && session.Run.Money - session.Shop!.RerollCost >= reserve + config.RerollSlack)
            {
                session = ShopRules.Reroll(session).Value;
                failed.Clear();
                rerolls++;
                continue;
            }
            break;
        }

        return Reorder(session, evaluator);
    }

    private static IEnumerable<Candidate> Candidates(GameSession session, Evaluator evaluator, ShopBotConfig config, HashSet<int> failed)
    {
        var run = session.Run;
        var offers = session.Shop!.Offers;
        var baseline = evaluator.Measure(run.DeskItems, run.TierUpgrades);

        for (int i = 0; i < offers.Length; i++)
        {
            if (offers[i] is not { } offer || failed.Contains(i))
                continue;
            int index = i;

            switch (offer)
            {
                case DeskItemOffer desk when run.DeskItems.Length < RunState.MaxDeskSlots:
                    if (run.Money < offer.Price)
                        break;
                    for (int pos = 0; pos <= run.DeskItems.Length; pos++)
                    {
                        int at = pos;
                        var m = evaluator.Measure(run.DeskItems.Insert(pos, desk.Item), run.TierUpgrades);
                        yield return new Candidate(index, evaluator.Gain(baseline, m),
                            offer.Price - evaluator.Earnings(baseline, m), offer.Price,
                            s => BuyItem(s, index, null, at));
                    }
                    break;

                case DeskItemOffer desk:
                    for (int slot = 0; slot < run.DeskItems.Length; slot++)
                    {
                        int sell = session.Config.Shop.SellValueOf(run.DeskItems[slot]);
                        if (run.Money + sell < offer.Price)
                            continue;
                        var without = run.DeskItems.RemoveAt(slot);
                        for (int pos = 0; pos <= without.Length; pos++)
                        {
                            int sold = slot, at = pos;
                            var m = evaluator.Measure(without.Insert(pos, desk.Item), run.TierUpgrades);
                            yield return new Candidate(index, evaluator.Gain(baseline, m),
                                offer.Price - sell - evaluator.Earnings(baseline, m), offer.Price - sell,
                                s => BuyItem(s, index, sold, at));
                        }
                    }
                    break;

                case StyleGuideOffer guide when run.Money >= offer.Price:
                    var g = evaluator.Measure(run.DeskItems, run.UpgradeTier(guide.TierMinLength).TierUpgrades);
                    yield return new Candidate(index, evaluator.Gain(baseline, g), offer.Price - evaluator.Earnings(baseline, g),
                        offer.Price, s => ShopRules.Buy(s, index));
                    break;

                case EnhanceOffer or StrikeOffer or AddTileOffer when run.Money >= offer.Price:
                    var tiles = NaiveShopBot.TilesFor(offer, session);
                    if (tiles is null)
                        break;
                    double gain = offer switch
                    {
                        EnhanceOffer => config.EnhanceGain,
                        StrikeOffer => config.StrikeGain,
                        _ => config.EnhancedTileGain,
                    };
                    yield return new Candidate(index, gain, offer.Price, offer.Price, s => ShopRules.Buy(s, index, tiles));
                    break;
            }
        }
    }

    private static Result<GameSession, string> BuyItem(GameSession session, int offer, int? sellSlot, int position)
    {
        if (sellSlot is int slot)
        {
            var sold = ShopRules.Sell(session, slot);
            if (!sold.IsOk)
                return sold;
            session = sold.Value;
        }
        var bought = ShopRules.Buy(session, offer);
        if (!bought.IsOk)
            return bought;
        session = bought.Value;
        int last = session.Run.DeskItems.Length - 1;
        return position == last
            ? bought
            : Result<GameSession, string>.Ok(session with { Run = session.Run.MoveDeskItem(last, position).Value });
    }

    /// <summary>Greedily moves owned items while any single move raises the measured score.</summary>
    private static GameSession Reorder(GameSession session, Evaluator evaluator)
    {
        for (int pass = 0; pass < 10; pass++)
        {
            var run = session.Run;
            long best = evaluator.Measure(run.DeskItems, run.TierUpgrades).Score;
            RunState? improved = null;
            for (int from = 0; from < run.DeskItems.Length; from++)
            {
                for (int to = 0; to < run.DeskItems.Length; to++)
                {
                    if (from == to)
                        continue;
                    var moved = run.MoveDeskItem(from, to).Value;
                    long score = evaluator.Measure(moved.DeskItems, moved.TierUpgrades).Score;
                    if (score > best)
                        (best, improved) = (score, moved);
                }
            }
            if (improved is null)
                break;
            session = session with { Run = improved };
        }
        return session;
    }

    internal readonly record struct Measurement(long Score, double MoneyPerPlay);

    /// <summary>Scores loadouts against the history, caching everything that doesn't depend on Desk Items.</summary>
    internal sealed class Evaluator
    {
        private readonly GameSession _session;
        private readonly ShopHistory _history;
        private readonly ShopBotConfig _config;
        private readonly double _playsRemaining;
        private readonly int _playsToProject;
        private readonly int _bossesToProject;
        private readonly Dictionary<string, ScoreContext[][]> _baseByUpgrades = new();
        private readonly Dictionary<IDeskItem, IDeskItem> _projected = new(ReferenceEqualityComparer.Instance);

        public Evaluator(GameSession session, ShopHistory history, ShopBotConfig config)
        {
            _session = session;
            _history = history;
            _config = config;
            var runConfig = session.Config;
            int roundIndex = session.Run.RoundIndex;
            int remaining = Math.Max(0, runConfig.TotalRounds - 1 - roundIndex);
            double playsPerRound = (double)history.Decisions.Count / history.Decisions.Select(d => d.RoundIndex).Distinct().Count();
            _playsRemaining = playsPerRound * remaining;
            int horizonRounds = (remaining + 1) / 2;
            _playsToProject = (int)Math.Round(playsPerRound * remaining / 2);
            _bossesToProject = Enumerable.Range(roundIndex + 1, horizonRounds).Count(r => runConfig.KindOf(r).IsBoss);
        }

        public Measurement Measure(IReadOnlyList<IDeskItem> items, ImmutableDictionary<int, int> upgrades)
        {
            var projected = items.Select(Project).ToArray();
            var bases = BaseContexts(upgrades);
            long score = 0;
            long money = 0;
            foreach (var plays in bases)
            {
                ScoreContext? best = null;
                foreach (var ctx in plays)
                {
                    var scored = EffectPipeline.Apply(projected, ctx);
                    if (best is null || scored.Total > best.Total)
                        best = scored;
                }
                if (best is null)
                    continue;
                score += best.Total;
                money += best.Money;
            }
            return new Measurement(score, (double)money / Math.Max(1, bases.Length));
        }

        public double Gain(Measurement baseline, Measurement candidate) =>
            baseline.Score > 0 ? (double)candidate.Score / baseline.Score - 1 : candidate.Score > 0 ? 1 : 0;

        /// <summary>Extra money the candidate is expected to earn over the remaining run (discounted).</summary>
        public double Earnings(Measurement baseline, Measurement candidate) =>
            (candidate.MoneyPerPlay - baseline.MoneyPerPlay) * _playsRemaining * _config.EarningsHorizon;

        /// <summary>A scaling item's state at the middle of the remaining run; static items return themselves.</summary>
        private IDeskItem Project(IDeskItem item)
        {
            if (_projected.TryGetValue(item, out var cached))
                return cached;
            var projected = item;
            var chosen = _history.Decisions;
            for (int i = 0; i < _playsToProject; i++)
                projected = projected.AfterPlay(chosen[i % chosen.Count].Chosen);
            for (int i = 0; i < _bossesToProject; i++)
                projected = projected.AfterRoundWon(wasBoss: true);
            _projected[item] = projected;
            return projected;
        }

        /// <summary>Per decision, each candidate play scored up to (not including) the Desk Items.</summary>
        private ScoreContext[][] BaseContexts(ImmutableDictionary<int, int> upgrades)
        {
            string key = string.Join(",", upgrades.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
            if (_baseByUpgrades.TryGetValue(key, out var cached))
                return cached;
            var scoring = _session.Config.Scoring.WithUpgrades(upgrades);
            var bases = _history.Decisions
                .Select(d => d.Plays
                    .Select(p => ScoringEngine.Score(p, ImmutableArray<IDeskItem>.Empty, d.Round.EffectiveScoring(scoring), d.Env))
                    .ToArray())
                .ToArray();
            _baseByUpgrades[key] = bases;
            return bases;
        }
    }
}

/// <summary>
/// The original bot: buys the most expensive affordable Desk Item, then enhances its most common letter, strikes
/// awkward letters (J, Q, X, Z, V, K) and buys enhanced tiles. Deliberately naive — a floor on what a thoughtful
/// player gets out of the shop.
/// </summary>
public static class NaiveShopBot
{
    private const string AwkwardLetters = "QZXJVK";

    public static GameSession Shop(GameSession session)
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
        StyleGuideOffer => 1,
        EnhanceOffer => 2,
        StrikeOffer => 3,
        AddTileOffer { Enhancement: not TileEnhancement.None } => 4,
        _ => 9,
    };

    /// <summary>Tile choice for edits, or null to skip the offer.</summary>
    internal static IReadOnlyCollection<int>? TilesFor(ShopOffer offer, GameSession session)
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
            case StyleGuideOffer { TierMinLength: < 3 }:
                return null; // the bot rarely scores with 2-letter words as its longest
            default:
                return [];
        }
    }
}
