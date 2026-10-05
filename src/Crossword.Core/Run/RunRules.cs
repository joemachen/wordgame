using System.Collections.Immutable;
using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Stationery;

namespace Crossword.Core.Run;

/// <summary>
/// Pure run-level transitions: new game → round → paycheck → shop → next round … → victory/defeat.
/// Round mechanics are delegated unchanged to <see cref="RoundRules"/>.
/// </summary>
public static class RunRules
{
    private const ulong WeekSalt = 0xD1B54A32D192ED03UL;

    public static GameSession NewGame(ulong seed, RunConfig config, IWordGraph lexicon)
    {
        var run = RunState.New(seed) with { Money = config.Economy.StartingMoney, Deck = config.StartingTiles };
        return StartRound(config, run, lexicon);
    }

    /// <summary>
    /// The week's boss, picked from that week's tier pool (<see cref="RunConfig.BossPoolFor"/>) using only the seed
    /// and week (no RNG consumed), so it can be previewed from the start of the week and is identical however the
    /// week is played.
    /// </summary>
    public static BossModifier BossFor(RunConfig config, RunState run, int week)
    {
        var pool = config.BossPoolFor(week);
        var (index, _) = Rng.FromSeed(run.Seed + (ulong)(week + 1) * WeekSalt).NextInt(pool.Length);
        return pool[index];
    }

    /// <summary>A round's deadline including its boss's adjustment (for previews before the round starts).</summary>
    public static long TargetFor(RunConfig config, RunState run, int roundIndex) =>
        config.RoundConfigFor(roundIndex, BossFor(config, run, config.WeekOf(roundIndex))).TargetScore;

    public static Result<SessionOutcome, RoundError> Submit(GameSession session, IReadOnlyList<PlacedTile> placed, IWordGraph lexicon)
    {
        if (session.Phase != RunPhase.InRound)
            return Result<SessionOutcome, RoundError>.Fail(new RoundError.RoundOver(session.Round.Status));

        var result = RoundRules.Submit(session.Round, placed, lexicon, session.Run.DeskItems, session.Scoring, session.Run.Money,
            canEscape: true);
        if (!result.IsOk)
            return Result<SessionOutcome, RoundError>.Fail(result.Error);

        var (round, score) = result.Value;
        var next = session with
        {
            Round = round,
            Run = session.Run with
            {
                Money = session.Run.Money + score.Money,
                DeskItems = session.Run.DeskItems.Select(item => item.AfterPlay(score.Play)).ToImmutableArray(),
            },
        };
        return Result<SessionOutcome, RoundError>.Ok(new SessionOutcome(Settle(CheckDeadlock(next, lexicon)), score));
    }

    public static Result<GameSession, RoundError> Discard(GameSession session, IReadOnlyCollection<int> tileIds, IWordGraph lexicon)
    {
        if (session.Phase != RunPhase.InRound)
            return Result<GameSession, RoundError>.Fail(new RoundError.RoundOver(session.Round.Status));

        var result = RoundRules.Discard(session.Round, tileIds, lexicon, canEscape: true);
        return result.IsOk
            ? Result<GameSession, RoundError>.Ok(Settle(CheckDeadlock(session with { Round = result.Value }, lexicon)))
            : Result<GameSession, RoundError>.Fail(result.Error);
    }

