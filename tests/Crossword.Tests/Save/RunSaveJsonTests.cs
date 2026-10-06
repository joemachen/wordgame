using System.Collections.Immutable;
using Crossword.Core.Analysis;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Save;
using Crossword.Core.Stationery;

namespace Crossword.Tests.Save;

[Trait("Category", "Save")]
public class RunSaveJsonTests
{
    private static readonly RunConfig Config = RunConfig.Default;

    private static SavedRun Load(string json)
    {
        var loaded = RunSaveJson.Deserialize(json, Config);
        Assert.True(loaded.IsOk, loaded.IsOk ? "" : loaded.Error);
        return loaded.Value;
    }

    /// <summary>Saves and reloads; record equality can't compare ImmutableArray contents, so compare the JSON.</summary>
    private static GameSession RoundTrip(GameSession session, int[]? handOrder = null)
    {
        string json = RunSaveJson.Serialize(session, handOrder);
        var loaded = Load(json);
        Assert.Equal(json, RunSaveJson.Serialize(loaded.Session, loaded.HandOrder));
        return loaded.Session;
    }

    /// <summary>A shop-phase session holding one item of the given kind, to round-trip polymorphic values.</summary>
    private static GameSession Holding(IEnumerable<IDeskItem>? desk = null, IEnumerable<IStationery>? stationery = null,
        IEnumerable<ShopOffer?>? offers = null, BossModifier? boss = null)
    {
        var session = RunRules.NewGame(3, Config, LexiconLoader.Enable);
        var run = session.Run with
        {
            DeskItems = (desk ?? []).ToImmutableArray(),
            Stationery = (stationery ?? []).ToImmutableArray(),
        };
        var round = session.Round with { Config = session.Round.Config with { Boss = boss } };
        var shop = new ShopState((offers ?? []).ToImmutableArray(), RerollCost: 5, Rng.FromSeed(9));
        return session with { Run = run, Round = round, Phase = RunPhase.Shop, Shop = shop };
    }

    [Fact]
    public void EveryDeskItem_RoundTripsWithItsParameters()
    {
        var items = DeskItemCatalog.All.Concat(
        [
            new WordCount(ChipsPerTile: 3, Chips: 42),
            new Archive(MinLength: 6, MultPerLongWord: 2, Mult: 7.5m),
            new Pulitzer(Factor: 2.5m, PerBoss: 0.25m),
            new PrintingPressRoller(MinWords: 2, Gain: 0.2m, Factor: 1.6m),
        ]).ToList();

        foreach (var item in items)
        {
            var restored = RoundTrip(Holding(desk: [item])).Run.DeskItems.Single();
            Assert.Equal(item, restored);
        }
    }

    [Fact]
    public void EveryStationery_RoundTrips()
    {
        foreach (var item in StationeryCatalog.All.Append(new RedInkBottle(Mult: 5)))
            Assert.Equal(item, RoundTrip(Holding(stationery: [item])).Run.Stationery.Single());
    }

    [Fact]
    public void EveryBoss_RoundTrips_IncludingAResolvedPuzzleMaster()
    {
        var bosses = BossCatalog.All.Append(new PuzzleMaster(new InkSpill(Pairs: 2), new TightDeadline(Submissions: 2), TargetScale: 1.2m));
        foreach (var boss in bosses)
            Assert.Equal(boss, RoundTrip(Holding(boss: boss)).Round.Config.Boss);
    }

    [Fact]
    public void EveryShopOffer_RoundTrips_KeepingBoughtSlots()
    {
        ShopOffer?[] offers =
        [
            new DeskItemOffer(new WordCount(Chips: 6), 4),
            null,
            new AddTileOffer(Letter.From('Q'), TileEnhancement.Gilded, 5),
            new AddTileOffer(Tile.WildPlaceholder, TileEnhancement.None, 6, Wild: true),
            new WildOffer(5),
            new EnhanceOffer(TileEnhancement.Italic, 4),
            new StrikeOffer(2, 3),
            new StyleGuideOffer(5, "Pulp Paperbacks", "5-letter", 10, 1, 3),
            new StationeryOffer(new MarginClip(), 6),
        ];
        var restored = RoundTrip(Holding(offers: offers)).Shop!.Offers;
        Assert.Equal(offers, restored.ToArray());
    }

