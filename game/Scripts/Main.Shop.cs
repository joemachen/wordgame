using Crossword.Core.Domain;
using Crossword.Core.Run;
using Godot;

namespace Wordgame.Godot;

public partial class Main
{
    // Tile picker for Enhance/Strike offers: which offer is being bought and which deck tiles are chosen.
    private int? _pickerOffer;
    private readonly List<int> _pickerSelection = new();

    private void RefreshShopArea()
    {
        UiKit.ClearChildren(_shopContent);
        switch (_session.Phase)
        {
            case RunPhase.Shop when _pickerOffer is { } offerIndex:
                BuildTilePicker(offerIndex);
                break;
            case RunPhase.Shop:
                BuildShop();
                break;
            case RunPhase.Victory:
                BuildEndScreen(victory: true);
                break;
            case RunPhase.Defeat:
                BuildEndScreen(victory: false);
                break;
        }
    }

    private void BuildShop()
    {
        if (_session.LastPayout is { } payout)
            _shopContent.AddChild(BuildPaycheck(payout));

        var shop = _session.Shop!;
        int next = Run.RoundIndex + 1;
        var config = _session.Config;
        var nextKind = config.KindOf(next);
        string nextBoss = nextKind.IsBoss ? $"  —  BOSS: {RunRules.BossFor(config, Run, config.WeekOf(next)).Name}" : "";

        _shopContent.AddChild(UiKit.MakeLabel("THE SHOP", 30, UiKit.Text));
        _shopContent.AddChild(UiKit.MakeLabel(
            $"Next: Week {config.WeekOf(next) + 1} {nextKind.Name}, deadline {RunRules.TargetFor(config, Run, next):N0}{nextBoss}", 17,
            nextKind.IsBoss ? UiKit.Bad : UiKit.TextMuted, wrap: true));

        var actions = UiKit.HBox(12);
        var reroll = UiKit.MakeButton($"Reroll  ${shop.RerollCost}", UiKit.PanelRaised, 18, UiKit.Money);
        reroll.Disabled = Run.Money < shop.RerollCost;
        reroll.Pressed += () => ApplyShop(ShopRules.Reroll(_session));
        var leave = UiKit.MakeButton("Next round  ▶", UiKit.Good.Darkened(0.25f), 20);
        leave.Pressed += LeaveShop;
        actions.AddChild(reroll);
        actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        actions.AddChild(leave);
        _shopContent.AddChild(actions);

        var offers = new HFlowContainer();
        offers.AddThemeConstantOverride("h_separation", 12);
        offers.AddThemeConstantOverride("v_separation", 12);
        for (int i = 0; i < shop.Offers.Length; i++)
            offers.AddChild(BuildOfferCard(i, shop.Offers[i]));
        _shopContent.AddChild(offers);

        var tiers = _session.Scoring.Tiers;
        string tierText = string.Join("     ", tiers.Select((t, i) =>
            $"{t.Label(i == tiers.Length - 1)} Lv{Run.TierUpgrades.GetValueOrDefault(t.MinLength) + 1}: {t.BaseChips}×{t.BaseMult:0.##}"));
        _shopContent.AddChild(UiKit.MakeLabel($"Word tiers   {tierText}     · Tab: Style Guides", 14, UiKit.TextMuted, wrap: true));
        _shopContent.AddChild(UiKit.MakeLabel(DeckSummary(), 14, UiKit.TextMuted, wrap: true));
    }

    private Control BuildPaycheck(Payout payout)
    {
        var panel = UiKit.MakePanel(UiKit.Panel, padding: 14, border: UiKit.Money, borderWidth: 1);
        var box = UiKit.VBox(4);
        panel.AddChild(box);
        var top = UiKit.HBox(12);
        var cleared = UiKit.MakeLabel($"{_session.Kind.Name} cleared — {Round.Score:N0} / {Round.Config.TargetScore:N0}", 20, UiKit.Good, wrap: true);
        cleared.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(cleared);
        top.AddChild(UiKit.MakeLabel($"+${payout.Total}", 24, UiKit.Money));
        box.AddChild(top);
        string parts = $"Column fee ${payout.Base}";
        if (payout.UnusedSubmissions > 0) parts += $"   ·   Unused submissions ${payout.UnusedSubmissions}";
        if (payout.Overkill > 0) parts += $"   ·   Overkill ${payout.Overkill}";
        if (payout.Interest > 0) parts += $"   ·   Interest ${payout.Interest}";
        box.AddChild(UiKit.MakeLabel(parts, 15, UiKit.TextMuted, wrap: true));
        return panel;
    }

