using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;

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
        var run = RunState.New(seed) with { Money = config.Economy.StartingMoney };
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

    public static Result<SessionOutcome, RoundError> Submit(GameSession session, IReadOnlyList<PlacedTile> placed, IWordGraph lexicon)
    {
        if (session.Phase != RunPhase.InRound)
            return Result<SessionOutcome, RoundError>.Fail(new RoundError.RoundOver(session.Round.Status));

        var result = RoundRules.Submit(session.Round, placed, lexicon, session.Run.DeskItems, session.Scoring, session.Run.Money);
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
        return Result<SessionOutcome, RoundError>.Ok(new SessionOutcome(Settle(next), score));
    }

    public static Result<GameSession, RoundError> Discard(GameSession session, IReadOnlyCollection<int> tileIds, IWordGraph lexicon)
    {
        if (session.Phase != RunPhase.InRound)
            return Result<GameSession, RoundError>.Fail(new RoundError.RoundOver(session.Round.Status));

        var result = RoundRules.Discard(session.Round, tileIds, lexicon);
        return result.IsOk
            ? Result<GameSession, RoundError>.Ok(Settle(session with { Round = result.Value }))
            : Result<GameSession, RoundError>.Fail(result.Error);
    }

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
