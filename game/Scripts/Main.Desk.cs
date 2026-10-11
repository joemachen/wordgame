using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Scoring;
using Crossword.Core.Stationery;
using Godot;
using GridPos = Crossword.Core.Domain.Position;

namespace Wordgame.Godot;

public partial class Main
{
    private static readonly Color StationeryColor = new("b48ef0");

    /// <summary>Desk Item cards by item id, so scoring playback can pop the card that fired.</summary>
    private readonly Dictionary<string, Control> _deskCards = new();

    /// <summary>How many times scoring playback has popped a Desk Item card (self-test hook).</summary>
    private int _deskPops;

    /// <summary>Slot of the Stationery waiting for a board square to be clicked (White-Out, Clipping, Gold Star); null when not targeting.</summary>
    private int? _targetingSlot;

    /// <summary>What the armed Stationery wants clicked, or null when nothing is armed.</summary>
    private StationeryTarget? ArmedTarget =>
        _targetingSlot is int slot && slot < Run.Stationery.Length ? Run.Stationery[slot].Target : null;

    private static bool TargetsBoard(StationeryTarget target) =>
        target is StationeryTarget.BoardTile or StationeryTarget.BoardWord or StationeryTarget.EmptyCell;

    private static Color RarityColor(DeskItemRarity rarity) => rarity switch
    {
        DeskItemRarity.Uncommon => new Color("4cc38a"),
        DeskItemRarity.Rare => new Color("e5484d"),
        DeskItemRarity.Epic => new Color("a66cff"),
        DeskItemRarity.Legendary => new Color("f0a020"),
        _ => new Color("5aa9e6"),
    };

    /// <summary>The five Desk Item slots. They apply left to right, so they can be reordered.</summary>
    private void RefreshDesk()
    {
        UiKit.ClearChildren(_deskRow);
        _deskCards.Clear();
        if (_targetingSlot is int targeting
            && (_session.Phase != RunPhase.InRound || targeting >= Run.Stationery.Length || !TargetsBoard(Run.Stationery[targeting].Target)))
            _targetingSlot = null;
        bool canEdit = _session.Phase is RunPhase.InRound or RunPhase.Shop && !_animating;
        _deskCaption.Visible = Run.DeskItems.Length >= 2;
        var play = _session.Phase == RunPhase.InRound ? PendingPlay() : null;
        long current = play is null ? 0 : ScoreWith(play, Run.DeskItems);

        for (int slot = 0; slot < _session.Config.DeskSlots; slot++)
        {
            if (slot >= Run.DeskItems.Length)
            {
                var empty = UiKit.MakePanel(UiKit.Panel, padding: 10, border: UiKit.PanelBorder, borderWidth: 1);
                empty.CustomMinimumSize = new Vector2(0, 104);
                empty.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                empty.AddChild(UiKit.MakeLabel("empty desk slot", 13, UiKit.TextMuted, HorizontalAlignment.Center));
                _deskRow.AddChild(empty);
                continue;
            }

            var item = Run.DeskItems[slot];
            var card = UiKit.MakePanel(UiKit.PanelRaised, padding: 10, border: RarityColor(item.Rarity), borderWidth: 2);
            card.CustomMinimumSize = new Vector2(0, 104);
            card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _deskCards[item.Id] = card;
            var box = UiKit.VBox(4);
            card.AddChild(box);
            box.AddChild(UiKit.MakeLabel(item.Name, 16, UiKit.Text));
            var description = UiKit.MakeLabel(item.Description, 12, UiKit.TextMuted, wrap: true);
            description.SizeFlagsVertical = SizeFlags.ExpandFill;
            box.AddChild(description);

            var actions = UiKit.HBox(4);
            int index = slot;
            var left = UiKit.MakeButton("◀", UiKit.Panel, 12);
            left.Disabled = !canEdit || index == 0;
            left.Pressed += () => MoveDeskItem(index, index - 1);
            var right = UiKit.MakeButton("▶", UiKit.Panel, 12);
            right.Disabled = !canEdit || index == Run.DeskItems.Length - 1;
            right.Pressed += () => MoveDeskItem(index, index + 1);
            SetMoveHint(left, index, index - 1, "left", play, current);
            SetMoveHint(right, index, index + 1, "right", play, current);
            var sell = UiKit.MakeButton($"Sell ${_session.Config.Shop.SellValueOf(item)}", UiKit.Panel, 12, UiKit.Money);
            sell.Disabled = !canEdit;
            sell.Pressed += () => SellDeskItem(index);
            actions.AddChild(left);
            actions.AddChild(right);
            actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            actions.AddChild(sell);
            box.AddChild(actions);
            _deskRow.AddChild(card);
        }

        _deskRow.AddChild(new VSeparator());
        for (int slot = 0; slot < RunState.MaxStationerySlots; slot++)
            _deskRow.AddChild(BuildStationerySlot(slot, canEdit));
    }

