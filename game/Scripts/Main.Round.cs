using Crossword.Core.Clues;
using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Scoring;
using Godot;
using GridPos = Crossword.Core.Domain.Position;

namespace Wordgame.Godot;

public partial class Main
{
    private const float CellSize = 66;
    private const float HandTileSize = 72;

    private Button _shuffleButton = null!;
    private Button _sortButton = null!;

    private ScoringConfig RoundScoring => Round.Config.EffectiveScoring(_session.Scoring);

    /// <summary>Appends what the tile's enhancement does to a tooltip (nothing for a plain tile).</summary>
    private string WithEnhancementTip(string tooltip, Tile tile) =>
        tile.Enhancement == TileEnhancement.None ? tooltip
        : tooltip.Length == 0 ? RoundScoring.Describe(tile.Enhancement)
        : $"{tooltip}\n{RoundScoring.Describe(tile.Enhancement)}";

    private Control BuildRoundArea()
    {
        var box = UiKit.VBox(16);

        // The board sits between its ACROSS and DOWN clue columns, like a printed crossword.
        var boardRow = UiKit.HBox(12);
        boardRow.SizeFlagsVertical = SizeFlags.ExpandFill;
        boardRow.AddChild(BuildClueColumn(out _acrossBox));
        _boardHolder = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        boardRow.AddChild(_boardHolder);
        boardRow.AddChild(BuildClueColumn(out _downBox));
        box.AddChild(boardRow);

        var handHolder = new CenterContainer();
        _handRow = new HandRow { CanAcceptDrop = () => _handDragId is not null || _boardDragId is not null, Dropped = DropOnHand };
        _handRow.AddThemeConstantOverride("separation", 10);
        handHolder.AddChild(_handRow);
        box.AddChild(handHolder);

        var buttonsHolder = new CenterContainer();
        var buttons = UiKit.HBox(12);
        _submitButton = UiKit.MakeButton("Submit  ⏎", UiKit.Good.Darkened(0.25f), 20);
        _submitButton.Pressed += Submit;
        _recallButton = UiKit.MakeButton("Recall  Esc", UiKit.PanelRaised, 18);
        _recallButton.Pressed += Recall;
        _discardButton = UiKit.MakeButton("Discard selected", UiKit.Mult.Darkened(0.3f), 18);
        _discardButton.Pressed += Discard;
        _hintButton = UiKit.MakeButton("Hint", UiKit.PanelRaised, 18);
        _hintButton.Pressed += Hint;
        _bestButton = UiKit.MakeButton("Best (dev)", UiKit.PanelRaised, 18);
        _bestButton.TooltipText = "Dev only: place the highest-scoring play";
        _bestButton.Visible = _devMode;
        _bestButton.Pressed += () => ShowHint(best: true);
        _shuffleButton = UiKit.MakeButton("Shuffle  Space", UiKit.PanelRaised, 18);
        _shuffleButton.Pressed += ShuffleHand;
        _sortButton = UiKit.MakeButton("A→Z", UiKit.PanelRaised, 18);
        _sortButton.TooltipText = "Sort the hand alphabetically (press again to reverse)";
        _sortButton.Pressed += SortHand;
        foreach (var b in new[] { _submitButton, _recallButton, _discardButton, _shuffleButton, _sortButton, _hintButton, _bestButton })
            buttons.AddChild(b);
        buttonsHolder.AddChild(buttons);
        box.AddChild(buttonsHolder);
        return box;
    }

    // ---------------------------------------------------------------- board & hand

    private void RefreshBoard()
    {
        UiKit.ClearChildren(_boardHolder);
        _clueNumbers = ShowClueColumns
            ? BoardWords.Numbered(Round.Board).GroupBy(w => w.Start).ToDictionary(g => g.Key, g => g.First().Number)
            : new();
        var frame = UiKit.MakePanel(UiKit.Panel, padding: 10, radius: 12);
        var grid = new GridContainer { Columns = Round.Board.Size };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        frame.AddChild(grid);
        _boardHolder.AddChild(frame);

        var scoring = RoundScoring;
        for (int r = 0; r < Round.Board.Size; r++)
        {
            for (int c = 0; c < Round.Board.Size; c++)
            {
                var pos = new GridPos(r, c);
                grid.AddChild(MakeCell(pos, scoring));
            }
        }
    }

