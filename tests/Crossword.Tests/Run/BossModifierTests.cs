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
}
