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
/// Stationery is valued like deck edits, by a fixed estimated gain per item (<see cref="StationeryGain"/>), and
/// used in rounds by <see cref="StationeryBot"/>. Wild tiles too: <see cref="WildTileGain"/> for a new wild tile,
/// <see cref="WildEditGain"/> for making the deck's most awkward tile wild (<see cref="NaiveShopBot.WildTarget"/>).
/// <see cref="DeskItemGain"/> adds a fixed gain for Desk Items whose value re-scoring can't see (Tile Rack's bigger hand);
/// off by default: Tile Rack at a fixed gain of 0.05 / 0.1 / 0.2 was bought in ≤6% of runs and changed wins by ≤0.5 pts.
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
    double StrikeGain = 0,
    IReadOnlyDictionary<string, double>? StationeryGain = null,
    double WildTileGain = 0,
    double WildEditGain = 0,
    IReadOnlyDictionary<string, double>? DeskItemGain = null,
    IReadOnlyDictionary<TileEnhancement, double>? EnhancedTileGains = null,
    IReadOnlyDictionary<string, double>? UpgradeGains = null)
{
    /// <summary>
    /// Wild buying is off: measured at ScoreFraction 0.75 (200 paired runs), a wild tile or wild edit at gain 0.05/0.15
    /// changed wins by ±0.5 pts, the Fountain Pen by −1.5 and all three together by −5 — never worth the money.
    /// Buys only the Margin Clip: measured alone at $3 (200 runs, ScoreFraction 0.75) it added +13 pts of win rate,
    /// while Answer Key (−3), Red Ink Bottle (−2.5) and Scissors (−8, cutting Q/Z/X/J) cost the bot more than they
    /// returned. White-Out is only used to escape a stuck hand, which almost never happens.
    /// Office upgrades (2026-10-13, 200 paired runs at ScoreFraction 0.75, $10, bot forced to buy the one on offer):
    /// Overtime +2 and Double Truck +2, Composing Stick 0, Filing Cabinet −2, Wastebasket −6, Bonus Clause −6.5,
    /// Photocopier −9, Supply Closet / Editorial Cuts / Trust Fund −10 (the bot never saves, rarely uses Stationery and
    /// never strikes, so $10 spent on them is $10 lost). Buying only Overtime and Double Truck at 0.3: +4.5 (36 → 40.5%).
    /// </summary>
    public static ShopBotConfig Default { get; } = new(
        StationeryGain: new Dictionary<string, double> { ["margin-clip"] = 0.05 },
        DeskItemGain: new Dictionary<string, double> { ["extra-extra"] = 0.2 }, // +1 submission/round is invisible to re-scoring
        UpgradeGains: new Dictionary<string, double> { ["overtime"] = 0.3, ["double-truck"] = 0.3 });

    /// <summary>Fixed estimated gain of buying a Stationery item (by id); 0 = never buy it.</summary>
    public double GainOf(Stationery.IStationery item) => StationeryGain?.GetValueOrDefault(item.Id) ?? 0;

    /// <summary>Fixed estimated gain of buying an Office upgrade (by upgrade id, any level); 0 = never buy it.</summary>
    public double GainOfUpgrade(string id) => UpgradeGains?.GetValueOrDefault(id) ?? 0;

    /// <summary>Fixed gain added to a Desk Item's measured value (by id), for effects outside scoring.</summary>
    public double GainOf(IDeskItem item) => DeskItemGain?.GetValueOrDefault(item.Id) ?? 0;

    /// <summary>
    /// Fixed estimated gain of an added tile with an enhancement (a new tile has no play history to re-score):
    /// <see cref="EnhancedTileGains"/> by enhancement, else <see cref="EnhancedTileGain"/>.
    /// </summary>
    public double GainOf(TileEnhancement enhancement) =>
        EnhancedTileGains is not null && EnhancedTileGains.TryGetValue(enhancement, out double gain) ? gain : EnhancedTileGain;
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
/// projected to the middle of the remaining run; money effects reduce an item's effective price. Enhancing a tile is
/// valued by re-scoring the history with the deck tile it placed most often enhanced (<see cref="PlayAnalysis.WithTile"/>);
/// other deck edits use fixed estimated gains. Valuation uses the best play per decision (a strong player's view of the shop), so the
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

            if (rerolls < config.MaxRerolls && (config.RerollWhenFull || session.Run.DeskItems.Length < session.Config.DeskSlots)
                && session.Run.Money - session.Shop!.RerollCost >= reserve + config.RerollSlack)
            {
                session = ShopRules.Reroll(session).Value;
                failed.Clear();
                rerolls++;
                continue;
            }
            break;
        }

        return session;
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
                case DeskItemOffer desk when run.DeskItems.Length < session.Config.DeskSlots:
                    if (run.Money < offer.Price)
                        break;
                    {
                        // The desk keeps itself in the best order (DeskOrder), so value the desk the player will have.
                        var m = evaluator.Measure(DeskOrder.Sorted(run.DeskItems.Add(desk.Item)), run.TierUpgrades);
                        yield return new Candidate(index, evaluator.Gain(baseline, m) + config.GainOf(desk.Item),
                            offer.Price - evaluator.Earnings(baseline, m), offer.Price,
                            s => BuyItem(s, index, null));
                    }
                    break;

                case DeskItemOffer desk:
                    for (int slot = 0; slot < run.DeskItems.Length; slot++)
                    {
                        int sell = session.Config.Shop.SellValueOf(run.DeskItems[slot]);
                        if (run.Money + sell < offer.Price)
                            continue;
                        int sold = slot;
                        var m = evaluator.Measure(DeskOrder.Sorted(run.DeskItems.RemoveAt(slot).Add(desk.Item)), run.TierUpgrades);
                        double fixedGain = config.GainOf(desk.Item) - config.GainOf(run.DeskItems[sold]);
                        yield return new Candidate(index, evaluator.Gain(baseline, m) + fixedGain,
                            offer.Price - sell - evaluator.Earnings(baseline, m), offer.Price - sell,
                            s => BuyItem(s, index, sold));
                    }
                    break;

                case StyleGuideOffer guide when run.Money >= offer.Price:
                    var g = evaluator.Measure(run.DeskItems, run.UpgradeTier(guide.TierMinLength).TierUpgrades);
                    yield return new Candidate(index, evaluator.Gain(baseline, g), offer.Price - evaluator.Earnings(baseline, g),
                        offer.Price, s => ShopRules.Buy(s, index));
                    break;

                case StationeryOffer stationery when run.Money >= offer.Price && run.Stationery.Length < session.Config.StationerySlots:
                    if (config.GainOf(stationery.Item) is var stationeryGain and > 0)
                        yield return new Candidate(index, stationeryGain, offer.Price, offer.Price, s => ShopRules.Buy(s, index));
                    break;

                case UpgradeOffer upgrade when run.Money >= offer.Price:
                    if (config.GainOfUpgrade(upgrade.UpgradeId) is var upgradeGain and > 0)
                        yield return new Candidate(index, upgradeGain, offer.Price, offer.Price, s => ShopRules.Buy(s, index));
                    break;

                case AddTileOffer { Wild: true } when run.Money >= offer.Price:
                    if (config.WildTileGain > 0)
                        yield return new Candidate(index, config.WildTileGain, offer.Price, offer.Price, s => ShopRules.Buy(s, index));
                    break;

                case WildOffer when run.Money >= offer.Price:
                    if (config.WildEditGain > 0 && NaiveShopBot.WildTarget(run.Deck) is { } wildTarget)
                        yield return new Candidate(index, config.WildEditGain, offer.Price, offer.Price,
                            s => ShopRules.Buy(s, index, [wildTarget.Id]));
                    break;

                case EnhanceOffer enhance when run.Money >= offer.Price:
                    {
                        // Enhance the plain deck tile the bot has placed most often; its value is what the history's plays
                        // would have scored with that tile enhanced (a tile it never plays measures no gain).
                        var target = evaluator.MostPlacedPlainTile(run.Deck)
                            ?? (NaiveShopBot.TilesFor(offer, session) is { Count: > 0 } ids ? run.Deck.First(t => t.Id == ids.First()) : null);
                        if (target is null)
                            break;
                        var m = evaluator.Measure(run.DeskItems, run.TierUpgrades, target with { Enhancement = enhance.Enhancement });
                        yield return new Candidate(index, evaluator.Gain(baseline, m) + config.EnhanceGain,
                            offer.Price - evaluator.Earnings(baseline, m), offer.Price, s => ShopRules.Buy(s, index, [target.Id]));
                    }
                    break;

                case StrikeOffer or AddTileOffer when run.Money >= offer.Price:
                    var tiles = NaiveShopBot.TilesFor(offer, session);
                    if (tiles is null)
                        break;
                    double gain = offer switch
                    {
                        StrikeOffer => config.StrikeGain,
                        AddTileOffer add => config.GainOf(add.Enhancement),
                        _ => 0,
                    };
                    yield return new Candidate(index, gain, offer.Price, offer.Price, s => ShopRules.Buy(s, index, tiles));
                    break;
            }
        }
    }

    private static Result<GameSession, string> BuyItem(GameSession session, int offer, int? sellSlot)
    {
        if (sellSlot is int slot)
        {
            var sold = ShopRules.Sell(session, slot);
            if (!sold.IsOk)
                return sold;
            session = sold.Value;
        }
        return ShopRules.Buy(session, offer);
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

        /// <summary>
        /// The plain, non-wild deck tile placed most often by the history's chosen plays (ties → lowest id), or null
        /// when none of the deck's plain tiles were placed.
        /// </summary>
        public Tile? MostPlacedPlainTile(ImmutableArray<Tile> deck)
        {
            var counts = _history.Decisions.SelectMany(d => d.Chosen.Placed).GroupBy(p => p.Tile.Id).ToDictionary(g => g.Key, g => g.Count());
            return deck.Where(t => t.Enhancement == TileEnhancement.None && !t.IsWild && counts.ContainsKey(t.Id))
                .OrderByDescending(t => counts[t.Id]).ThenBy(t => t.Id)
                .FirstOrDefault();
        }

        /// <summary>
        /// Scores a loadout against the history; with <paramref name="enhanced"/>, every play is re-scored as if that
        /// deck tile (by id) were the given tile (uncached: one candidate per shop).
        /// </summary>
        public Measurement Measure(IReadOnlyList<IDeskItem> items, ImmutableDictionary<int, int> upgrades, Tile? enhanced = null)
        {
            var projected = items.Select(Project).ToArray();
            var bases = BaseContexts(upgrades, enhanced);
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
        private ScoreContext[][] BaseContexts(ImmutableDictionary<int, int> upgrades, Tile? enhanced = null)
        {
            string key = string.Join(",", upgrades.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
            if (enhanced is null && _baseByUpgrades.TryGetValue(key, out var cached))
                return cached;
            var scoring = _session.Config.Scoring.WithUpgrades(upgrades);
            var bases = _history.Decisions
                .Select(d => d.Plays
                    .Select(p => ScoringEngine.Score(enhanced is null ? p : p.WithTile(enhanced), ImmutableArray<IDeskItem>.Empty,
                        d.Round.EffectiveScoring(scoring), d.Env))
                    .ToArray())
                .ToArray();
            if (enhanced is null)
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

    /// <summary>
    /// The tile a wild edit should convert: the most awkward letter (Q, Z, X, J, V, K) first, otherwise a copy of the
    /// deck's most common letter; plain tiles before enhanced ones. Null when every tile is already wild.
    /// </summary>
    public static Tile? WildTarget(IReadOnlyList<Tile> deck)
    {
        var candidates = deck.Where(t => !t.IsWild).ToList();
        var counts = candidates.GroupBy(t => t.Letter.Char).ToDictionary(g => g.Key, g => g.Count());
        return candidates
            .OrderByDescending(t => AwkwardLetters.IndexOf(t.Letter.Char) is var i and >= 0 ? AwkwardLetters.Length - i : 0)
            .ThenByDescending(t => counts[t.Letter.Char])
            .ThenBy(t => t.Enhancement == TileEnhancement.None ? 0 : 1)
            .ThenBy(t => t.Id)
            .FirstOrDefault();
    }

    /// <summary>Tile choice for edits, or null to skip the offer.</summary>
    internal static IReadOnlyCollection<int>? TilesFor(ShopOffer offer, GameSession session)
    {
        var deck = session.Run.Deck;
        switch (offer)
        {
            case EnhanceOffer:
                var target = deck.Where(t => t.Enhancement == TileEnhancement.None && !t.IsWild)
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
            case StationeryOffer:
                return null; // bots don't use Stationery yet
            case WildOffer:
                return null; // not valued yet (like other deck edits)
            default:
                return [];
        }
    }
}
