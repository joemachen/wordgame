using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Scoring;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Run;

public class BossModifierTests
{
    private static readonly RoundConfig Base = new(TargetScore: 500);

    [Fact]
    public void Apply_RecordsBossOnConfig()
    {
        var boss = new TightDeadline();

        Assert.Same(boss, boss.Apply(Base).Boss);
    }

    [Fact]
    public void TightDeadline_ReducesSubmissions() =>
        Assert.Equal(3, new TightDeadline().Apply(Base).Submissions);

    [Fact]
    public void StrictGrammarian_RaisesMinimumWordLength() =>
        Assert.Equal(3, new StrictGrammarian().Apply(Base).MinWordLength);

    [Fact]
    public void StrictGrammarian_ScalesTheTarget() =>
        Assert.Equal((long)(Base.TargetScore * 0.5m), new StrictGrammarian(TargetScale: 0.5m).Apply(Base).TargetScore);

    [Fact]
    public void TightMargins_ShrinksBoard()
    {
        var (round, _) = RoundRules.Start(RunState.New(5), new TightMargins().Apply(Base), LexiconLoader.Enable);

        Assert.Equal(5, round.Board.Size);
    }

    [Fact]
    public void InkSpill_AddsSymmetricBlockedCells_OffPremiums()
    {
        var (round, _) = RoundRules.Start(RunState.New(5), new InkSpill(Pairs: 4).Apply(Base), LexiconLoader.Enable);
        var board = round.Board;

        Assert.Equal(8, board.Blocked.Count);
        Assert.All(board.Blocked, p =>
        {
            Assert.Contains(new Position(board.Size - 1 - p.Row, board.Size - 1 - p.Col), board.Blocked);
            Assert.Equal(Premium.None, board.PremiumAt(p));
        });
    }

    [Fact]
    public void VowelDrought_MakesVowelsCostChips_InEffectiveScoring()
    {
        var config = new VowelDrought(ValuePerVowel: -1).Apply(Base);

        var scoring = config.EffectiveScoring(ScoringConfig.Default);

        Assert.Equal(-1, scoring.ValueOf(Letter.From('E')));
        Assert.Equal(ScoringConfig.Default.ValueOf(Letter.From('C')), scoring.ValueOf(Letter.From('C')));
    }

    [Fact]
    public void VowelDrought_AppliesWhenSubmitting()
    {
        var round = new RoundState(new VowelDrought(ValuePerVowel: -1).Apply(Base with { BoardSize = 5 }), Board.Empty(5),
            TileBag.Empty, HandOf("CAT"), Rng.FromSeed(1), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 3);

        var outcome = RoundRules.Submit(round, Spell(round.Board, round.Hand, 0, 0, Direction.Across, "CAT"),
            Words, ImmutableArray<IDeskItem>.Empty, ScoringConfig.Default).Value;

        Assert.Equal(5 + 3 - 1 + 1, outcome.Score.Chips); // tier + C + (A taxed) + T
    }

    [Fact]
    public void StrictGrammarian_IsEnforcedWhenSubmitting()
    {
        var round = new RoundState(new StrictGrammarian().Apply(Base with { BoardSize = 5 }), Board.Empty(5),
            TileBag.Empty, HandOf("AT"), Rng.FromSeed(1), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 3);

        var result = RoundRules.Submit(round, Spell(round.Board, round.Hand, 0, 0, Direction.Across, "AT"),
            Words, ImmutableArray<IDeskItem>.Empty, ScoringConfig.Default);

        var error = Assert.IsType<RoundError.InvalidPlacement>(result.Error);
        Assert.IsType<PlacementError.WordsTooShort>(error.Error);
    }

    [Fact]
    public void Total_IsNeverNegative()
    {
        var play = PlayOn(Board.Empty(5), "AE", 0, 0, Direction.Across, "AE");

        Assert.Equal(0, (ScoreContext.Start(play, chips: -5, mult: 3)).Total);
    }

    [Fact]
    public void DefaultTiers_StartAtWeekZero_Ascend_AndUseCatalogBosses()
    {
        var tiers = BossCatalog.DefaultTiers;

        Assert.Equal(0, tiers[0].FirstWeek);
        Assert.True(tiers.Zip(tiers.Skip(1)).All(p => p.First.FirstWeek < p.Second.FirstWeek));
        Assert.All(tiers, t => Assert.NotEmpty(t.Bosses));
        Assert.All(tiers.SelectMany(t => t.Bosses), b => Assert.Contains(b, BossCatalog.All));
    }

    [Fact]
    public void Catalog_HasUniqueIds_AndDescriptions()
    {
        Assert.Equal(BossCatalog.All.Length, BossCatalog.All.Select(b => b.Id).Distinct().Count());
        Assert.All(BossCatalog.All, b => Assert.False(string.IsNullOrWhiteSpace(b.Description)));
    }