    private Control BuildOfferCard(int index, ShopOffer? offer)
    {
        var accent = offer switch
        {
            DeskItemOffer desk => RarityColor(desk.Item.Rarity),
            StyleGuideOffer => UiKit.Chips,
            StationeryOffer => StationeryColor,
            null => UiKit.PanelBorder,
            _ => UiKit.Money,
        };
        var card = UiKit.MakePanel(UiKit.PanelRaised, padding: 14, border: accent, borderWidth: 2);
        card.CustomMinimumSize = new Vector2(250, 170);
        var box = UiKit.VBox(8);
        card.AddChild(box);

        if (offer is null)
        {
            box.AddChild(UiKit.MakeLabel("SOLD", 22, UiKit.TextMuted, HorizontalAlignment.Center));
            return card;
        }

        (string kind, string title, string body) = offer switch
        {
            DeskItemOffer d => ($"DESK ITEM · {d.Item.Rarity.ToString().ToUpperInvariant()}", d.Item.Name, d.Item.Description),
            StationeryOffer st => ("STATIONERY · ONE USE", st.Item.Name, st.Item.Description),
            StyleGuideOffer g => ("STYLE GUIDE", g.Name, $"{g.TierLabel} words: +{g.Chips} chips, +{g.Mult} mult for every play whose longest word is this length. Permanent."),
            AddTileOffer { Wild: true } => ("NEW TILE", "Wild tile", "Plays as any letter you choose (0 chips). Added to your deck."),
            AddTileOffer a => ("NEW TILE", a.Enhancement == TileEnhancement.None ? $"Tile {a.Letter}" : $"{a.Enhancement} {a.Letter}", "Added to your deck."),
            WildOffer => ("EDIT", "Make a tile wild", "It plays as any letter you choose (0 chips) and keeps its enhancement. You choose the tile."),
            EnhanceOffer e => ("EDIT", $"Make a tile {e.Enhancement}", EnhancementBlurb(e.Enhancement) + " You choose the tile."),
            StrikeOffer s => ("EDIT", "Strike tiles", $"Remove up to {s.MaxTiles} tiles from your deck."),
            _ => ("", offer.Description, ""),
        };

        box.AddChild(UiKit.MakeLabel(kind, 12, accent));
        box.AddChild(UiKit.MakeLabel(title, 20, UiKit.Text, wrap: true));
        var description = UiKit.MakeLabel(body, 14, UiKit.TextMuted, wrap: true);
        description.SizeFlagsVertical = SizeFlags.ExpandFill;
        box.AddChild(description);

        var buy = UiKit.MakeButton($"Buy  ${offer.Price}", UiKit.Good.Darkened(0.3f), 18, UiKit.Money);
        buy.Disabled = Run.Money < offer.Price;
        buy.Pressed += () => BuyOffer(index, offer);
        box.AddChild(buy);
        return card;
    }

    private static string EnhancementBlurb(TileEnhancement enhancement) => enhancement switch
    {
        TileEnhancement.Bold => "Bold: +10 chips per word it's in.",
        TileEnhancement.Italic => "Italic: +2 mult per word it's in.",
        TileEnhancement.Gilded => "Gilded: +$1 per word it's in.",
        _ => "",
    };

    private string DeckSummary()
    {
        var counts = Run.Deck.GroupBy(t => t.IsWild ? '?' : t.Letter.Char).OrderBy(g => g.Key).Select(g => $"{g.Key}{g.Count()}");
        int enhanced = Run.Deck.Count(t => t.Enhancement != TileEnhancement.None);
        return $"Deck ({Run.Deck.Length} tiles, {enhanced} enhanced)   {string.Join(" ", counts)}";
    }

    private void BuyOffer(int index, ShopOffer offer)
    {
        if (offer is EnhanceOffer or StrikeOffer or WildOffer)
        {
            _pickerOffer = index;
            _pickerSelection.Clear();
            Refresh();
            return;
        }
        ApplyShop(ShopRules.Buy(_session, index));
    }

    private void ApplyShop(Result<GameSession, string> result)
    {
        if (result.IsOk)
        {
            _session = result.Value;
            SetMessage("", UiKit.TextMuted);
        }
        else
        {
            SetMessage(result.Error, UiKit.Bad);
        }
        Refresh();
    }

    private void LeaveShop()
    {
        var result = RunRules.LeaveShop(_session, _lexicon);
        if (!result.IsOk)
        {
            SetMessage(result.Error, UiKit.Bad);
            return;
        }
        _session = result.Value;
        ClearLog();
        SetMessage($"{_session.Kind.Name}: reach {Round.Config.TargetScore:N0}.", UiKit.TextMuted);
        AfterAction();
    }

    // ---------------------------------------------------------------- tile picker