    /// <summary>
    /// Uses the Stationery in <paramref name="slot"/> during a round, consuming it. Items that target hand tiles
    /// (Scissors) read <paramref name="tileIds"/>; items that target the board (White-Out) read <paramref name="cell"/>.
    /// The Answer Key returns the best play for the current hand (scored with the Desk Items) for the UI to place.
    /// When the item can't take effect (bad target, no legal play for the Answer Key) it fails and stays in its slot.
    /// </summary>
    public static Result<StationeryUse, string> UseStationery(GameSession session, int slot, IWordGraph lexicon,
        IReadOnlyCollection<int>? tileIds = null, Position? cell = null)
    {
        if (session.Phase != RunPhase.InRound)
            return Result<StationeryUse, string>.Fail("Stationery can only be used during a round.");
        if (slot < 0 || slot >= session.Run.Stationery.Length)
            return Result<StationeryUse, string>.Fail($"No stationery in slot {slot + 1}.");

        var item = session.Run.Stationery[slot];
        var used = session with { Run = session.Run.RemoveStationery(slot).Value };
        var round = session.Round;
        switch (item)
        {
            case AnswerKey:
                var ranked = MoveRanker.Rank(round.Board, round.Hand, lexicon, session.Run.DeskItems,
                    round.Config.EffectiveScoring(session.Scoring), round.Config.MinWordLength,
                    RoundRules.Environment(round, session.Run.Money));
                return Hints.Best(ranked) is { } best
                    ? Result<StationeryUse, string>.Ok(new StationeryUse(used, best))
                    : Result<StationeryUse, string>.Fail("No legal play with this hand — discard some tiles first.");

            case MarginClip clip:
                return Done(used with { Round = round with { SubmissionsLeft = round.SubmissionsLeft + clip.Submissions } });

            case RedInkBottle ink:
                return Done(used with { Round = round with { Config = round.Config with { BonusMult = round.Config.BonusMult + ink.Mult } } });

            case Scissors scissors:
                var ids = tileIds ?? [];
                if (ids.Count == 0 || ids.Count > scissors.MaxTiles)
                    return Result<StationeryUse, string>.Fail($"Select 1 to {scissors.MaxTiles} hand tiles to cut.");
                if (round.Bag.IsEmpty)
                    return Result<StationeryUse, string>.Fail("The bag is empty — nothing to redraw.");
                var redrawn = RoundRules.Redraw(round, ids, lexicon, canEscape: true);
                return redrawn.IsOk
                    ? Done(used with { Round = redrawn.Value })
                    : Result<StationeryUse, string>.Fail(redrawn.Error.Message);

            case FountainPen:
                if (tileIds is not { Count: 1 } || round.Hand.Tiles.FirstOrDefault(t => t.Id == tileIds.First()) is not { } inked)
                    return Result<StationeryUse, string>.Fail("Select one hand tile to turn wild.");
                if (inked.IsWild)
                    return Result<StationeryUse, string>.Fail("That tile is already wild.");
                var hand = new Hand(round.Hand.Tiles.Replace(inked, Tile.Wild(inked.Id, inked.Enhancement)));
                return Done(used with { Round = round with { Hand = hand } });

            case WhiteOut:
                if (cell is not { } position)
                    return Result<StationeryUse, string>.Fail("Choose a board tile to white out.");
                var removed = RoundRules.RemoveTile(round, position, lexicon, canEscape: true);
                return removed.IsOk
                    ? Done(used with { Round = removed.Value })
                    : Result<StationeryUse, string>.Fail(removed.Error.Message);

            default:
                return Result<StationeryUse, string>.Fail($"{item.Name} can't be used.");
        }

        Result<StationeryUse, string> Done(GameSession next) =>
            Result<StationeryUse, string>.Ok(new StationeryUse(Settle(CheckDeadlock(next, lexicon)), null));
    }

    /// <summary>
    /// Re-checks the current round for a deadlock against the Stationery now held (e.g. after selling the Scissors
    /// that could have redrawn a stuck hand), then settles it.
    /// </summary>
    public static GameSession Recheck(GameSession session, IWordGraph lexicon) =>
        session.Phase == RunPhase.InRound ? Settle(CheckDeadlock(session, lexicon)) : session;

    /// <summary>Leaves the shop and starts the next round.</summary>
    public static Result<GameSession, string> LeaveShop(GameSession session, IWordGraph lexicon)
    {
        if (session.Phase != RunPhase.Shop)
            return Result<GameSession, string>.Fail("The shop isn't open.");
        return Result<GameSession, string>.Ok(StartRound(session.Config, session.Run.AdvanceRound(), lexicon));
    }

    /// <summary>After winning the run, keep playing with ever-growing targets.</summary>
    public static Result<GameSession, string> ContinueEndless(GameSession session)
    {
        if (session.Phase != RunPhase.Victory)
            return Result<GameSession, string>.Fail("Endless mode unlocks after winning the run.");
        return Result<GameSession, string>.Ok(OpenShop(session));
    }

    private static GameSession StartRound(RunConfig config, RunState run, IWordGraph lexicon)
    {
        var boss = BossFor(config, run, config.WeekOf(run.RoundIndex));
        var (round, nextRun) = RoundRules.Start(run, config.RoundConfigFor(run.RoundIndex, boss), lexicon);
        return new GameSession(config, nextRun, RunPhase.InRound, round);
    }

    /// <summary>No legal play and no discards loses the round, unless held Stationery can still change the hand or board.</summary>
    private static GameSession CheckDeadlock(GameSession session, IWordGraph lexicon) => session with
    {
        Round = RoundRules.CheckDeadlock(session.Round, lexicon, StationeryCatalog.CanEscape(session.Run.Stationery, session.Round)),
    };

    /// <summary>Resolves the end of a round: paycheck then shop (or victory), or defeat.</summary>
    private static GameSession Settle(GameSession session)
    {
        switch (session.Round.Status)
        {
            case RoundStatus.Won:
                var payout = Economy.Calculate(session.Config.Economy, session.Kind, session.Round, session.Run.Money);
                var paid = session with
                {
                    Run = session.Run with
                    {
                        Money = session.Run.Money + payout.Total,
                        DeskItems = session.Run.DeskItems.Select(item => item.AfterRoundWon(session.Kind.IsBoss)).ToImmutableArray(),
                    },
                    LastPayout = payout,
                };
                return session.Config.IsFinalRound(session.Run.RoundIndex)
                    ? paid with { Phase = RunPhase.Victory }
                    : OpenShop(paid);
            case RoundStatus.Lost:
                return session with { Phase = RunPhase.Defeat };
            default:
                return session;
        }
    }

    private static GameSession OpenShop(GameSession session)
    {
        var (shop, rng) = ShopRules.Generate(session.Run, session.Config, session.Run.Rng);
        return session with { Phase = RunPhase.Shop, Shop = shop, Run = session.Run with { Rng = rng } };
    }
}