    /// <summary>Fixed tiers so retuning <see cref="ScoringConfig.Default"/> never breaks the Redundant Copy test.</summary>
    private static readonly ScoringConfig PinnedScoring = ScoringConfig.Default with
    {
        Tiers = [new WordTier(2, 2, 1), new WordTier(3, 5, 1)],
        LetterValues = ScoringConfig.Default.LetterValues.SetItems([new('A', 1), new('T', 1)]),
    };

    [Fact]
    public void RedundantCopy_RepeatedWordScoresNoLetterChips_ButKeepsItsTier()
    {
        // AT across, then a T under the A spells AT again (down).
        var round = new RoundState(new RedundantCopy().Apply(Base with { BoardSize = 5 }), Board.Empty(5), TileBag.Empty,
            HandOf("ATT"), Rng.FromSeed(1), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 3);
        var first = RoundRules.Submit(round, Spell(round.Board, round.Hand, 0, 0, Direction.Across, "AT"),
            Words, ImmutableArray<IDeskItem>.Empty, PinnedScoring).Value;

        var second = RoundRules.Submit(first.State, Spell(first.State.Board, first.State.Hand, 0, 0, Direction.Down, "AT"),
            Words, ImmutableArray<IDeskItem>.Empty, PinnedScoring).Value;

        Assert.Equal(2 + 2, first.Score.Chips);
        Assert.Equal(2, second.Score.Chips);
        Assert.Contains(second.Score.Log, e => e.Description.Contains("already printed"));
    }

    [Fact]
    public void RedundantCopy_ExtendingAWord_MakesANewWord()
    {
        var round = new RoundState(new RedundantCopy().Apply(Base with { BoardSize = 5 }), Board.Empty(5), TileBag.Empty,
            HandOf("CATS"), Rng.FromSeed(1), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 3);
        var first = RoundRules.Submit(round, Spell(round.Board, round.Hand, 0, 0, Direction.Across, "CAT"),
            Words, ImmutableArray<IDeskItem>.Empty, PinnedScoring).Value;

        var second = RoundRules.Submit(first.State, Spell(first.State.Board, first.State.Hand, 0, 0, Direction.Across, "CATS"),
            Words, ImmutableArray<IDeskItem>.Empty, PinnedScoring).Value;

        Assert.Equal(PinnedScoring.TierFor(4).BaseChips + "CATS".Sum(ch => PinnedScoring.LetterValues[ch]), second.Score.Chips);
    }

    [Fact]
    public void PuzzleMaster_AppliesBothBosses_RoundAndScoringEffects()
    {
        var master = new PuzzleMaster(new VowelDrought(ValuePerVowel: -1), new TightDeadline(Submissions: 3));

        var config = master.Apply(Base);

        Assert.Equal(3, config.Submissions);
        Assert.Same(master, config.Boss);
        Assert.Equal(-1, config.EffectiveScoring(ScoringConfig.Default).ValueOf(Letter.From('A')));
        Assert.Contains("Vowel Drought", master.Description);
        Assert.Contains("Tight Deadline", master.Description);
    }

    [Fact]
    public void PuzzleMaster_ScalesTheDeadline_AfterBothBosses()
    {
        var master = new PuzzleMaster(new StrictGrammarian(TargetScale: 0.5m), new TightDeadline(), TargetScale: 0.8m);

        Assert.Equal((long)((long)(Base.TargetScore * 0.5m) * 0.8m), master.Apply(Base).TargetScore);
    }

    [Fact]
    public void PuzzleMaster_Unresolved_ChangesNothing() =>
        Assert.Equal(Base with { Boss = new PuzzleMaster() }, new PuzzleMaster().Apply(Base));

    [Fact]
    public void PuzzleMaster_TightMarginsWithInkSpill_StartsARound()
    {
        var master = new PuzzleMaster(new TightMargins(), new InkSpill(Pairs: 3));

        var (round, _) = RoundRules.Start(RunState.New(5), master.Apply(Base), LexiconLoader.Enable);

        Assert.Equal(5, round.Board.Size);
        Assert.Equal(6, round.Board.Blocked.Count);
    }

    [Fact]
    public void PuzzleMaster_Pick_DrawsTwoDifferentCandidates()
    {
        var candidates = BossCatalog.PuzzleMasterCandidates;
        var picks = Enumerable.Range(1, 60).Select(seed => new PuzzleMaster().Pick(candidates, Rng.FromSeed((ulong)seed))).ToList();

        Assert.All(picks, p => Assert.NotEqual(p.First, p.Second));
        Assert.All(picks, p => Assert.Contains(p.First!, candidates));
        Assert.All(picks, p => Assert.Contains(p.Second!, candidates));
        Assert.True(picks.Select(p => (p.First!.Id, p.Second!.Id)).Distinct().Count() > 5);
        Assert.DoesNotContain(candidates, b => b is StrictGrammarian or PuzzleMaster);
    }
}