    private void BuildTilePicker(int offerIndex)
    {
        var offer = _session.Shop!.Offers[offerIndex]!;
        int maxTiles = offer is StrikeOffer strike ? strike.MaxTiles : 1;
        string verb = offer switch
        {
            EnhanceOffer enhance => $"make {enhance.Enhancement}",
            WildOffer => "make wild",
            _ => "strike",
        };

        _shopContent.AddChild(UiKit.MakeLabel($"Choose {(maxTiles == 1 ? "a tile" : $"up to {maxTiles} tiles")} to {verb}", 26, UiKit.Text));
        _shopContent.AddChild(UiKit.MakeLabel($"{offer.Description} — ${offer.Price}", 16, UiKit.TextMuted));

        var grid = new HFlowContainer();
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var tile in Run.Deck.OrderBy(t => t.IsWild).ThenBy(t => t.Letter.Char).ThenBy(t => t.Enhancement))
        {
            bool chosen = _pickerSelection.Contains(tile.Id);
            var button = UiKit.MakeTile(tile, _session.Scoring.ValueOf(tile), 48, chosen ? UiKit.Selected : UiKit.Newsprint, chosen, blankWild: true);
            button.Pressed += () =>
            {
                if (!_pickerSelection.Remove(tile.Id))
                {
                    if (_pickerSelection.Count >= maxTiles)
                        _pickerSelection.RemoveAt(0);
                    _pickerSelection.Add(tile.Id);
                }
                Refresh();
            };
            grid.AddChild(button);
        }
        _shopContent.AddChild(grid);

        var footer = UiKit.HBox(12);
        var cancel = UiKit.MakeButton("Cancel", UiKit.PanelRaised, 18);
        cancel.Pressed += () =>
        {
            _pickerOffer = null;
            Refresh();
        };
        var confirm = UiKit.MakeButton($"Confirm  ${offer.Price}", UiKit.Good.Darkened(0.25f), 18, UiKit.Money);
        confirm.Disabled = _pickerSelection.Count == 0;
        confirm.Pressed += () =>
        {
            var result = ShopRules.Buy(_session, offerIndex, _pickerSelection.ToArray());
            _pickerOffer = null;
            ApplyShop(result);
        };
        footer.AddChild(cancel);
        footer.AddChild(confirm);
        _shopContent.AddChild(footer);
    }

    // ---------------------------------------------------------------- end of run

    // The end screen's "Unlocked Press Run" line (read by the self-test); null when it isn't shown.
    private Label? _endScreenUnlock;
    private Label? _endScreenDeckUnlock;

    private void BuildEndScreen(bool victory)
    {
        _endScreenUnlock = null;
        _endScreenDeckUnlock = null;
        var holder = new CenterContainer { CustomMinimumSize = new Vector2(0, 600) };
        var panel = UiKit.MakePanel(UiKit.Panel, padding: 40, border: victory ? UiKit.Good : UiKit.Bad, borderWidth: 2);
        var box = UiKit.VBox(14);
        panel.AddChild(box);
        holder.AddChild(panel);

        box.AddChild(UiKit.MakeLabel(victory ? "ALL FIVE WEEKS PUBLISHED" : "MISSED THE DEADLINE", 40,
            victory ? UiKit.Good : UiKit.Bad, HorizontalAlignment.Center));
        string detail = victory
            ? "You won the run!"
            : Round.Deadlocked
                ? "No legal plays and no discards left."
                : $"{Round.Score:N0} of {Round.Config.TargetScore:N0} on Week {_session.Week + 1}, {_session.Kind.Name}.";
        box.AddChild(UiKit.MakeLabel(detail, 20, UiKit.Text, HorizontalAlignment.Center));
        if (victory && _justUnlockedPressRun is { } unlocked)
        {
            var press = PressRuns.Get(unlocked);
            _endScreenUnlock = UiKit.MakeLabel($"Unlocked Press Run {press.Level}: {press.Name}. {press.Adds}", 18,
                new Color(press.Color).Lightened(0.2f), HorizontalAlignment.Center);
            box.AddChild(_endScreenUnlock);
        }
        if (victory && _justUnlockedDeck is { } deck)
        {
            _endScreenDeckUnlock = UiKit.MakeLabel($"Unlocked {deck.Name}: {deck.Upside} {deck.Cost}", 18,
                new Color(deck.Color).Lightened(0.2f), HorizontalAlignment.Center);
            box.AddChild(_endScreenDeckUnlock);
        }
        box.AddChild(UiKit.MakeLabel($"Seed {Run.Seed}   ·   {PressRunText()}   ·   ${Run.Money}   ·   {Run.DeskItems.Length} desk items",
            15, UiKit.TextMuted, HorizontalAlignment.Center));

        var buttons = UiKit.HBox(12);
        var again = UiKit.MakeButton("New run", UiKit.Good.Darkened(0.25f), 22);
        again.Pressed += ChooseNewRun;
        buttons.AddChild(again);
        if (victory)
        {
            var endless = UiKit.MakeButton("Keep going (endless)", UiKit.PanelRaised, 22);
            endless.Pressed += () => ApplyShop(RunRules.ContinueEndless(_session));
            buttons.AddChild(endless);
        }
        var centred = new CenterContainer();
        centred.AddChild(buttons);
        box.AddChild(centred);
        _shopContent.AddChild(holder);
    }
}
