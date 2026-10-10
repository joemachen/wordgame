using Crossword.Core.Clues;
using Crossword.Core.Lexicon;
using System.Collections.Immutable;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Save;
using Godot;
using GridPos = Crossword.Core.Domain.Position;

namespace Wordgame.Godot;

/// <summary>
/// --selftest: drives the real UI through Godot's input pipeline (simulated mouse/keyboard events) to check
/// click-select, drag-to-reorder (ghost slot, sliding tiles, gap drops, cancel), drag-onto-board, shuffle, hints, the Style
/// Guides popup, every Stationery item (incl. White-Out board targeting) and save/resume. Prints PASS/FAIL lines, exits
/// with code 0/1.
/// </summary>
public partial class Main
{
    private int _selfTestFailures;

    private async Task RunSelfTest()
    {
        await Frames(3);

        // 0. The sidebar shows the week, its three puzzles and how far away the boss is.
        Check("progress shows week and boss distance", _titleLabel.Text == "WEEK 1 OF 5" && _weekPips.GetChildCount() == 5
            && _dayStrip.GetChildCount() == 3 && _bossLabel.Text.StartsWith("2 puzzles until the Sunday Edition"));

        // 0b. A fresh profile fills the clue columns with newsroom tips.
        if (ShowClueColumns)
            Check("fresh profile shows editor's notes", ClueText(_acrossBox).Contains("EDITOR'S NOTES") && ClueText(_acrossBox).Contains("DEADLINE")
                && ClueText(_downBox).StartsWith("DOWN"));
        else
            Check("clue columns are hidden", !_acrossBox.IsVisibleInTree() && !_downBox.IsVisibleInTree());

        // 1. Click selects a hand tile.
        var first = HandButton(0);
        int firstId = first.TileId;
        await Click(Centre(first));
        Check("click selects tile", _selected.Any(t => t.Id == firstId));
        await Click(Centre(HandButton(0)));
        Check("second click deselects tile", _selected.Count == 0);

        // 2. Drag tile 0 onto tile 3 → a ghost marks slot 3 and the tiles in between slide left to make room.
        //    Dropping puts it there (after the old tile 3), without toggling selection.
        int targetId = HandButton(3).TileId;
        var slot2 = Centre(HandButton(2));
        await BeginDrag(Centre(HandButton(0)));
        await MoveTo(Centre(HandButton(3)));
        Check("ghost marks the drop slot", HandButton(3).TileId == firstId && HandButton(3).IsGhost);
        await Seconds(0.3);
        Check("tiles slide apart for the ghost",
            HandButton(2).TileId == targetId && Centre(HandButton(2)).DistanceTo(slot2) < 1);
        await Release();
        var order = _handOrder.ToList();
        Check("drag right reorders hand", order.IndexOf(firstId) == 3 && order.IndexOf(targetId) == 2);
        Check("drag does not select", _selected.Count == 0);
        Check("no ghost after drop", !AnyGhost());

        // 2b. Drag the last tile onto the first → it becomes first.
        int lastId = HandButton(_handRow.GetChildCount() - 1).TileId;
        await Drag(Centre(HandButton(_handRow.GetChildCount() - 1)), Centre(HandButton(0)));
        Check("drag left reorders hand", _handOrder[0] == lastId);

        // 2c. Dropping into the gap between tiles 1 and 2 lands in slot 2.
        int gapId = HandButton(0).TileId;
        var gap = (HandButton(1).GetGlobalRect().End + HandButton(2).GetGlobalRect().Position) / 2;
        await Drag(Centre(HandButton(0)), new Vector2(gap.X + 2, Centre(HandButton(0)).Y));
        Check("drop in a gap reorders hand", _handRow.GetChildren().OfType<TileButton>().ToList().FindIndex(b => b.TileId == gapId) == 2);

        // 2d. Leaving the hand sends the ghost home; letting go there changes nothing.
        var beforeCancel = _handOrder.ToList();
        int cancelId = HandButton(0).TileId;
        await BeginDrag(Centre(HandButton(0)));
        await MoveTo(Centre(HandButton(2)));
        await MoveTo(Centre(_seedLabel));
        Check("ghost returns home off the hand", HandButton(0).TileId == cancelId && HandButton(0).IsGhost);
        await Release();
        Check("cancelled drag keeps order", beforeCancel.SequenceEqual(_handOrder) && !AnyGhost());

        // 3. Drag a hand tile onto an empty board square → it becomes pending there.
        int boardTileId = HandButton(0).TileId;
        var cellPos = new GridPos(3, 3);
        await Drag(Centre(HandButton(0)), Centre(BoardCell(cellPos)));
        Check("drag onto board places tile", _pending.TryGetValue(cellPos, out var placed) && placed.Id == boardTileId);
        Check("placed tile leaves the hand row", _handRow.GetChildCount() == Round.Hand.Count - 1);

        // 4. Space shuffles the remaining arrangement.
        var before = _handOrder.ToList();
        await PressKey(global::Godot.Key.Space);
        Check("space shuffles hand", !before.SequenceEqual(_handOrder) && before.Order().SequenceEqual(_handOrder.Order()));

        // 5. Esc recalls the pending tile.
        await PressKey(global::Godot.Key.Escape);
        Check("escape recalls pending tiles", _pending.Count == 0 && _handRow.GetChildCount() == Round.Hand.Count);

        // 5b. A pending tile can be dragged to another square, then back into the hand.
        int movingId = HandButton(0).TileId;
        await Drag(Centre(HandButton(0)), Centre(BoardCell(new GridPos(3, 3))));
        await Drag(Centre(BoardCell(new GridPos(3, 3))), Centre(BoardCell(new GridPos(3, 4))));
        Check("pending tile moves to another square",
            _pending.Count == 1 && _pending.TryGetValue(new GridPos(3, 4), out var moved) && moved.Id == movingId);
        await Drag(Centre(BoardCell(new GridPos(3, 4))), Centre(HandButton(1)));
        Check("pending tile drags back into the hand", _pending.Count == 0 && _handRow.GetChildCount() == Round.Hand.Count
            && _handRow.GetChildren().OfType<TileButton>().Any(b => b.TileId == movingId));

        await Seconds(0.2); // clicks right after a drag are ignored (TileButton.RecentlyDragged)

        // 5c. A→Z sorts the hand; pressing again sorts Z→A.
        await Click(Centre(_sortButton));
        bool ascending = _handOrder.SequenceEqual(Crossword.Core.Domain.HandArrangement.Sort(Round.Hand));
        await Click(Centre(_sortButton));
        Check("sort orders the hand A→Z then Z→A",
            ascending && _handOrder.SequenceEqual(Crossword.Core.Domain.HandArrangement.Sort(Round.Hand, descending: true)));

        // 5d. Tiles drawn by a discard are marked NEW.
        var handBefore = Round.Hand.Tiles.Select(t => t.Id).ToHashSet();
        await Click(Centre(HandButton(0)));
        await Click(Centre(HandButton(1)));
        await Click(Centre(_discardButton));
        var drawn = Round.Hand.Tiles.Where(t => !handBefore.Contains(t.Id)).Select(t => t.Id).ToHashSet();
        var tagged = _handRow.GetChildren().OfType<TileButton>().Where(b => b.FindChild("NewTag", owned: false) is not null)
            .Select(b => b.TileId).ToHashSet();
        Check("new tiles are highlighted after a discard", drawn.Count == 2 && tagged.SetEquals(drawn));
        await Click(Centre(HandButton(0)));
        Check("touching the hand clears NEW tags", NewTagNodes().Count == 0 && _newTileIds.Count == 0);
        await Click(Centre(HandButton(0))); // deselect

        // 5e. Untouched, NEW tags fade away on their own.
        await Click(Centre(HandButton(0)));
        await Click(Centre(_discardButton));
        bool taggedAgain = NewTagNodes().Count > 0;
        await Seconds(Juice.NewTagSeconds + Juice.NewTagFadeSeconds + 0.3);
        Refresh();
        await Frames(2);
        Check("NEW tags fade after a few seconds", taggedAgain && NewTagNodes().Count == 0 && _newTileIds.Count == 0);

        // 5f. A wild tile shows "?" in the hand; placing it asks for a letter, and the pending tile plays as that letter.
        var wildId = HandButton(0).TileId;
        _session = _session with
        {
            Round = Round with
            {
                Hand = new Crossword.Core.Domain.Hand(Round.Hand.Tiles.Select(t => t.Id == wildId ? Crossword.Core.Domain.Tile.Wild(t.Id) : t).ToImmutableArray()),
            },
        };
        Refresh();
        await Frames(2);
        var wildButton = _handRow.GetChildren().OfType<TileButton>().First(b => b.TileId == wildId);
        bool showsBlank = wildButton.GetChildren().OfType<Label>().Any(l => l.Text == "?");
        await Click(Centre(wildButton));
        await Click(Centre(BoardCell(new GridPos(3, 3))));
        bool asked = _wildOverlay.Visible;
        await PressKey(global::Godot.Key.E);
        Check("a wild tile asks for its letter when placed", showsBlank && asked && !_wildOverlay.Visible
            && _pending.TryGetValue(new GridPos(3, 3), out var wildPlaced) && wildPlaced.Id == wildId && wildPlaced.IsWild
            && wildPlaced.Letter.Char == 'E');
        await PressKey(global::Godot.Key.Escape);

        // 6. Hint places a play; the preview defines every word it forms.
        await Click(Centre(_hintButton));
        var words = PlacementValidator.Validate(Round.Board, Round.Hand, PendingPlacement(), _lexicon, Round.Config.MinWordLength,
            Round.Config.CensoredLetter)
            .Value.Words.Select(w => w.Text).ToList();
        string defined = _definitionsLabel.GetParsedText();
        Check("preview defines each word", words.Count > 0 && words.All(w => defined.Contains(w)));
        var ranked = RankedPlays();
        var decent = Crossword.Core.Analysis.Hints.Decent(ranked)!;
        Check("hint shows a decent play, not the best",
            _pending.Count == decent.Play.Placed.Length && decent.Play.Placed.All(p => _pending.TryGetValue(p.Position, out var t) && t == p.Tile)
            && (_devMode || decent.Score.Total < ranked[0].Score.Total || ranked.Count == 1));

        // 7. Tab opens the Style Guides popup with every tier's guide and the hinted play's tier highlighted;
        //    Esc closes it without recalling the pending play.
        int pendingBefore = _pending.Count;
        await PressKey(global::Godot.Key.Tab);
        var tiers = RoundScoring.Tiers;
        string popup = string.Join("\n", _styleGuidesBox.FindChildren("*", nameof(Label), owned: false).OfType<Label>().Select(l => l.Text));
        Check("tab opens style guides", _styleGuidesOverlay.Visible
            && tiers.Select((t, i) => Crossword.Core.Run.StyleGuideNames.For(t.MinLength, t.Label(i == tiers.Length - 1))).All(popup.Contains));
        Check("style guides highlight this play's tier",
            _highlightedTier == RoundScoring.TierFor(words.Max(w => w.Length)).MinLength);
        await PressKey(global::Godot.Key.Escape);
        Check("esc closes style guides, keeps the play", !_styleGuidesOverlay.Visible && _pending.Count == pendingBefore);

        // 7b. With ×Mult before +Mult, the +Mult item's ◀ arrow previews a higher score for the pending play (green).
        _session = _session with
        {
            Run = Run with
            {
                DeskItems = [Crossword.Core.DeskItems.DeskItemCatalog.Find("pulitzer")!, Crossword.Core.DeskItems.DeskItemCatalog.Find("red-pen")!],
            },
        };
        Refresh();
        await Frames(2);
        var moveLeft = _deskRow.GetChild(1).FindChildren("*", nameof(Button), owned: false).OfType<Button>().First(b => b.Text == "◀");
        Check("desk arrow previews a better order", _deskCaption.Visible && moveLeft.TooltipText.StartsWith("Move left:")
            && moveLeft.TooltipText.Contains("(+") && moveLeft.GetThemeColor("font_color") == UiKit.Good);
        _session = _session with { Run = Run with { DeskItems = [] } };
        Refresh();

        // 8. Using an Answer Key places the best play and empties its Stationery slot.
        _session = _session with { Run = Run.AddStationery(new Crossword.Core.Stationery.AnswerKey()).Value };
        Refresh();
        await Frames(2);
        var useButton = _deskRow.FindChildren("*", nameof(Button), owned: false).OfType<Button>().First(b => b.Text == "Use");
        var best = RankedPlays()[0];
        await Click(Centre(useButton));
        Check("answer key places the best play",
            Run.Stationery.IsEmpty && _pending.Count == best.Play.Placed.Length
            && best.Play.Placed.All(p => _pending.TryGetValue(p.Position, out var t) && t == p.Tile));

        // 9. Margin Clip adds a submission and keeps the pending play.
        int submissionsBefore = Round.SubmissionsLeft;
        int pendingCount = _pending.Count;
        await GiveAndUse(new Crossword.Core.Stationery.MarginClip());
        Check("margin clip adds a submission", Round.SubmissionsLeft == submissionsBefore + 1 && _pending.Count == pendingCount);

        // 10. Red Ink Bottle raises the previewed mult by its bonus.
        decimal multBefore = decimal.Parse(_multLabel.Text);
        await GiveAndUse(new Crossword.Core.Stationery.RedInkBottle(Mult: 3));
        Check("red ink adds mult to the preview", Round.Config.BonusMult == 3 && decimal.Parse(_multLabel.Text) == multBefore + 3
            && _resourcesLabel.Text.Contains("Red ink"));
        await PressKey(global::Godot.Key.Escape);

        // 11. Scissors redraw the selected tiles without spending a discard.
        int discardsBefore = Round.DiscardsLeft;
        int[] cut = [HandButton(0).TileId, HandButton(1).TileId];
        await Click(Centre(HandButton(0)));
        await Click(Centre(HandButton(1)));
        await GiveAndUse(new Crossword.Core.Stationery.Scissors(MaxTiles: 2));
        Check("scissors redraw the selected tiles",
            cut.All(id => !Round.Hand.Contains(id)) && Round.Hand.Count == Round.Config.HandSize && Round.DiscardsLeft == discardsBefore
            && Run.Stationery.IsEmpty && _selected.Count == 0);

        // 11b. The Fountain Pen turns the selected hand tile wild.
        var inkId = Round.Hand.Tiles.First(t => !t.IsWild).Id;
        await Click(Centre(_handRow.GetChildren().OfType<TileButton>().First(b => b.TileId == inkId)));
        await GiveAndUse(new Crossword.Core.Stationery.FountainPen());
        Check("fountain pen makes the selected tile wild", Round.Hand.Tiles.First(t => t.Id == inkId).IsWild && Run.Stationery.IsEmpty);

        // 12. White-Out: Use arms board targeting, Esc cancels, clicking a board tile removes it.
        var spot = new GridPos(3, 3);
        _session = _session with
        {
            Round = Round with { Board = Round.Board.Place([new(spot, new Crossword.Core.Domain.Tile(9000, Crossword.Core.Domain.Letter.From('A')))]) },
        };
        await GiveAndUse(new Crossword.Core.Stationery.WhiteOut());
        Check("white-out arms board targeting", _whiteOutSlot == 0 && BoardCell(spot) is BaseButton { Disabled: false });
        await PressKey(global::Godot.Key.Escape);
        Check("esc cancels white-out", _whiteOutSlot is null && Round.Board.IsOccupied(spot) && Run.Stationery.Length == 1);
        await Click(Centre(UseButton()));
        await Click(Centre(BoardCell(spot)));
        Check("white-out removes the clicked tile", !Round.Board.IsOccupied(spot) && Run.Stationery.IsEmpty && _whiteOutSlot is null);

        // 13. Submitting rings the score up: the Desk Item that fired pops, the finish plays, then input unlocks.
        _session = _session with { Run = Run with { DeskItems = [Crossword.Core.DeskItems.DeskItemCatalog.Find("red-pen")!] } };
        Refresh();
        var submitted = RankedPlays()[0];
        PlacePlay(submitted, null);
        int popsBefore = _deskPops;
        await PressKey(global::Godot.Key.Enter);
        bool animatingAfterSubmit = _animating;
        for (int i = 0; i < 100 && _animating; i++)
            await Seconds(0.1);
        Check("scoring pops the desk item and finishes", animatingAfterSubmit && _deskPops > popsBefore
            && _lastCelebration is not null && !_animating);

        // 13b. The submitted words appear in the clue columns, numbered like a crossword, and their record clues too.
        var numbered = BoardWords.Numbered(Round.Board);
        if (ShowClueColumns)
            Check("clue columns list the board's words", numbered.Length > 0
            && numbered.All(w => ClueText(w.Direction == Crossword.Core.Domain.Direction.Across ? _acrossBox : _downBox).Contains($"{w.Number}  {w.Text}"))
            && ClueText(_downBox).Contains("FROM THE MORGUE"));

        // 14. The play lands in the (in-memory) profile, and the Stats popup lists its words; Esc closes it.
        var playedWords = submitted.Play.Words.Select(w => w.Text).ToList();
        await Click(Centre(_statsButton));
        string statsText = string.Join("\n", _statsBox.FindChildren("*", nameof(Label), owned: false).OfType<Label>().Select(l => l.Text));
        Check("stats popup lists the played words", _statsOverlay.Visible && _profile.Profile.Stats.PlaysRecorded == 1
            && playedWords.All(w => _profile.Profile.Stats.Words.ContainsKey(w) && statsText.Contains(w)));
        await PressKey(global::Godot.Key.Escape);
        Check("esc closes stats", !_statsOverlay.Visible);

        // 15. Save & resume through a real file: a fresh run, then resuming, restores the run and hand order exactly
        //     without recording stats twice; a corrupt save is set aside with a notice.
        string savePath = ProjectSettings.GlobalizePath("user://saves/__selftest.json");
        var disk = RunSaveStore.AtPath(savePath);
        if (_session.Phase == Crossword.Core.Run.RunPhase.Shop)
        {
            _session = Crossword.Core.Run.RunRules.LeaveShop(_session, _lexicon).Value;
            Refresh();
        }
        var savedSession = _session;
        var savedOrder = Crossword.Core.Domain.HandArrangement.Reconcile(_handOrder, Round.Hand).Reverse().ToArray();
        disk.Save(_session, savedOrder);
        var statsBefore = _profile.Profile.Stats;
        NewRun(7);
        bool replaced = !ReferenceEquals(_session, savedSession);
        if (disk.Load(_baseConfig) is { } resumed)
            Resume(resumed);
        await Frames(2);
        var shownOrder = Enumerable.Range(0, _handRow.GetChildCount()).Select(i => HandButton(i).TileId).ToArray();
        static string json(Crossword.Core.Run.GameSession g) => Crossword.Core.Save.RunSaveJson.Serialize(g);
        Check("resume restores the saved run and hand order", replaced && json(_session) == json(savedSession)
            && shownOrder.SequenceEqual(savedOrder) && _messageLabel.Text.StartsWith("Resumed your run"));
        Check("resume doesn't count stats twice", _profile.Profile.Stats.PlaysRecorded == statsBefore.PlaysRecorded
            && _profile.Profile.Stats.RunsStarted == statsBefore.RunsStarted + 1 && _profile.Profile.Stats.RunsWon == statsBefore.RunsWon);
        File.WriteAllText(savePath, "not a save");
        var corrupt = disk.Load(_baseConfig);
        string saveDir = Path.GetDirectoryName(savePath)!;
        var backups = Directory.GetFiles(saveDir, "__selftest.json.*.bak");
        Check("a corrupt save is set aside with a notice", corrupt is null && disk.Notice is not null && !File.Exists(savePath) && backups.Length > 0);
        foreach (var file in backups)
            File.Delete(file);

        // 16. Abandoning a run in progress takes a second click on New run.
        var runBefore = _session;
        await Click(Centre(_newRunButton));
        bool armed = ReferenceEquals(_session, runBefore) && _newRunButton.Text.StartsWith("Abandon");
        await Click(Centre(_newRunButton));
        bool pickerOpened = _pressRunOverlay.Visible && ReferenceEquals(_session, runBefore);
        await Click(Centre((Control)_pressRunBox.FindChild("PressRun1", owned: false)!));
        Check("new run asks before abandoning a run", armed && !ReferenceEquals(_session, runBefore) && _newRunButton.Text == "New run");
        Check("with only Proofreader unlocked, new run still opens the picker", pickerOpened && !_pressRunOverlay.Visible
            && Run.PressRun == 1 && !Run.Seeded);

        // 17. Press Runs: winning a run unlocks the next one (shown on the victory screen); New run then asks which to
        //     play (locked ones greyed); Censored Press crosses out and refuses its letter; Final Print Run adds a rule
        //     to the boss.
        _session = _session with { Phase = Crossword.Core.Run.RunPhase.Victory };
        Refresh();
        await Frames(2);
        Check("a win unlocks the next press run", _profile.Profile.Stats.HighestPressRunWon.GetValueOrDefault(Decks.StandardId) == 1
            && _endScreenUnlock is { } unlockLine && unlockLine.Text.StartsWith("Unlocked Press Run 2"));
        Check("a win unlocks the next deck", _endScreenDeckUnlock is { } deckLine
            && deckLine.Text.StartsWith($"Unlocked {Decks.Get(Decks.CrosswordDraftId).Name}"));
        var endNewRun = _shopContent.FindChildren("*", nameof(Button), owned: false).OfType<Button>().First(b => b.Text == "New run");
        await Click(Centre(endNewRun));
        Button PressRow(int level) => _pressRunBox.FindChild($"PressRun{level}", owned: false) as Button ?? throw new InvalidOperationException($"No PressRun{level}");
        Button DeckCard(string id) => _pressRunBox.FindChild($"Deck_{id}", owned: false) as Button ?? throw new InvalidOperationException($"No Deck_{id}");
        Check("new run opens the press run picker", _pressRunOverlay.Visible && !PressRow(1).Disabled && !PressRow(2).Disabled
            && PressRow(3).Disabled && PressRow(8).Disabled);
        Check("the picker shows the unlocked decks", !DeckCard(Decks.StandardId).Disabled && !DeckCard(Decks.CrosswordDraftId).Disabled
            && DeckCard(Decks.RedactorId).Disabled && DeckCard(Decks.CopyEditorId).Disabled);
        await Click(Centre(PressRow(2)));
        Check("picking a press run starts it", !_pressRunOverlay.Visible && Run.PressRun == 2 && _seedLabel.Text.Contains("First Edition"));
        ChooseNewRun();
        await PressKey(global::Godot.Key.Escape);
        Check("esc closes the press run picker", !_pressRunOverlay.Visible && Run.PressRun == 2);

        // 17b. The seed box: a typed seed starts a seeded run (junk keeps the picker open), and its win unlocks nothing.
        ChooseNewRun();
        await Frames(2);
        await Click(Centre((Control)_pressRunBox.FindChild("SeedField", owned: false)!));
        await TypeText("4x");
        await Click(Centre(PressRow(1)));
        Check("a seed that isn't a number keeps the picker open", _pressRunOverlay.Visible && _pickerSeedError is { Text.Length: > 0 });
        await PressKey(global::Godot.Key.Backspace);
        await TypeText("2");
        await Click(Centre(PressRow(1)));
        Check("a typed seed starts a seeded run", !_pressRunOverlay.Visible && Run.Seed == 42 && Run.Seeded
            && _seedLabel.Text.Contains("Seed 42 (chosen)"));
        int winsBefore = _profile.Profile.Stats.RunsWon;
        int decksBefore = Crossword.Core.Profile.StatsQueries.UnlockedDecks(_profile.Profile.Stats).Length;
        _session = _session with { Phase = Crossword.Core.Run.RunPhase.Victory };
        Refresh();
        await Frames(2);
        Check("a seeded win unlocks nothing", _profile.Profile.Stats.RunsWon == winsBefore && _profile.Profile.Stats.SeededRunsWon == 1
            && Crossword.Core.Profile.StatsQueries.UnlockedDecks(_profile.Profile.Stats).Length == decksBefore
            && _justUnlockedDeck is null && _justUnlockedPressRun is null && _shopContent.FindChild("SeededNote", owned: false) is not null);

        // 18. Decks: picking one shows its own Press Run ladder (unlocks are per deck) and starts a run by its rules.
        ChooseNewRun();
        await Frames(2);
        await Click(Centre(DeckCard(Decks.CrosswordDraftId)));
        Check("press runs are unlocked per deck", _pressRunOverlay.Visible && !PressRow(1).Disabled && PressRow(2).Disabled);
        await Click(Centre(PressRow(1)));
        Check("picking a deck starts a run with its rules", !_pressRunOverlay.Visible && Run.DeckId == Decks.CrosswordDraftId
            && Round.Config.MinWordLength == 3 && _seedLabel.Text.Contains("Crossword Draft"));
        NewRun(42, deck: Decks.CopyEditorId);
        await Frames(2);
        int emptySlots = _deskRow.FindChildren("*", nameof(Label), owned: false).OfType<Label>().Count(l => l.Text == "empty desk slot");
        Check("the copy editor's deck has 4 desk slots and a red pen", Run.DeskItems.Length == 1 && Run.DeskItems[0].Id == "red-pen"
            && emptySlots == 3);

        NewRun(42, pressRun: 7);
        await Frames(2);
        char censored = Round.Config.CensoredLetter ?? '_';
        var plain = Round.Hand.Tiles.First(t => !t.IsWild);
        _session = _session with { Round = Round with { Hand = new Crossword.Core.Domain.Hand(Round.Hand.Tiles.Replace(plain, plain with { Letter = Crossword.Core.Domain.Letter.From(censored == '_' ? 'R' : censored) })) } };
        Refresh();
        await Frames(2);
        var censoredButton = _handRow.GetChildren().OfType<TileButton>().First(b => b.TileId == plain.Id);
        Check("censored press marks its letter", censored != '_' && _resourcesLabel.Text.Contains($"Censored: {censored}")
            && censoredButton.HasNode("CensoredStrike"));
        await Click(Centre(censoredButton));
        await Click(Centre(BoardCell(new GridPos(3, 3))));
        Check("a censored tile can't be placed", _pending.Count == 1 && _messageLabel.Text.Contains("censored"));
        await PressKey(global::Godot.Key.Escape);

        NewRun(42, run => run with { RoundIndex = 2 }, pressRun: 8);
        await Frames(2);
        string dayText = string.Join("\n", _dayStrip.FindChildren("*", nameof(Label), owned: false).OfType<Label>().Select(l => l.Text));
        Check("final print run adds a rule to the boss", Round.Config.Boss is Crossword.Core.Run.Reprint reprint
            && reprint.Name.Contains(" + ") && dayText.Contains(reprint.Name));

        // 19. The Lexicographer's Deck: the picker offers its dictionary, the run's words include it (an acronym
        //     previews as valid with its expansion), and resuming keeps it.
        _profile.Update(s => s with { RunsWon = Decks.All.IndexOf(Decks.Get(Decks.LexicographerId)) });
        ChooseNewRun();
        await Frames(2);
        await Click(Centre(DeckCard(Decks.LexicographerId)));
        var dictionaryButton = _pressRunBox.FindChild($"Dictionary_{Dictionaries.TechShorthandId}", owned: false) as Button;
        Check("the lexicographer's deck offers its dictionary", dictionaryButton is { Disabled: false });
        await Click(Centre(PressRow(1)));
        emptySlots = _deskRow.FindChildren("*", nameof(Label), owned: false).OfType<Label>().Count(l => l.Text == "empty desk slot");
        Check("picking it starts a run with the dictionary", Run.DeckId == Decks.LexicographerId
            && Run.Dictionaries.SequenceEqual([Dictionaries.TechShorthandId]) && _seedLabel.Text.Contains("The Tech Shorthand")
            && emptySlots == DeckConfig.Default.LexicographerDeskSlots && _lexicon.Contains("CPU"));
        var acronymIds = Round.Hand.Tiles.Take(3).Select(t => t.Id).ToList();
        var spelled = Round.Hand.Tiles.Select(t => acronymIds.IndexOf(t.Id) is var i and >= 0
            ? t with { Letter = Crossword.Core.Domain.Letter.From("CPU"[i]), IsWild = false } : t);
        _session = _session with { Round = Round with { Hand = new Crossword.Core.Domain.Hand(spelled.ToImmutableArray()) } };
        Refresh();
        await Frames(2);
        for (int i = 0; i < acronymIds.Count; i++)
        {
            await Click(Centre(_handRow.GetChildren().OfType<TileButton>().First(b => b.TileId == acronymIds[i])));
            await Click(Centre(BoardCell(new GridPos(3, 2 + i))));
        }
        string acronymText = _definitionsLabel.GetParsedText();
        Check("an acronym previews as valid with its expansion", _pending.Count == 3 && acronymText.Contains("CPU")
            && acronymText.Contains("central processing unit"));
        await PressKey(global::Godot.Key.Escape);
        Resume(RunSaveJson.Deserialize(RunSaveJson.Serialize(_session, _handOrder), _baseConfig).Value);
        Check("resuming keeps the dictionary", Run.Dictionaries.SequenceEqual([Dictionaries.TechShorthandId]) && _lexicon.Contains("CPU"));
        NewRun(42);
        Check("a standard run doesn't take the dictionary's words", Run.Dictionaries.IsEmpty && !_lexicon.Contains("CPU"));

        // 20. One more win unlocks The Atlas Unlocked; picking it swaps the run's extra words.
        _profile.Update(s => s with { RunsWon = s.RunsWon + 1 });
        ChooseNewRun();
        await Frames(2);
        await Click(Centre(DeckCard(Decks.LexicographerId)));
        if (_pressRunBox.FindChild($"Dictionary_{Dictionaries.AtlasId}", owned: false) is Button { Disabled: false } atlasButton)
            await Click(Centre(atlasButton));
        await Click(Centre(PressRow(1)));
        Check("a win unlocks the atlas and picking it starts a run with place names",
            Run.Dictionaries.SequenceEqual([Dictionaries.AtlasId]) && _lexicon.Contains("OSLO") && !_lexicon.Contains("CPU")
            && _seedLabel.Text.Contains("The Atlas Unlocked"));

        // 21. One more win unlocks The Olde English Folio: an archaic word previews with its gloss and its bonus.
        _profile.Update(s => s with { RunsWon = s.RunsWon + 1 });
        ChooseNewRun();
        await Frames(2);
        await Click(Centre(DeckCard(Decks.LexicographerId)));
        if (_pressRunBox.FindChild($"Dictionary_{Dictionaries.OldeFolioId}", owned: false) is Button { Disabled: false } folioButton)
            await Click(Centre(folioButton));
        await Click(Centre(PressRow(1)));
        var theeIds = Round.Hand.Tiles.Take(4).Select(t => t.Id).ToList();
        var thee = Round.Hand.Tiles.Select(t => theeIds.IndexOf(t.Id) is var i and >= 0
            ? t with { Letter = Crossword.Core.Domain.Letter.From("THEE"[i]), IsWild = false } : t);
        _session = _session with { Round = Round with { Hand = new Crossword.Core.Domain.Hand(thee.ToImmutableArray()) } };
        Refresh();
        await Frames(2);
        for (int i = 0; i < theeIds.Count; i++)
        {
            await Click(Centre(_handRow.GetChildren().OfType<TileButton>().First(b => b.TileId == theeIds[i])));
            await Click(Centre(BoardCell(new GridPos(3, 2 + i))));
        }
        string folioText = _definitionsLabel.GetParsedText();
        Check("an archaic word previews with its gloss and its bonus", Run.Dictionaries.SequenceEqual([Dictionaries.OldeFolioId])
            && _pending.Count == 4 && folioText.Contains("arch. you") && folioText.Contains($"+{Dictionaries.OldeFolioMult} mult"));
        await PressKey(global::Godot.Key.Escape);

        GD.Print(_selfTestFailures == 0 ? "SELFTEST: ALL PASSED" : $"SELFTEST: {_selfTestFailures} FAILED");
        GetTree().Quit(_selfTestFailures == 0 ? 0 : 1);
    }