    [Fact]
    public void RichSession_RoundTripsExactly()
    {
        var session = RunRules.NewGame(11, Config, LexiconLoader.Enable, run => run with
        {
            Money = 17,
            DeskItems = [new WordCount(Chips: 8), new RedPen(), new Pulitzer(Factor: 2)],
            Stationery = [new Scissors(), new RedInkBottle()],
            TierUpgrades = ImmutableDictionary<int, int>.Empty.Add(2, 1).Add(5, 3),
            Deck = run.Deck.SetItem(0, run.Deck[0] with { Enhancement = TileEnhancement.Bold })
                .SetItem(1, run.Deck[1] with { Enhancement = TileEnhancement.Italic })
                .SetItem(2, run.Deck[2] with { Enhancement = TileEnhancement.Gilded }),
        });
        session = session with { Round = session.Round with { Config = session.Round.Config with { TargetScore = 1_000_000 } } };
        var best = Rank(session)[0];
        session = RunRules.Submit(session, best.Play.Placed, LexiconLoader.Enable).Value.Session;
        Assert.Equal(RunPhase.InRound, session.Phase);

        var round = session.Round;
        var empty = Enumerable.Range(0, round.Board.Size * round.Board.Size)
            .Select(i => new Position(i / round.Board.Size, i % round.Board.Size))
            .Where(p => !round.Board.IsOccupied(p)).ToList();
        var board = round.Board.Place([new PlacedTile(empty[0], Tile.Wild(900).As(Letter.From('Z')))])
            with { Blocked = [empty[1], empty[2]] };
        session = session with
        {
            Round = round with { Board = board, Config = round.Config with { BonusMult = 3 } },
            LastPayout = new Payout(3, 2, 1, 4),
        };

        var restored = RoundTrip(session, [5, 3, 1]);

        Assert.Equal(session.Run.Seed, restored.Run.Seed);
        Assert.Equal(session.Run.Rng, restored.Run.Rng);
        Assert.Equal(session.Round.Rng, restored.Round.Rng);
        Assert.Equal(session.Round.Score, restored.Round.Score);
        Assert.Equal(3, restored.Round.Config.BonusMult);
        Assert.Equal(session.Round.WordsFormed.Order(), restored.Round.WordsFormed.Order());
        Assert.Equal(session.Round.Bag.Tiles.ToArray(), restored.Round.Bag.Tiles.ToArray());
        Assert.Equal(session.Round.Hand.Tiles.ToArray(), restored.Round.Hand.Tiles.ToArray());
        Assert.Equal(session.Round.Board.Cells.ToArray(), restored.Round.Board.Cells.ToArray());
        Assert.Equal(session.Round.Board.Premiums.ToArray(), restored.Round.Board.Premiums.ToArray());
        Assert.True(restored.Round.Board.Blocked.SetEquals(board.Blocked));
        var wild = restored.Round.Board.TileAt(empty[0])!;
        Assert.True(wild.IsWild);
        Assert.Equal('Z', wild.Letter.Char);
        Assert.Equal(session.Run.TierUpgrades, restored.Run.TierUpgrades);
        Assert.Equal(session.Run.Deck.ToArray(), restored.Run.Deck.ToArray());
        Assert.Equal(new Payout(3, 2, 1, 4), restored.LastPayout);
        Assert.Same(Config, restored.Config);
    }

    [Fact]
    public void HandOrder_RoundTrips()
    {
        var session = RunRules.NewGame(4, Config, LexiconLoader.Enable);
        var order = session.Round.Hand.Tiles.Select(t => t.Id).Reverse().ToArray();
        var loaded = Load(RunSaveJson.Serialize(session, order));
        Assert.Equal(order, loaded.HandOrder);
    }

    [Fact]
    [Trait("Category", "Determinism")]
    public void ResumingAfterEveryStep_PlaysOutExactlyLikeTheUninterruptedRun()
    {
        var straight = RunRules.NewGame(21, Config, LexiconLoader.Enable);
        var resumed = straight;
        for (int step = 0; step < 40 && straight.Phase is RunPhase.InRound or RunPhase.Shop; step++)
        {
            straight = Step(straight);
            resumed = Load(RunSaveJson.Serialize(Step(resumed))).Session;
            Assert.Equal(RunSaveJson.Serialize(straight), RunSaveJson.Serialize(resumed));
        }
        Assert.True(straight.Run.RoundIndex >= 3, "the run should get past a shop or two");
    }

    [Fact]
    public void NewerOrOlderVersion_IsRejected()
    {
        string json = RunSaveJson.Serialize(RunRules.NewGame(1, Config, LexiconLoader.Enable));
        foreach (int version in new[] { RunSaveJson.CurrentVersion + 1, RunSaveJson.CurrentVersion - 1 })
        {
            string other = json.Replace($"\"version\": {RunSaveJson.CurrentVersion}", $"\"version\": {version}");
            Assert.NotEqual(json, other);
            Assert.False(RunSaveJson.Deserialize(other, Config).IsOk);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"version\": 1}")]
    public void GarbageSave_FailsWithoutThrowing(string json) =>
        Assert.False(RunSaveJson.Deserialize(json, Config).IsOk);

    [Fact]
    public void UnknownDeskItem_FailsWithoutThrowing()
    {
        string json = RunSaveJson.Serialize(Holding(desk: [new RedPen()]));
        Assert.Contains("\"red-pen\"", json);
        var loaded = RunSaveJson.Deserialize(json.Replace("\"red-pen\"", "\"no-such-item\""), Config);
        Assert.False(loaded.IsOk);
        Assert.Contains("no-such-item", loaded.Error);
    }

