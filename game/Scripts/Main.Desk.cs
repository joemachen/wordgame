using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Run;
using Godot;

namespace Wordgame.Godot;

public partial class Main
{
    private static readonly Color StationeryColor = new("b48ef0");

    private static Color RarityColor(DeskItemRarity rarity) => rarity switch
    {
        DeskItemRarity.Uncommon => new Color("4cc38a"),
        DeskItemRarity.Rare => new Color("e5484d"),
        _ => new Color("5aa9e6"),
    };

    /// <summary>The five Desk Item slots. They apply left to right, so they can be reordered.</summary>
    private void RefreshDesk()
    {
        UiKit.ClearChildren(_deskRow);
        bool canEdit = _session.Phase is RunPhase.InRound or RunPhase.Shop && !_animating;

        for (int slot = 0; slot < RunState.MaxDeskSlots; slot++)
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
            var sell = UiKit.MakeButton($"Sell ${_config.Shop.SellValueOf(item)}", UiKit.Panel, 12, UiKit.Money);
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
        var use = UiKit.MakeButton("Use", UiKit.Panel, 12, StationeryColor);
        use.Disabled = _animating || _session.Phase != RunPhase.InRound;
        use.Pressed += () => UseStationery(index);
        var sell = UiKit.MakeButton($"Sell ${_config.Shop.SellValueOf(item)}", UiKit.Panel, 12, UiKit.Money);
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
        string name = Run.Stationery[slot].Name;
        var used = RunRules.UseStationery(_session, slot, _lexicon);
        if (!used.IsOk)
        {
            SetMessage(used.Error, UiKit.Bad);
            return;
        }
        _session = used.Value.Session;
        if (used.Value.Play is { } play)
            PlacePlay(play, $"{name}: the best play for this hand");
        else
            Refresh();
    }

    private void SellStationery(int slot)
    {
        var sold = ShopRules.SellStationery(_session, slot);
        if (sold.IsOk)
            _session = sold.Value;
        Refresh();
    }

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