    /// <summary>A Stationery slot: one-shot items, used during a round or sold.</summary>
    private Control BuildStationerySlot(int slot, bool canEdit)
    {
        var card = UiKit.MakePanel(slot < Run.Stationery.Length ? UiKit.PanelRaised : UiKit.Panel, padding: 10,
            border: slot < Run.Stationery.Length ? StationeryColor : UiKit.PanelBorder, borderWidth: slot < Run.Stationery.Length ? 2 : 1);
        card.CustomMinimumSize = new Vector2(150, 104);
        if (slot >= Run.Stationery.Length)
        {
            card.AddChild(UiKit.MakeLabel("empty stationery", 13, UiKit.TextMuted, HorizontalAlignment.Center));
            return card;
        }

        var item = Run.Stationery[slot];
        var box = UiKit.VBox(4);
        card.AddChild(box);
        box.AddChild(UiKit.MakeLabel(item.Name, 16, UiKit.Text));
        var description = UiKit.MakeLabel(item.Description, 12, UiKit.TextMuted, wrap: true);
        description.SizeFlagsVertical = SizeFlags.ExpandFill;
        box.AddChild(description);

        var actions = UiKit.HBox(4);
        int index = slot;
        var use = UiKit.MakeButton(_targetingSlot == slot ? "Cancel" : "Use", UiKit.Panel, 12, StationeryColor);
        use.Disabled = _animating || _session.Phase != RunPhase.InRound;
        use.TooltipText = item.Target switch
        {
            StationeryTarget.HandTiles => "Select hand tiles first, then Use",
            StationeryTarget.BoardTile => "Use, then click a board tile",
            StationeryTarget.BoardWord => "Use, then click a tile of the word",
            StationeryTarget.EmptyCell => "Use, then click an empty square",
            _ => "",
        };
        use.Pressed += () => UseStationery(index);
        var sell = UiKit.MakeButton($"Sell ${_session.Config.Shop.SellValueOf(item)}", UiKit.Panel, 12, UiKit.Money);
        sell.Disabled = !canEdit;
        sell.Pressed += () => SellStationery(index);
        actions.AddChild(use);
        actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        actions.AddChild(sell);
        box.AddChild(actions);
        return card;
    }

    private void UseStationery(int slot)
    {
        var item = Run.Stationery[slot];
        switch (item)
        {
            case { Target: StationeryTarget.BoardTile or StationeryTarget.BoardWord or StationeryTarget.EmptyCell }:
                if (_targetingSlot == slot)
                {
                    _targetingSlot = null;
                    SetMessage($"{item.Name} put away.", UiKit.TextMuted);
                }
                else if (item.Target != StationeryTarget.EmptyCell && Round.Board.IsEmpty)
                {
                    SetMessage("There are no tiles on the board yet.", UiKit.Bad);
                    return;
                }
                else
                {
                    _targetingSlot = slot;
                    SetMessage(item.Target switch
                    {
                        StationeryTarget.EmptyCell => $"{item.Name}: click an empty square (Esc cancels).",
                        StationeryTarget.BoardWord => $"{item.Name}: click a tile of the word to reprint (Esc cancels).",
                        _ => $"{item.Name}: click a board tile to remove it (Esc cancels).",
                    }, StationeryColor);
                }
                Refresh();
                return;

            case Scissors scissors when _selected.Count == 0:
                SetMessage($"Select up to {scissors.MaxTiles} hand tiles, then use the {item.Name}.", UiKit.TextMuted);
                return;

            case FountainPen when _selected.Count != 1:
                SetMessage($"Select one hand tile, then use the {item.Name} to make it wild.", UiKit.TextMuted);
                return;

            case Highlighter when _selected.Count != 1:
                SetMessage($"Select one hand tile, then use the {item.Name} to triple its letter value next play.", UiKit.TextMuted);
                return;

            case { Target: StationeryTarget.HandTiles }:
                ApplyStationery(slot, tileIds: _selected.Select(t => t.Id).ToArray());
                return;

            default:
                ApplyStationery(slot);
                return;
        }
    }

    /// <summary>Board click while a board-targeting Stationery is armed.</summary>
    private void TargetCell(GridPos pos)
    {
        if (_targetingSlot is int slot && !_animating)
            ApplyStationery(slot, cell: pos);
    }