    private Control MakeCell(GridPos pos, ScoringConfig scoring)
    {
        var board = Round.Board;
        if (board.TileAt(pos) is { } placed)
        {
            bool target = _whiteOutSlot is not null;
            var tile = UiKit.MakeTile(placed, scoring.ValueOf(placed), CellSize, target ? StationeryColor.Lightened(0.55f) : UiKit.Newsprint,
                raised: target);
            tile.Disabled = !target;
            if (_clueNumbers.TryGetValue(pos, out int number))
            {
                var label = UiKit.MakeLabel(number.ToString(), 11, new Color("5b5545"), HorizontalAlignment.Right);
                label.Name = "ClueNumber";
                label.SetAnchorsPreset(LayoutPreset.FullRect);
                label.OffsetRight = -4;
                label.OffsetTop = 1;
                label.MouseFilter = MouseFilterEnum.Ignore;
                tile.AddChild(label);
            }
            if (target)
            {
                tile.TooltipText = "Click to white out";
                tile.Pressed += () => WhiteOutCell(pos);
            }
            else
                tile.TooltipText = WithEnhancementTip("", placed);
            return tile;
        }

        if (_pending.TryGetValue(pos, out var pending))
        {
            int value = scoring.ValueOf(pending);
            var tile = UiKit.MakeTile(pending, value, CellSize, UiKit.Pending, raised: true);
            tile.TooltipText = WithEnhancementTip("Click to take back · drag to another square or back to your hand", pending);
            tile.Pressed += () => ReturnPending(pos);
            if (!_animating)
            {
                tile.DragPreviewFactory = () => UiKit.MakeTile(pending, value, CellSize, UiKit.Selected, raised: true);
                tile.DragStarted = id => _boardDragId = id;
            }
            return tile;
        }

        var cell = new DropCell { CustomMinimumSize = new Vector2(CellSize, CellSize), FocusMode = FocusModeEnum.None };
        if (board.IsBlocked(pos))
        {
            cell.AddThemeStyleboxOverride("normal", UiKit.Box(UiKit.Blocked, 4));
            cell.AddThemeStyleboxOverride("disabled", UiKit.Box(UiKit.Blocked, 4));
            cell.Disabled = true;
            return cell;
        }

        var premium = board.PremiumAt(pos);
        var color = UiKit.PremiumColor(premium);
        cell.Text = UiKit.PremiumText(premium);
        cell.AddThemeFontSizeOverride("font_size", UiKit.FontSize(18));
        cell.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.85f));
        cell.AddThemeColorOverride("font_hover_color", Colors.White);
        cell.AddThemeStyleboxOverride("normal", UiKit.Box(color, 4));
        cell.AddThemeStyleboxOverride("hover", UiKit.Box(color.Lightened(0.25f), 4, UiKit.Selected, 2));
        cell.AddThemeStyleboxOverride("pressed", UiKit.Box(color.Darkened(0.1f), 4));
        cell.Pressed += () => PlaceSelected(pos);
        cell.TileDropped = id => PlaceTile(pos, id);
        return cell;
    }

    private void RefreshHand()
    {
        UiKit.ClearChildren(_handRow);
        var scoring = RoundScoring;
        _handOrder = HandArrangement.Reconcile(_handOrder, Round.Hand);
        var byId = Round.Hand.Tiles.ToDictionary(t => t.Id);
        var pendingIds = _pending.Values.Select(t => t.Id).ToHashSet();
        foreach (int id in _handOrder)
        {
            var tile = byId[id];
            if (pendingIds.Contains(id))
                continue;

            bool selected = _selected.Contains(tile);
            int value = scoring.ValueOf(tile);
            var button = UiKit.MakeTile(tile, value, HandTileSize, selected ? UiKit.Selected : UiKit.Newsprint, raised: selected, blankWild: true);
            button.TooltipText = WithEnhancementTip("Click to select · drag to reorder or onto the board", tile);
            if (!tile.IsWild && tile.Letter.Char == Round.Config.CensoredLetter)
            {
                UiKit.MarkCensored(button);
                button.TooltipText = WithEnhancementTip($"{tile.Letter} is censored this round: it can't be placed. Discard it or cut it with Scissors.", tile);
            }
            button.Pressed += () => ToggleSelected(tile);
            if (!_animating)
            {
                button.DragPreviewFactory = () => UiKit.MakeTile(tile, value, HandTileSize, UiKit.Selected, raised: true, blankWild: true);
                button.DragStarted = BeginHandDrag;
                button.CanAcceptDrop = () => _handDragId is not null || _boardDragId is not null;
                button.Dropped = DropOnHand;
            }
            _handRow.AddChild(button);
            if (_newTilesRound == Run.RoundIndex && _newTileIds.Contains(id))
            {
                UiKit.MarkNew(button);
                if (_animatedNewTiles.Add(id))
                    UiKit.DropIn(button, HandTileSize);
            }
        }
    }

    private void RefreshButtons()
    {
        bool idle = !_animating && _session.Phase == RunPhase.InRound;
        _submitButton.Disabled = !idle || _pending.Count == 0;
        _recallButton.Disabled = !idle || _pending.Count == 0;
        _discardButton.Disabled = !idle || _selected.Count == 0 || Round.DiscardsLeft == 0;
        _discardButton.Text = _selected.Count > 0 ? $"Discard {_selected.Count}" : "Discard selected";
        _hintButton.Disabled = !idle;
        _bestButton.Disabled = !idle;
        _shuffleButton.Disabled = !idle || Round.Hand.Count < 2;
        _sortButton.Disabled = !idle || Round.Hand.Count < 2;
    }

    // ---------------------------------------------------------------- interaction

    private void ToggleSelected(Tile tile)
    {
        if (_animating || TileButton.RecentlyDragged)
            return;
        DismissNewTags();
        if (!_selected.Remove(tile))
            _selected.Add(tile);
        Refresh();
    }

    private void PlaceSelected(GridPos pos)
    {
        if (_animating)
            return;
        if (_selected.Count == 0)
        {
            SetMessage("Pick a tile from your hand first (click it or type its letter).", UiKit.TextMuted);
            return;
        }
        DismissNewTags();
        var tile = _selected[0];
        if (tile.IsWild)
        {
            AskWildLetter(pos, tile);
            return;
        }
        _selected.RemoveAt(0);
        _pending[pos] = tile;
        Refresh();
    }

    /// <summary>Drag-and-drop placement: puts a hand tile, or a pending tile from another square, on an empty square.</summary>
    private void PlaceTile(GridPos pos, int tileId)
    {
        if (_animating || Round.Hand.Tiles.FirstOrDefault(t => t.Id == tileId) is not { } held)
            return;
        DismissNewTags();
        // A pending tile keeps its face when moved (a wild keeps its letter); a wild from the hand asks for one.
        var tile = _pending.Values.FirstOrDefault(t => t.Id == tileId) ?? held;
        if (tile.IsWild && !_pending.Values.Any(t => t.Id == tileId))
        {
            AskWildLetter(pos, tile);
            return;
        }
        foreach (var moved in _pending.Where(kv => kv.Value.Id == tileId).Select(kv => kv.Key).ToList())
            _pending.Remove(moved);
        _selected.Remove(tile);
        _pending[pos] = tile;
        Refresh();
    }

    /// <summary>A pending tile dragged onto the hand goes back into the hand at the slot under the cursor.</summary>
    private void ReturnDraggedPending(int tileId)
    {
        foreach (var pos in _pending.Where(kv => kv.Value.Id == tileId).Select(kv => kv.Key).ToList())
            _pending.Remove(pos);
        var visible = _handRow.GetChildren().OfType<TileButton>().Select(b => b.TileId).ToList();
        if (visible.Count > 0)
        {
            var rect = _handRow.GetGlobalRect();
            int index = Mathf.Clamp((int)Mathf.Round((_cursor.X - rect.Position.X) / (rect.Size.X / visible.Count)), 0, visible.Count);
            _handOrder = index < visible.Count
                ? HandArrangement.Move(_handOrder, tileId, visible[index], after: false)
                : HandArrangement.Move(_handOrder, tileId, visible[^1], after: true);
        }
        Refresh();
    }

    // ---------------------------------------------------------------- hand drag (ghost slot)

    /// <summary>The dragged tile stays in the row as a ghost marking where it will land.</summary>
    private void BeginHandDrag(int tileId)
    {
        if (_animating || HandTileButton(tileId) is not { } ghost)
            return;
        DismissNewTags();
        _handDragId = tileId;
        _ghostHome = ghost.GetIndex();
        ghost.SetGhost(true);
        UpdateHandGhost();
    }

    /// <summary>
    /// Moves the ghost to the slot under the cursor (or back home when the cursor leaves the hand). The row always
    /// holds the same equal-width slots, so the slot under the cursor doesn't shift as the ghost moves.
    /// </summary>
    private void UpdateHandGhost()
    {
        if (_handDragId is not { } id || HandTileButton(id) is not { } ghost)
            return;
        int count = _handRow.GetChildCount();
        var rect = _handRow.GetGlobalRect();
        int index = _ghostHome;
        if (rect.HasPoint(_cursor))
        {
            float separation = _handRow.GetThemeConstant("separation");
            float slot = (rect.Size.X + separation) / count;
            index = Mathf.Clamp((int)Mathf.Floor((_cursor.X - rect.Position.X + separation / 2) / slot), 0, count - 1);
        }
        _handRow.MoveChildAnimated(ghost, index);
    }

    /// <summary>A hand tile dropped on the hand takes the ghost's slot; a pending board tile returns to the hand.</summary>
    private void DropOnHand(int tileId)
    {
        if (!_animating && _boardDragId == tileId)
        {
            _boardDragId = null;
            ReturnDraggedPending(tileId);
            return;
        }
        if (_animating || _handDragId != tileId)
            return;
        UpdateHandGhost();
        var visible = _handRow.GetChildren().OfType<TileButton>().Select(b => b.TileId).ToList();
        int index = visible.IndexOf(tileId);
        _handDragId = null;
        if (index >= 0 && visible.Count > 1)
        {
            _handOrder = index > 0
                ? HandArrangement.Move(_handOrder, tileId, visible[index - 1], after: true)
                : HandArrangement.Move(_handOrder, tileId, visible[1], after: false);
        }
        Refresh();
    }

    private TileButton? HandTileButton(int tileId) =>
        _handRow.GetChildren().OfType<TileButton>().FirstOrDefault(b => b.TileId == tileId);

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouse mouse)
            return;
        _cursor = mouse.Position;
        if (_handDragId is not null && mouse is InputEventMouseMotion)
            UpdateHandGhost();
    }

    public override void _Notification(int what)
    {
        // Closing the window saves the run (also catching the latest hand arrangement).
        if (what == NotificationWMCloseRequest && _session is not null)
            PersistRun();
        // Fired after any drop has been handled. A drag that didn't land on the hand (cancelled, or placed on the
        // board) leaves its ghost behind: rebuild the row. Deferred so no nodes are freed mid-propagation.
        if (what == NotificationDragEnd)
            _boardDragId = null;
        if (what == NotificationDragEnd && _handDragId is not null)
        {
            _handDragId = null;
            Callable.From(RefreshHand).CallDeferred();
        }
    }

    private void ShuffleHand()
    {
        if (_animating)
            return;
        DismissNewTags();
        (_handOrder, _arrangementRng) = HandArrangement.Shuffle(HandArrangement.Reconcile(_handOrder, Round.Hand), _arrangementRng);
        Refresh();
    }

    private void SortHand()
    {
        if (_animating)
            return;
        DismissNewTags();
        _handOrder = HandArrangement.Sort(Round.Hand, _sortDescending);
        _sortDescending = !_sortDescending;
        _sortButton.Text = _sortDescending ? "Z→A" : "A→Z";
        Refresh();
    }

    private void ReturnPending(GridPos pos)
    {
        if (_animating || TileButton.RecentlyDragged)
            return;
        _pending.Remove(pos);
        Refresh();
    }

    private void Recall()
    {
        _pending.Clear();
        Refresh();
    }

    private IReadOnlyList<PlacedTile> PendingPlacement() =>
        _pending.Select(kv => new PlacedTile(kv.Key, kv.Value)).ToList();

    /// <summary>Validates and scores the pending placement without committing it.</summary>
    private void UpdatePreview()
    {
        _definitionsLabel.Text = "";
        _previewTier = null;
        if (_pending.Count == 0)
        {
            _chipsLabel.Text = "0";
            _multLabel.Text = "0";
            return;
        }

        var validation = PlacementValidator.Validate(Round.Board, Round.Hand, PendingPlacement(), _lexicon, Round.Config.MinWordLength,
            Round.Config.CensoredLetter);
        if (!validation.IsOk)
        {
            _chipsLabel.Text = "–";
            _multLabel.Text = "–";
            SetMessage(validation.Error.Message, UiKit.Bad);
            return;
        }

        var score = ScoringEngine.Score(validation.Value, Run.DeskItems, RoundScoring, RoundRules.Environment(Round, Run.Money));
        _chipsLabel.Text = score.Chips.ToString("N0");
        _multLabel.Text = score.Mult.ToString("0.##");
        SetMessage($"{string.Join(" + ", validation.Value.Words.Select(w => w.Text))}  →  {score.Total:N0} points", UiKit.Good);
        _definitionsLabel.Text = DefinitionsText(validation.Value.Words.Select(w => w.Text));
        _previewTier = RoundScoring.TierFor(validation.Value.Words.Max(w => w.Text.Length)).MinLength;
    }

    /// <summary>
    /// A word's definition: WordNet's, else a dictionary overlay's expansion (overlay words are never ENABLE words, so
    /// at most one has it; every overlay is searched so the Stats popup can define words from earlier runs).
    /// </summary>
    private static WordDefinition? Define(string word) =>
        DefinitionLoader.Default.Define(word) ?? Dictionaries.Define(word, Dictionaries.All.Select(d => d.Id));

    /// <summary>
    /// One line per distinct word: "[b]GLEY[/b] n. a sticky clay soil" (BBCode). The run's own dictionaries come first
    /// (a theme dictionary's gloss beats WordNet's), and a theme word is tagged with its bonus.
    /// </summary>
    private string DefinitionsText(IEnumerable<string> words) => string.Join('\n', words.Distinct().Select(word =>
    {
        string head = $"[b][color=#{UiKit.Text.ToHtml(false)}]{word}[/color][/b]  ";
        string body = (Dictionaries.Define(word, Run.Dictionaries) ?? Define(word)) is { } definition
            ? definition.Summary.Replace("[", "[lb]")
            : "[i]valid word — no definition on file[/i]";
        string tag = RoundScoring.Theme is { } theme && theme.Words.Contains(word)
            ? $"  [color=#{ThemeColor.ToHtml(false)}]+{theme.MultPerWord} mult · {theme.Name}[/color]"
            : "";
        return head + body + tag;
    }));

    private IReadOnlyList<RankedPlay> RankedPlays() =>
        MoveRanker.Rank(Round.Board, Round.Hand, _lexicon, Run.DeskItems, RoundScoring, Round.Config.MinWordLength,
            RoundRules.Environment(Round, Run.Money), Round.Config.CensoredLetter);

    private RankedPlay? BestPlay() => Hints.Best(RankedPlays());

    /// <summary>The Hint button: a decent play, never the best (the dev-only Best button places that).</summary>
    private void Hint() => ShowHint(best: false);

    private void ShowHint(bool best)
    {
        var ranked = RankedPlays();
        if ((best ? Hints.Best(ranked) : Hints.Decent(ranked)) is not { } hint)
        {
            SetMessage("No legal play with this hand — discard some tiles.", UiKit.Bad);
            return;
        }
        PlacePlay(hint, ReferenceEquals(hint, ranked[0]) ? null : "a hint, not the best play");
    }

    /// <summary>Puts a play's tiles on the board as pending (not submitted); <paramref name="note"/> follows the preview.</summary>
    private void PlacePlay(RankedPlay play, string? note)
    {
        _pending.Clear();
        _selected.Clear();
        foreach (var placed in play.Play.Placed)
            _pending[placed.Position] = placed.Tile;
        Refresh();
        if (note is not null)
            SetMessage(_messageLabel.Text + "  ·  " + note, UiKit.Good);
    }

    private void Discard()
    {
        var before = HandIds();
        var result = RunRules.Discard(_session, _selected.Select(t => t.Id).ToArray(), _lexicon);
        if (!result.IsOk)
        {
            SetMessage(result.Error.Message, UiKit.Bad);
            return;
        }
        _session = result.Value;
        MarkNewTiles(before);
        _selected.Clear();
        SetMessage("Discarded.", UiKit.TextMuted);
        AfterAction();
    }

    private void Submit()
    {
        if (_animating || _pending.Count == 0)
            return;

        var before = HandIds();
        var result = RunRules.Submit(_session, PendingPlacement(), _lexicon);
        if (!result.IsOk)
        {
            SetMessage(result.Error.Message, UiKit.Bad);
            return;
        }

        long scoreBefore = Round.Score;
        _session = result.Value.Session;
        MarkNewTiles(before);
        _profile.Update(s => Crossword.Core.Profile.StatsRules.RecordPlay(s, result.Value.Score.Play, result.Value.Score.Total));
        _pending.Clear();
        _selected.Clear();
        _animating = true;
        ClearLog();
        Refresh();
        AnimateScore(result.Value.Score, scoreBefore);
    }

    /// <summary>
    /// Plays back the scoring event log step by step, Balatro-style: numbers tick up, the Chips/Mult panels punch, the
    /// Desk Item (or Red Ink line) behind each step pops with a floating delta, and the finish escalates with the play's
    /// share of the deadline (see <see cref="Juice"/>).
    /// </summary>
    private void AnimateScore(ScoreContext score, long scoreBefore)
    {
        long target = Round.Config.TargetScore;
        var level = Juice.LevelFor(score.Total, target);
        _scoreLabel.Text = scoreBefore.ToString("N0");
        _chipsLabel.Text = "0";
        _multLabel.Text = "0";

        var tween = CreateTween();
        long chips = 0;
        decimal mult = 0;
        for (int i = 0; i < score.Log.Count; i++)
        {
            var e = score.Log[i];
            var (chipsFrom, multFrom) = (chips, mult);
            (chips, mult) = (e.ChipsAfter, e.MultAfter);
            double seconds = Juice.StepSeconds(i);
            float punch = Juice.StepScale(i, level);
            tween.TweenCallback(Callable.From(() => RingUp(e, chipsFrom, multFrom, seconds, punch)));
            tween.TweenInterval(seconds);
        }
        tween.TweenCallback(Callable.From(() =>
        {
            AddLog($"= {score.Total:N0} points" + (score.Money > 0 ? $"  (+${score.Money})" : ""), UiKit.Text);
            Juice.CountUp(_scoreLabel, scoreBefore, scoreBefore + score.Total, Juice.FinalCountSeconds, "N0");
            SetMessage($"+{score.Total:N0}", UiKit.Good);
        }));
        tween.TweenInterval(Juice.FinalCountSeconds);
        tween.TweenCallback(Callable.From(() => Celebrate(score.Total, target, level)));
        tween.TweenInterval(_session.Phase == RunPhase.InRound ? (level == Juice.Level.Huge ? 0.6 : 0.25) : 1.4);
        tween.TweenCallback(Callable.From(() =>
        {
            _animating = false;
            if (_session.Phase == RunPhase.Shop)
                SetMessage("Paycheck in. Spend it in the shop, then start the next round.", UiKit.Money);
            AfterAction();
        }));
    }

    /// <summary>One scoring step: tick the numbers that changed and pop whatever caused it.</summary>
    private void RingUp(EffectEvent e, long chipsFrom, decimal multFrom, double seconds, float punch)
    {
        double count = seconds * 0.7;
        if (e.ChipsAfter != chipsFrom)
        {
            Juice.CountUp(_chipsLabel, chipsFrom, e.ChipsAfter, count, "N0");
            Juice.Pop(_chipsPanel, punch);
        }
        if (e.MultAfter != multFrom)
        {
            Juice.CountUp(_multLabel, multFrom, e.MultAfter, count, "0.##");
            Juice.Pop(_multPanel, punch);
        }
        var color = ColorFor(e.SourceId);
        AddLog(e.Description, color);

        int colon = e.Description.IndexOf(": ", StringComparison.Ordinal);
        string delta = colon >= 0 ? e.Description[(colon + 2)..] : e.Description;
        if (_deskCards.TryGetValue(e.SourceId, out var card) && IsInstanceValid(card))
        {
            _deskPops++;
            Juice.Pop(card, Juice.SourcePunch + (punch - 1) * Juice.SourcePunchPerStep, 0.24);
            Juice.Flash(card, UiKit.Selected, 0.35);
            var rect = card.GetGlobalRect();
            Juice.FloatText(_fxLayer, new Vector2(rect.GetCenter().X, rect.End.Y + 26), delta, color, 18);
        }
        else if (e.SourceId == ScoringEngine.Sources.Bonus)
        {
            Juice.Pop(_resourcesLabel, Juice.SourcePunch, fromLeft: true);
            Juice.FloatText(_fxLayer, _resourcesLabel.GetGlobalRect().GetCenter(), delta, StationeryColor, 18);
        }
    }

    /// <summary>The finish: a punch on the score that grows with the play, shake + confetti for huge plays, and a stamp
    /// when one play clears the whole deadline.</summary>
    private void Celebrate(long total, long target, Juice.Level level)
    {
        float punch = level switch
        {
            Juice.Level.Huge => Juice.HugeFinalPunch,
            Juice.Level.Big => Juice.BigFinalPunch,
            _ => Juice.FinalPunch,
        };
        Juice.Pop(_scoreLabel, punch, 0.4, fromLeft: true);
        if (level != Juice.Level.Normal)
            Juice.Flash(_scoreLabel, UiKit.Money, 0.6);
        if (level == Juice.Level.Huge)
        {
            Juice.Shake(_shakeRoot);
            Juice.Confetti(_fxLayer, _scoreLabel.GetGlobalRect().GetCenter());
        }
        if (target > 0 && total >= target)
            Juice.Stamp(_fxLayer, _boardHolder, Juice.StampText);
        _lastCelebration = level;
    }

    /// <summary>The last finish level shown (self-test hook).</summary>
    private Juice.Level? _lastCelebration;

    /// <summary>Theme dictionary words (The Olde English Folio): old gold.</summary>
    private static readonly Color ThemeColor = new("d9b77a");

    private static Color ColorFor(string source) => source switch
    {
        ScoringEngine.Sources.Tier => UiKit.Text,
        ScoringEngine.Sources.Word => UiKit.Chips.Lightened(0.3f),
        ScoringEngine.Sources.Intersection => UiKit.Mult.Lightened(0.3f),
        ScoringEngine.Sources.Enhancement => UiKit.Money,
        ScoringEngine.Sources.Bonus => StationeryColor,
        ScoringEngine.Sources.Theme => ThemeColor,
        _ => new Color("c9a6ff"), // desk items
    };

    /// <summary>Common follow-up after any action: warn about dead hands, then redraw.</summary>
    private void AfterAction()
    {
        if (_session.Phase == RunPhase.InRound && !RoundRules.HasLegalPlay(Round, _lexicon))
            SetMessage(Round.DiscardsLeft > 0
                ? "No legal play with this hand — select tiles and discard."
                : "No legal play and no discards — use your Scissors or White-Out.", UiKit.Bad);
        Refresh();
    }

    // ---------------------------------------------------------------- keyboard

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        // Stats popup: while it's open, Esc closes it and nothing else reacts.
        if (HandleWildKey(key))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_statsOverlay.Visible)
        {
            if (key.Keycode == Key.Escape)
                ToggleStats();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_pressRunOverlay.Visible)
        {
            if (key.Keycode == Key.Escape)
                _pressRunOverlay.Visible = false;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_profilesOverlay.Visible || _settingsOverlay.Visible)
        {
            if (key.Keycode == Key.Escape)
                _profilesOverlay.Visible = _settingsOverlay.Visible = false;
            GetViewport().SetInputAsHandled();
            return;
        }
        // Title menu: Enter continues; nothing else reacts.
        if (_titleOverlay.Visible)
        {
            HandleTitleKey(key);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Style Guides popup: Tab toggles it anywhere; while it's open, Esc closes it and nothing else reacts.
        if (key.Keycode == Key.Tab || (_styleGuidesOverlay.Visible && key.Keycode == Key.Escape))
        {
            ToggleStyleGuides();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_styleGuidesOverlay.Visible || _session.Phase != RunPhase.InRound || _animating)
            return;

        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                Submit();
                break;
            case Key.Escape when _whiteOutSlot is not null:
                _whiteOutSlot = null;
                SetMessage("White-Out put away.", UiKit.TextMuted);
                Refresh();
                break;
            case Key.Escape:
                Recall();
                break;
            case Key.Space:
                ShuffleHand();
                break;
            case Key.Backspace when _pending.Count > 0:
                ReturnPending(_pending.Keys.Last());
                break;
            default:
                char typed = char.ToUpperInvariant((char)key.Unicode);
                if (typed is >= 'A' and <= 'Z'
                    && Round.Hand.Tiles.FirstOrDefault(t => !t.IsWild && t.Letter.Char == typed && !_selected.Contains(t)
                        && !_pending.Values.Any(p => p.Id == t.Id)) is { } tile)
                    ToggleSelected(tile);
                break;
        }
        GetViewport().SetInputAsHandled();
    }
}
