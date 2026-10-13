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

        for (int slot = 0; slot < _session.Config.DeskSlots; slot++)
        {
            if (slot >= Run.DeskItems.Length)
            {
                var empty = UiKit.MakePanel(UiKit.Panel, padding: 10, border: UiKit.PanelBorder, borderWidth: 1);
                empty.CustomMinimumSize = new Vector2(0, DeskCardHeight);
                empty.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                var emptyBox = UiKit.VBox(4);
                empty.AddChild(emptyBox);
                emptyBox.AddChild(SlotLine(slot, null));
                var emptyText = UiKit.MakeLabel("empty desk slot", 13, UiKit.TextMuted, HorizontalAlignment.Center);
                emptyText.SizeFlagsVertical = SizeFlags.ExpandFill;
                emptyText.VerticalAlignment = VerticalAlignment.Center;
                emptyBox.AddChild(emptyText);
                _deskRow.AddChild(empty);
                continue;
            }

            var item = Run.DeskItems[slot];
            var card = UiKit.MakePanel(UiKit.PanelRaised, padding: 10, border: RarityColor(item.Rarity), borderWidth: 2);
            card.CustomMinimumSize = new Vector2(0, DeskCardHeight);
            card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            card.TooltipText = $"{item.Name} ({item.Rarity})\n{item.Description}";
            _deskCards[item.Id] = card;
            var box = UiKit.VBox(4);
            card.AddChild(box);
            box.AddChild(SlotLine(slot, item.Kind));
            var name = UiKit.MakeLabel(item.Name, 15, UiKit.Text);
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            box.AddChild(name);
            var description = UiKit.MakeLabel(item.Description, 12, UiKit.TextMuted, wrap: true);
            description.MaxLinesVisible = 3;
            description.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            description.SizeFlagsVertical = SizeFlags.ExpandFill;
            box.AddChild(description);

            var actions = UiKit.HBox(4);
            int index = slot;
            var sell = UiKit.Compact(UiKit.MakeButton($"Sell ${_session.Config.Shop.SellValueOf(item)}", UiKit.Panel, 12, UiKit.Money));
            sell.Disabled = !canEdit;
            sell.Pressed += () => SellDeskItem(index);
            actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            actions.AddChild(sell);
            box.AddChild(actions);
            _deskRow.AddChild(card);
        }

        _deskRow.AddChild(new VSeparator());
        for (int slot = 0; slot < RunState.MaxStationerySlots; slot++)
            _deskRow.AddChild(BuildStationerySlot(slot, canEdit));
    }

    /// <summary>
    /// Every desk and Stationery card has this height, full or empty, whatever its text says (descriptions are capped
    /// and the full text is in the card's tooltip), so the bar never changes height as items come and go.
    /// </summary>
    private static int DeskCardHeight => UiKit.FontSize(166);

    /// <summary>A desk card's top line: its firing position, and what it does to the score (its kind tag).</summary>
    private static Control SlotLine(int slot, DeskItemKind? kind)
    {
        var line = UiKit.HBox(4);
        var number = UiKit.MakeLabel($"{slot + 1}", 11, UiKit.TextMuted);
        number.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        number.Name = "SlotNumber";
        line.AddChild(number);
        if (kind is { } k)
        {
            var tag = UiKit.MakeLabel(DeskItemKinds.Tag(k), 11, KindColor(k), HorizontalAlignment.Right);
            tag.Name = "KindTag";
            tag.TooltipText = DeskItemKinds.Explain(k);
            tag.MouseFilter = MouseFilterEnum.Pass;
            line.AddChild(tag);
        }
        return line;
    }

    private static Color KindColor(DeskItemKind kind) => kind switch
    {
        DeskItemKind.Chips => UiKit.Chips.Lightened(0.25f),
        DeskItemKind.Mult => UiKit.Mult.Lightened(0.25f),
        DeskItemKind.TimesMult => UiKit.Mult.Lightened(0.45f),
        DeskItemKind.TimesChipsAndMult => UiKit.Money,
        DeskItemKind.TimesChips => UiKit.Chips.Lightened(0.45f),
        DeskItemKind.Retrigger => UiKit.Good,
        DeskItemKind.Copy => UiKit.Money,
        DeskItemKind.Money => UiKit.Money,
        _ => UiKit.TextMuted,
    };

    /// <summary>A Stationery slot: one-shot items, used during a round or sold.</summary>
    private Control BuildStationerySlot(int slot, bool canEdit)
    {
        var card = UiKit.MakePanel(slot < Run.Stationery.Length ? UiKit.PanelRaised : UiKit.Panel, padding: 10,
            border: slot < Run.Stationery.Length ? StationeryColor : UiKit.PanelBorder, borderWidth: slot < Run.Stationery.Length ? 2 : 1);
        card.CustomMinimumSize = new Vector2(150, DeskCardHeight);
        if (slot >= Run.Stationery.Length)
        {
            var emptyText = UiKit.MakeLabel("empty stationery", 13, UiKit.TextMuted, HorizontalAlignment.Center);
            emptyText.VerticalAlignment = VerticalAlignment.Center;
            card.AddChild(emptyText);
            return card;
        }

        var item = Run.Stationery[slot];
        card.TooltipText = $"{item.Name}\n{item.Description}";
        var box = UiKit.VBox(4);
        card.AddChild(box);
        var name = UiKit.MakeLabel(item.Name, 15, UiKit.Text);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        box.AddChild(name);
        var description = UiKit.MakeLabel(item.Description, 12, UiKit.TextMuted, wrap: true);
        description.MaxLinesVisible = 4;
        description.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        description.SizeFlagsVertical = SizeFlags.ExpandFill;
        box.AddChild(description);

        var actions = UiKit.HBox(4);
        int index = slot;
        var use = UiKit.Compact(UiKit.MakeButton(_targetingSlot == slot ? "Cancel" : "Use", UiKit.Panel, 12, StationeryColor));
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
        var sell = UiKit.Compact(UiKit.MakeButton($"Sell ${_session.Config.Shop.SellValueOf(item)}", UiKit.Panel, 12, UiKit.Money));
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

    private void SellDeskItem(int slot)
    {
        var sold = ShopRules.Sell(_session, slot);
        if (sold.IsOk)
            _session = sold.Value;
        Refresh();
    }
}
