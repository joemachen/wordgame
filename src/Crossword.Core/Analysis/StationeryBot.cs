using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Stationery;

namespace Crossword.Core.Analysis;

/// <summary>
/// How simulated players use held Stationery during a round. Each call uses at most one item, so the caller re-ranks
/// after any change. Deliberately simple rules a sensible human would follow:
/// <list type="bullet">
/// <item>Answer Key: when the best play wins the round now and the chosen play doesn't.</item>
/// <item>Red Ink Bottle: on the first play of a boss round, or on the last submission when short of the target.</item>
/// <item>Margin Clip: on the last submission when the chosen play falls short of the target.</item>
/// <item>Scissors: to cut hard letters (Q, Z, X, J) the chosen play doesn't use, or to escape a stuck hand.</item>
/// <item>Fountain Pen: to turn such a hard letter wild (checked before Scissors), or to escape a stuck hand.</item>
/// <item>White-Out: only to escape a stuck hand (no legal play, no discards left).</item>
/// </list>
/// </summary>
public static class StationeryBot
{
    private const string AwkwardLetters = "QZXJVK";
    private const string HardLetters = "QZXJ";

    /// <summary>
    /// Before submitting <paramref name="choice"/>: returns the session after using one item (or the same session), and
    /// the play to submit instead when the Answer Key revealed one.
    /// </summary>
    public static (GameSession Session, RankedPlay? Play) BeforePlay(GameSession session, IReadOnlyList<RankedPlay> ranked,
        RankedPlay choice, IWordGraph lexicon)
    {
        var round = session.Round;
        var held = session.Run.Stationery;
        if (held.IsEmpty || ranked.Count == 0)
            return (session, null);

        long needed = round.Config.TargetScore - round.Score;
        bool fallsShort = choice.Score.Total < needed;
        bool lastSubmission = round.SubmissionsLeft == 1;

        if (fallsShort && ranked[0].Score.Total >= needed && Use<AnswerKey>(session, lexicon) is { } key)
            return (key.Session, key.Play);
        if (fallsShort && lastSubmission && Use<MarginClip>(session, lexicon) is { } clip)
            return (clip.Session, null);

        bool bossOpener = session.Kind.IsBoss && round.SubmissionsLeft == round.Config.Submissions;
        if ((bossOpener || (fallsShort && lastSubmission)) && Use<RedInkBottle>(session, lexicon) is { } ink)
            return (ink.Session, null);

        var playing = choice.Play.Placed.Select(p => p.Tile.Id).ToHashSet();
        int penSlot = SlotOf<FountainPen>(held);
        if (penSlot >= 0
            && round.Hand.Tiles.FirstOrDefault(t => !t.IsWild && HardLetters.Contains(t.Letter.Char) && !playing.Contains(t.Id)) is { } stuck
            && RunRules.UseStationery(session, penSlot, lexicon, tileIds: [stuck.Id]) is { IsOk: true } inked)
            return (inked.Value.Session, null);

        int scissorsSlot = SlotOf<Scissors>(held);
        if (scissorsSlot >= 0 && !round.Bag.IsEmpty)
        {
            var dead = round.Hand.Tiles
                .Where(t => !t.IsWild && HardLetters.Contains(t.Letter.Char) && !playing.Contains(t.Id))
                .Take(((Scissors)held[scissorsSlot]).MaxTiles)
                .Select(t => t.Id)
                .ToArray();
            if (dead.Length > 0 && RunRules.UseStationery(session, scissorsSlot, lexicon, tileIds: dead) is { IsOk: true } cut)
                return (cut.Value.Session, null);
        }

        return (session, null);
    }

    /// <summary>
    /// With no legal play and no discards left: redraws the most awkward tiles (Scissors) or whites out the board tile
    /// whose removal opens the best play. Returns null when nothing can be used.
    /// </summary>
    public static GameSession? Escape(GameSession session, IWordGraph lexicon)
    {
        var round = session.Round;
        var held = session.Run.Stationery;

        int scissorsSlot = SlotOf<Scissors>(held);
        if (scissorsSlot >= 0 && !round.Bag.IsEmpty)
        {
            var scoring = round.Config.EffectiveScoring(session.Scoring);
            var cut = round.Hand.Tiles
                .Where(t => !t.IsWild)
                .OrderByDescending(t => AwkwardLetters.Contains(t.Letter.Char))
                .ThenByDescending(t => scoring.ValueOf(t))
                .Take(((Scissors)held[scissorsSlot]).MaxTiles)
                .Select(t => t.Id)
                .ToArray();
            var used = RunRules.UseStationery(session, scissorsSlot, lexicon, tileIds: cut);
            if (used.IsOk)
                return used.Value.Session;
        }

        int penSlot = SlotOf<FountainPen>(held);
        if (penSlot >= 0 && round.Hand.Tiles.FirstOrDefault(t => !t.IsWild) is not null)
        {
            var scoring = round.Config.EffectiveScoring(session.Scoring);
            var ink = round.Hand.Tiles.Where(t => !t.IsWild)
                .OrderByDescending(t => AwkwardLetters.Contains(t.Letter.Char))
                .ThenByDescending(t => scoring.ValueOf(t))
                .First();
            var used = RunRules.UseStationery(session, penSlot, lexicon, tileIds: [ink.Id]);
            if (used.IsOk)
                return used.Value.Session;
        }

        int whiteOutSlot = SlotOf<WhiteOut>(held);
        if (whiteOutSlot >= 0 && !round.Board.IsEmpty)
        {
            var cell = BestCellToRemove(session, lexicon);
            var used = RunRules.UseStationery(session, whiteOutSlot, lexicon, cell: cell);
            if (used.IsOk)
                return used.Value.Session;
        }
        return null;
    }

    private static StationeryUse? Use<T>(GameSession session, IWordGraph lexicon) where T : IStationery
    {
        int slot = SlotOf<T>(session.Run.Stationery);
        if (slot < 0)
            return null;
        var used = RunRules.UseStationery(session, slot, lexicon);
        return used.IsOk ? used.Value : null;
    }

    private static int SlotOf<T>(IReadOnlyList<IStationery> held) where T : IStationery
    {
        for (int slot = 0; slot < held.Count; slot++)
        {
            if (held[slot] is T)
                return slot;
        }
        return -1;
    }

    /// <summary>The occupied cell whose removal gives the highest-scoring play (the first one when none helps).</summary>
    private static Position BestCellToRemove(GameSession session, IWordGraph lexicon)
    {
        var round = session.Round;
        var scoring = round.Config.EffectiveScoring(session.Scoring);
        var env = RoundRules.Environment(round, session.Run.Money);
        Position? best = null;
        long bestScore = -1;
        for (int row = 0; row < round.Board.Size; row++)
        {
            for (int col = 0; col < round.Board.Size; col++)
            {
                var cell = new Position(row, col);
                if (!round.Board.IsOccupied(cell))
                    continue;
                var ranked = MoveRanker.Rank(round.Board.Remove(cell), round.Hand, lexicon, session.Run.DeskItems, scoring,
                    round.Config.MinWordLength, env);
                long score = ranked.Count > 0 ? ranked[0].Score.Total : -1;
                if (best is null || score > bestScore)
                    (best, bestScore) = (cell, score);
            }
        }
        return best!.Value;
    }
}