    private void Check(string name, bool ok)
    {
        if (!ok)
            _selfTestFailures++;
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {name}");
    }

    private TileButton HandButton(int index) => _handRow.GetChild<TileButton>(index);

    private Button UseButton() => _deskRow.FindChildren("*", nameof(Button), owned: false).OfType<Button>().First(b => b.Text == "Use");

    /// <summary>Puts a Stationery item in the first free slot and clicks its Use button.</summary>
    private async Task GiveAndUse(Crossword.Core.Stationery.IStationery item)
    {
        _session = _session with { Run = Run.AddStationery(item).Value };
        Refresh();
        await Frames(2);
        await Click(Centre(UseButton()));
    }

    /// <summary>The board grid is rebuilt on refresh; find the control at a grid position.</summary>
    private Control BoardCell(GridPos pos)
    {
        var grid = _boardHolder.GetChild(0).GetChild<GridContainer>(0);
        return grid.GetChild<Control>(pos.Row * Round.Board.Size + pos.Col);
    }

    private static Vector2 Centre(Control control) => control.GetGlobalRect().GetCenter();

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e, inLocalCoords: true);

    private InputEventMouseButton Button(Vector2 at, bool pressed) => new()
    {
        Position = at,
        GlobalPosition = at,
        ButtonIndex = MouseButton.Left,
        Pressed = pressed,
        ButtonMask = pressed ? MouseButtonMask.Left : 0,
    };

    private async Task Click(Vector2 at)
    {
        Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Push(Button(at, pressed: true));
        await Frames(1);
        Push(Button(at, pressed: false));
        await Frames(3);
    }

    private Vector2 _dragAt;

    private async Task Drag(Vector2 from, Vector2 to)
    {
        await BeginDrag(from);
        await MoveTo(to);
        await Release();
    }

    private async Task BeginDrag(Vector2 from)
    {
        Push(new InputEventMouseMotion { Position = from, GlobalPosition = from });
        Push(Button(from, pressed: true));
        _dragAt = from;
        await Frames(1);
    }

    private async Task MoveTo(Vector2 to)
    {
        var from = _dragAt;
        for (int step = 1; step <= 12; step++)
        {
            var at = from.Lerp(to, step / 12f);
            Push(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = at - _dragAt, ButtonMask = MouseButtonMask.Left });
            _dragAt = at;
            await Frames(1);
        }
    }

    private async Task Release()
    {
        Push(Button(_dragAt, pressed: false));
        await Frames(3);
    }

    private async Task Seconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private bool AnyGhost() => _handRow.GetChildren().OfType<TileButton>().Any(b => b.IsGhost);

    /// <summary>Types letters and digits into whatever has keyboard focus.</summary>
    private async Task TypeText(string text)
    {
        foreach (char c in text)
        {
            var key = char.IsDigit(c)
                ? (global::Godot.Key)((long)global::Godot.Key.Key0 + (c - '0'))
                : (global::Godot.Key)((long)global::Godot.Key.A + (char.ToUpperInvariant(c) - 'A'));
            Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = c, Pressed = true });
            Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        }
        await Frames(3);
    }

    private async Task PressKey(global::Godot.Key key)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(3);
    }
}
