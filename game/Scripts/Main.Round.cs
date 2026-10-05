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
    private const double StepSeconds = 0.32;

    private Button _shuffleButton = null!;

    private ScoringConfig RoundScoring => Round.Config.EffectiveScoring(_session.Scoring);

    private Control BuildRoundArea()
    {
        var box = UiKit.VBox(16);

        _boardHolder = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        box.AddChild(_boardHolder);

        var handHolder = new CenterContainer();
        _handRow = new HandRow { CanAcceptDrop = () => _handDragId is not null, Dropped = DropOnHand };
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
        _shuffleButton = UiKit.MakeButton("Shuffle  Space", UiKit.PanelRaised, 18);
        _shuffleButton.Pressed += ShuffleHand;
        foreach (var b in new[] { _submitButton, _recallButton, _discardButton, _shuffleButton, _hintButton })
            buttons.AddChild(b);
        buttonsHolder.AddChild(buttons);
        box.AddChild(buttonsHolder);
        return box;
    }

    // ---------------------------------------------------------------- board & hand

    private void RefreshBoard()
    {
        UiKit.ClearChildren(_boardHolder);
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
            var tile = UiKit.MakeTile(placed, scoring.ValueOf(placed.Letter), CellSize, UiKit.Newsprint);
            tile.Disabled = true;
            return tile;
        }

        if (_pending.TryGetValue(pos, out var pending))
        {
            var tile = UiKit.MakeTile(pending, scoring.ValueOf(pending.Letter), CellSize, UiKit.Pending, raised: true);
            tile.TooltipText = "Click to take back";
            tile.Pressed += () => ReturnPending(pos);
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
        cell.AddThemeFontSizeOverride("font_size", 18);
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
        foreach (int id in _handOrder)
        {
            var tile = byId[id];
            if (_pending.ContainsValue(tile))
                continue;

            bool selected = _selected.Contains(tile);
            int value = scoring.ValueOf(tile.Letter);
            var button = UiKit.MakeTile(tile, value, HandTileSize, selected ? UiKit.Selected : UiKit.Newsprint, raised: selected);
            button.TooltipText = "Click to select · drag to reorder or onto the board";
            button.Pressed += () => ToggleSelected(tile);
            if (!_animating)
            {
                button.DragPreviewFactory = () => UiKit.MakeTile(tile, value, HandTileSize, UiKit.Selected, raised: true);
                button.DragStarted = BeginHandDrag;
                button.CanAcceptDrop = () => _handDragId is not null;
                button.Dropped = DropOnHand;
            }
            _handRow.AddChild(button);
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
        _shuffleButton.Disabled = !idle || Round.Hand.Count < 2;
    }

    // ---------------------------------------------------------------- interaction

    private void ToggleSelected(Tile tile)
    {
        if (_animating || TileButton.RecentlyDragged)
            return;
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
        var tile = _selected[0];
        _selected.RemoveAt(0);
        _pending[pos] = tile;
        Refresh();
    }

    /// <summary>Drag-and-drop placement: puts a specific hand tile on an empty square.</summary>
    private void PlaceTile(GridPos pos, int tileId)
    {
        if (_animating || Round.Hand.Tiles.FirstOrDefault(t => t.Id == tileId) is not { } tile || _pending.ContainsValue(tile))
            return;
        _selected.Remove(tile);
        _pending[pos] = tile;
        Refresh();
    }

    // ---------------------------------------------------------------- hand drag (ghost slot)

    /// <summary>The dragged tile stays in the row as a ghost marking where it will land.</summary>
    private void BeginHandDrag(int tileId)
    {
        if (_animating || HandTileButton(tileId) is not { } ghost)
            return;
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

    /// <summary>A hand tile dropped on the hand takes the ghost's slot.</summary>
    private void DropOnHand(int tileId)
    {
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
        // Fired after any drop has been handled. A drag that didn't land on the hand (cancelled, or placed on the
        // board) leaves its ghost behind: rebuild the row. Deferred so no nodes are freed mid-propagation.
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
        (_handOrder, _arrangementRng) = HandArrangement.Shuffle(HandArrangement.Reconcile(_handOrder, Round.Hand), _arrangementRng);
        Refresh();
    }

    private void ReturnPending(GridPos pos)
    {
        if (_animating)
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

        var validation = PlacementValidator.Validate(Round.Board, Round.Hand, PendingPlacement(), _lexicon, Round.Config.MinWordLength);
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

    /// <summary>One line per distinct word: "[b]GLEY[/b] n. a sticky clay soil" (BBCode).</summary>
    private static string DefinitionsText(IEnumerable<string> words) => string.Join('\n', words.Distinct().Select(word =>
    {
        string head = $"[b][color=#{UiKit.Text.ToHtml(false)}]{word}[/color][/b]  ";
        return DefinitionLoader.Default.Define(word) is { } definition
            ? head + definition.Summary.Replace("[", "[lb]")
            : head + "[i]valid word — no definition on file[/i]";
    }));

    private RankedPlay? BestPlay() =>
        MoveRanker.Rank(Round.Board, Round.Hand, _lexicon, Run.DeskItems, RoundScoring, Round.Config.MinWordLength,
            RoundRules.Environment(Round, Run.Money)).FirstOrDefault();

    private void Hint()
    {
        if (BestPlay() is not { } best)
        {
            SetMessage("No legal play with this hand — discard some tiles.", UiKit.Bad);
            return;
        }
        _pending.Clear();
        _selected.Clear();
        foreach (var placed in best.Play.Placed)
            _pending[placed.Position] = placed.Tile;
        Refresh();
    }

    private void Discard()
    {
        var result = RunRules.Discard(_session, _selected.Select(t => t.Id).ToArray(), _lexicon);
        if (!result.IsOk)
        {
            SetMessage(result.Error.Message, UiKit.Bad);
            return;
        }
        _session = result.Value;
        _selected.Clear();
        SetMessage("Discarded.", UiKit.TextMuted);
        AfterAction();
    }

    private void Submit()
    {
        if (_animating || _pending.Count == 0)
            return;

        var result = RunRules.Submit(_session, PendingPlacement(), _lexicon);
        if (!result.IsOk)
        {
            SetMessage(result.Error.Message, UiKit.Bad);
            return;
        }

        long scoreBefore = Round.Score;
        _session = result.Value.Session;
        _pending.Clear();
        _selected.Clear();
        _animating = true;
        ClearLog();
        Refresh();
        AnimateScore(result.Value.Score, scoreBefore);
    }

    /// <summary>Plays back the scoring event log step by step, Balatro-style.</summary>
    private void AnimateScore(ScoreContext score, long scoreBefore)
    {
        _scoreLabel.Text = scoreBefore.ToString("N0");
        var tween = CreateTween();
        foreach (var evt in score.Log)
        {
            var e = evt;
            tween.TweenCallback(Callable.From(() =>
            {
                _chipsLabel.Text = e.ChipsAfter.ToString("N0");
                _multLabel.Text = e.MultAfter.ToString("0.##");
                AddLog(e.Description, ColorFor(e.SourceId));
            }));
            tween.TweenInterval(StepSeconds);
        }
        tween.TweenCallback(Callable.From(() =>
        {
            AddLog($"= {score.Total:N0} points" + (score.Money > 0 ? $"  (+${score.Money})" : ""), UiKit.Text);
            _scoreLabel.Text = (scoreBefore + score.Total).ToString("N0");
            SetMessage($"+{score.Total:N0}", UiKit.Good);
        }));
        tween.TweenInterval(_session.Phase == RunPhase.InRound ? 0.2 : 1.1);
        tween.TweenCallback(Callable.From(() =>
        {
            _animating = false;
            if (_session.Phase == RunPhase.Shop)
                SetMessage("Paycheck in. Spend it in the shop, then start the next round.", UiKit.Money);
            AfterAction();
        }));
    }

    private static Color ColorFor(string source) => source switch
    {
        ScoringEngine.Sources.Tier => UiKit.Text,
        ScoringEngine.Sources.Word => UiKit.Chips.Lightened(0.3f),
        ScoringEngine.Sources.Intersection => UiKit.Mult.Lightened(0.3f),
        ScoringEngine.Sources.Enhancement => UiKit.Money,
        _ => new Color("c9a6ff"), // desk items
    };

    /// <summary>Common follow-up after any action: warn about dead hands, then redraw.</summary>
    private void AfterAction()
    {
        if (_session.Phase == RunPhase.InRound && !RoundRules.HasLegalPlay(Round, _lexicon))
            SetMessage("No legal play with this hand — select tiles and discard.", UiKit.Bad);
        Refresh();
    }

    // ---------------------------------------------------------------- keyboard

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

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
                    && Round.Hand.Tiles.FirstOrDefault(t => t.Letter.Char == typed && !_selected.Contains(t) && !_pending.ContainsValue(t)) is { } tile)
                    ToggleSelected(tile);
                break;
        }
        GetViewport().SetInputAsHandled();
    }
}