    private void ApplyStationery(int slot, IReadOnlyCollection<int>? tileIds = null, GridPos? cell = null)
    {
        var item = Run.Stationery[slot];
        var before = HandIds();
        var used = RunRules.UseStationery(_session, slot, _lexicon, tileIds, cell);
        if (!used.IsOk)
        {
            SetMessage(used.Error, UiKit.Bad);
            return;
        }
        _session = used.Value.Session;
        _targetingSlot = null;
        if (item is Scissors or CorrectionTape)
            MarkNewTiles(before);
        if (item is CorrectionTape)
            ClearLog();
        if (used.Value.Play is { } play)
        {
            PlacePlay(play, $"{item.Name}: the best play for this hand");
            return;
        }

        _selected.Clear();
        foreach (var gone in _pending.Where(kv => !Round.Hand.Contains(kv.Value.Id) || Round.Board.IsOccupied(kv.Key)).ToList())
            _pending.Remove(gone.Key);
        SetMessage(item switch
        {
            MarginClip clip => $"{item.Name}: +{clip.Submissions} submission this round.",
            RedInkBottle ink => $"{item.Name}: +{ink.Mult} mult on every play this round.",
            Scissors => $"{item.Name}: {tileIds?.Count ?? 0} tile(s) cut and redrawn.",
            WhiteOut => $"{item.Name}: tile removed.",
            FountainPen => $"{item.Name}: that tile is wild this round — place it and pick its letter.",
            Highlighter marker => $"{item.Name}: that tile's letter value counts ×{marker.Factor} on your next play.",
            GoldStar => $"{item.Name}: that square pays when a tile lands on it this round.",
            Clipping => $"{item.Name}: {Round.Config.Clipping?.Text} is scored again on your next play.",
            PoeticLicense => $"{item.Name}: one of your next plays may contain a word that isn't in the dictionary.",
            CorrectionTape => $"{item.Name}: your last play is back in your hand.",
            _ => $"{item.Name} used.",
        }, StationeryColor);
        AfterAction();
    }

    private void SellStationery(int slot)
    {
        var sold = ShopRules.SellStationery(_session, slot, _lexicon);
        if (sold.IsOk)
            _session = sold.Value;
        Refresh();
    }

    /// <summary>
    /// Tooltip (and tint) for a Desk Item move arrow: with a pending play, what that play would score after the move;
    /// otherwise the ordering rule.
    /// </summary>
    private void SetMoveHint(Button arrow, int from, int to, string direction, PlayAnalysis? play, long current)
    {
        if (arrow.Disabled)
            return;
        if (play is null)
        {
            arrow.TooltipText = $"Move {direction}. Desk Items apply left to right, so put +Chips and +Mult items before ×Mult ones. "
                + "Place a play on the board to compare scores.";
            return;
        }
        long moved = ScoreWith(play, Run.MoveDeskItem(from, to).Value.DeskItems);
        long change = moved - current;
        arrow.TooltipText = change == 0
            ? $"Move {direction}: no change for this play ({current:N0})"
            : $"Move {direction}: {current:N0} → {moved:N0} ({change:+#,0;-#,0}) for this play";
        if (change != 0)
        {
            var color = change > 0 ? UiKit.Good : UiKit.Bad;
            arrow.AddThemeColorOverride("font_color", color);
            arrow.AddThemeColorOverride("font_hover_color", color);
        }
    }

    /// <summary>The pending placement as a validated play, or null when there is none or it is illegal.</summary>
    private PlayAnalysis? PendingPlay() =>
        _pending.Count > 0 && PlacementValidator.Validate(Round.Board, Round.Hand, PendingPlacement(), _lexicon, Round.Config.MinWordLength,
                Round.Config.CensoredLetter, Round.Config.IllegalWordsAllowed)
            is { IsOk: true } valid
            ? valid.Value
            : null;

    private long ScoreWith(PlayAnalysis play, IReadOnlyList<IDeskItem> items) =>
        ScoringEngine.Score(play, items, RoundScoring, RoundRules.Environment(Round, Run.Money)).Total;

    private void MoveDeskItem(int from, int to)
    {
        var moved = Run.MoveDeskItem(from, to);
        if (moved.IsOk)
            _session = _session with { Run = moved.Value };
        Refresh();
    }

    private void SellDeskItem(int slot)
    {
        var sold = ShopRules.Sell(_session, slot);
        if (sold.IsOk)
            _session = sold.Value;
        Refresh();
    }
}