    /// <summary>
    /// A save written by an earlier build must keep loading into the same state. If this fails after renaming or
    /// adding a state property, players' saves would silently lose data: bump <see cref="RunSaveJson.CurrentVersion"/>
    /// (or keep the old name), then regenerate the fixture by writing <see cref="FixtureSession"/>'s save over it.
    /// </summary>
    [Fact]
    public void CommittedFixture_StillLoadsIntoTheSameState()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Save", "Fixtures", "run-v1.json");
        string fixture = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd();
        string current = RunSaveJson.Serialize(FixtureSession()).ReplaceLineEndings("\n");
        Assert.Equal(fixture, current);

        var loaded = Load(fixture).Session;
        Assert.Equal(RunPhase.Shop, loaded.Phase);
        Assert.Equal(current, RunSaveJson.Serialize(loaded).ReplaceLineEndings("\n"));
    }

    /// <summary>Saves from before Press Runs (no <c>pressRun</c>, no <c>censoredLetter</c>) load as Proofreader runs.</summary>
    [Fact]
    public void FixtureFromBeforePressRuns_LoadsAsProofreader()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Save", "Fixtures", "run-v1-before-press-runs.json");
        string legacy = File.ReadAllText(path);
        Assert.DoesNotContain("pressRun", legacy);

        var loaded = Load(legacy).Session;

        Assert.Equal(PressRuns.Lowest, loaded.Run.PressRun);
        Assert.Same(Config, loaded.Config);
        Assert.Null(loaded.Round.Config.CensoredLetter);
        Assert.Equal(RunSaveJson.Serialize(FixtureSession()), RunSaveJson.Serialize(loaded));
    }

    [Fact]
    public void PressRun_SurvivesSaveAndLoad_AndItsRulesAreReapplied()
    {
        // Final Print Run at a Sunday: a Reprint boss and a censored letter in the round.
        var session = RunRules.NewGame(9, Config, LexiconLoader.Enable, run => run with { RoundIndex = 2 }, pressRun: 8);
        Assert.IsType<Reprint>(session.Round.Config.Boss);
        Assert.NotNull(session.Round.Config.CensoredLetter);

        var loaded = RoundTrip(session);

        Assert.Equal(8, loaded.Run.PressRun);
        Assert.Equal(session.Round.Config.Boss, loaded.Round.Config.Boss);
        Assert.Equal(session.Round.Config.CensoredLetter, loaded.Round.Config.CensoredLetter);
        Assert.Equal(PressRuns.Apply(Config, 8).Shop, loaded.Config.Shop);
        Assert.True(loaded.Config.BossExtraModifier);
        Assert.Equal(session.WeekBoss, loaded.WeekBoss);
    }

    [Fact]
    public void SaveWithAnUnknownPressRun_FailsWithoutThrowing()
    {
        string json = RunSaveJson.Serialize(RunRules.NewGame(3, Config, LexiconLoader.Enable));
        Assert.Contains("\"pressRun\": 1", json);

        Assert.False(RunSaveJson.Deserialize(json.Replace("\"pressRun\": 1", "\"pressRun\": 9"), Config).IsOk);
    }

    /// <summary>The state in <c>Save/Fixtures/run-v1.json</c>: a few rounds into seed 21, in the shop.</summary>
    internal static GameSession FixtureSession()
    {
        var session = RunRules.NewGame(21, Config, LexiconLoader.Enable, run => run with
        {
            DeskItems = [new WordCount(Chips: 12), new PrintingPressRoller(Factor: 1.3m)],
            Stationery = [new FountainPen()],
            TierUpgrades = ImmutableDictionary<int, int>.Empty.Add(3, 1),
        });
        while (session.Phase != RunPhase.Shop || session.Run.RoundIndex < 1)
            session = Step(session);
        return session with
        {
            Round = session.Round with
            {
                Config = session.Round.Config with { Boss = new PuzzleMaster(new InkSpill(), new RedundantCopy()), BonusMult = 3 },
            },
        };
    }

    private static IReadOnlyList<RankedPlay> Rank(GameSession session)
    {
        var round = session.Round;
        return MoveRanker.Rank(round.Board, round.Hand, LexiconLoader.Enable, session.Run.DeskItems,
            round.Config.EffectiveScoring(session.Scoring), round.Config.MinWordLength,
            RoundRules.Environment(round, session.Run.Money), round.Config.CensoredLetter);
    }

    /// <summary>One deterministic transition: shop like the naive bot and leave, or play the best move (else discard all).</summary>
    private static GameSession Step(GameSession session)
    {
        var lexicon = LexiconLoader.Enable;
        if (session.Phase == RunPhase.Shop)
            return RunRules.LeaveShop(NaiveShopBot.Shop(session), lexicon).Value;
        var ranked = Rank(session);
        if (ranked.Count > 0)
            return RunRules.Submit(session, ranked[0].Play.Placed, lexicon).Value.Session;
        return RunRules.Discard(session, session.Round.Hand.Tiles.Select(t => t.Id).ToArray(), lexicon).Value;
    }
}
