using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Run;

namespace Crossword.Core.Analysis;

public sealed record SimulatedRunRound(int RoundIndex, string Kind, string? Boss, long Target, long Score, bool Won, int Submissions);

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
/// in the shop it follows <see cref="ShopStrategy"/> — by default <see cref="EvaluatingShopBot"/>, which values
/// purchases by re-scoring the plays it recently faced.
/// </summary>
public static class RunSimulator
{
    public static SimulatedRun PlayRun(ulong seed, RunConfig config, IWordGraph lexicon, double skill = 1.0,
        ShopStrategy strategy = ShopStrategy.Evaluating, ShopBotConfig? bot = null, SkillModel model = SkillModel.Percentile)
    {
        bot ??= ShopBotConfig.Default;
        var session = RunRules.NewGame(seed, config, lexicon);
        var rounds = ImmutableArray.CreateBuilder<SimulatedRunRound>();
        var history = ShopHistory.Empty;

        while (session.Phase is RunPhase.InRound or RunPhase.Shop)
        {
            if (session.Phase == RunPhase.Shop)
            {
                session = strategy == ShopStrategy.Naive ? NaiveShopBot.Shop(session) : EvaluatingShopBot.Shop(session, history, bot);
                session = RunRules.LeaveShop(session, lexicon).Value;
                continue;
            }

            var startIndex = session.Run.RoundIndex;
            (session, history) = PlayRound(session, lexicon, skill, model, strategy == ShopStrategy.Evaluating ? bot : null, history);
            var round = session.Round;
            rounds.Add(new SimulatedRunRound(startIndex, config.KindOf(startIndex).Name, round.Config.Boss?.Name,
                round.Config.TargetScore, round.Score, round.Status == RoundStatus.Won,
                round.Config.Submissions - round.SubmissionsLeft));
        }

        return new SimulatedRun(seed, rounds.ToImmutable(), session.Phase == RunPhase.Victory, session.Run.Money,
            session.Run.DeskItems.Select(d => d.Id).ToImmutableArray());
    }

    /// <summary>Plays the round; when <paramref name="bot"/> is set, records each decision for the shop bot.</summary>
    private static (GameSession, ShopHistory) PlayRound(GameSession session, IWordGraph lexicon, double skill,
        SkillModel model, ShopBotConfig? bot, ShopHistory history)
    {
        while (session.Phase == RunPhase.InRound)
        {
            var round = session.Round;
            var scoring = round.Config.EffectiveScoring(session.Scoring);
            var env = RoundRules.Environment(round, session.Run.Money);
            var ranked = MoveRanker.Rank(round.Board, round.Hand, lexicon, session.Run.DeskItems, scoring,
                round.Config.MinWordLength, env);

            if (ranked.Count == 0)
            {
                var discarded = RunRules.Discard(session, round.Hand.Tiles.Select(t => t.Id).ToArray(), lexicon);
                if (!discarded.IsOk)
                    break;
                session = discarded.Value;
                continue;
            }

            var choice = PlayChooser.Choose(ranked, skill, model);
            if (bot is not null)
                history = history.Add(ShopHistory.Capture(session.Run.RoundIndex, round, env, ranked, scoring, choice.Play,
                    bot.CandidatePlays), bot.HistoryWindow);
            session = RunRules.Submit(session, choice.Play.Placed, lexicon).Value.Session;
        }
        return (session, history);
    }
}
