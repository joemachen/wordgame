using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Run;
using Crossword.Core.Scoring;
using Crossword.Core.Stationery;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// How to play: the rules in four tabs (Basics, Scoring, Desk Items, Stationery &amp; shop), opened from the title or the
/// pause screen. Every number comes from the live config, so retuning never leaves the text out of date; the Desk Item
/// and Stationery lists come straight from the catalogs.
/// </summary>
public partial class Main
{
    private static readonly string[] HowToPlayTabs = ["Basics", "Scoring", "Desk Items", "Stationery & shop"];

    private Control _howToPlayOverlay = null!;
    private VBoxContainer _howToPlayBox = null!;
    private HBoxContainer _howToPlayTabRow = null!;
    private ScrollContainer _howToPlayScroll = null!;
    private int _howToPlayTab;

    private Control BuildHowToPlayOverlay()
    {
        var overlay = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                HideHowToPlay(); // click outside the panel closes
        };
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);
        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(centre);

        var panel = UiKit.MakePanel(UiKit.Panel, padding: 22, radius: 12, border: UiKit.Chips, borderWidth: 2);
        panel.MouseFilter = MouseFilterEnum.Stop;
        var layout = UiKit.VBox(12);
        panel.AddChild(layout);
        centre.AddChild(panel);

        layout.AddChild(UiKit.MakeLabel("How to play", 28, UiKit.Text));
        _howToPlayTabRow = UiKit.HBox(8);
        for (int i = 0; i < HowToPlayTabs.Length; i++)
        {
            int tab = i;
            var button = UiKit.MakeButton(HowToPlayTabs[i], UiKit.PanelRaised, 16);
            button.Name = $"HowTo_{i}";
            button.Pressed += () => ShowHowToPlayTab(tab);
            _howToPlayTabRow.AddChild(button);
        }
        layout.AddChild(_howToPlayTabRow);

        _howToPlayScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(940, 600),
        };
        _howToPlayBox = UiKit.VBox(8);
        _howToPlayBox.CustomMinimumSize = new Vector2(920, 0);
        _howToPlayScroll.AddChild(_howToPlayBox);
        layout.AddChild(_howToPlayScroll);
        layout.AddChild(UiKit.MakeLabel("Esc or click outside to close", 13, UiKit.TextMuted, HorizontalAlignment.Right));
        return overlay;
    }

    private void ShowHowToPlay()
    {
        ShowHowToPlayTab(_howToPlayTab);
        _howToPlayOverlay.Visible = true;
    }

    private void HideHowToPlay() => _howToPlayOverlay.Visible = false;

    private void ShowHowToPlayTab(int tab)
    {
        _howToPlayTab = tab;
        foreach (var button in _howToPlayTabRow.GetChildren().OfType<Button>())
        {
            bool selected = button.Name == $"HowTo_{tab}";
            var bg = selected ? UiKit.Chips.Darkened(0.35f) : UiKit.PanelRaised;
            foreach (var state in new[] { "normal", "hover", "pressed" })
                button.AddThemeStyleboxOverride(state, UiKit.Box(state == "hover" ? bg.Lightened(0.1f) : bg, padding: 10,
                    border: selected ? UiKit.Chips : null, borderWidth: selected ? 2 : 0));
        }
        UiKit.ClearChildren(_howToPlayBox);
        var config = _session?.Config ?? _baseConfig;
        var scoring = _session?.Scoring ?? config.Scoring;
        switch (tab)
        {
            case 0: BasicsTab(config); break;
            case 1: ScoringTab(scoring); break;
            case 2: DeskItemsTab(); break;
            default: ShopTab(config); break;
        }
        _howToPlayScroll.ScrollVertical = 0;
    }

    private void BasicsTab(RunConfig config)
    {
        var round = config.RoundConfigFor(0);
        HowHeading("The run");
        HowText($"A run is {config.WeekTargets.Length} weeks. Each week has {string.Join(", ", config.Days.Select(d => d.Name))}. "
            + "Every puzzle has a deadline: score at least that many points before you run out of submissions. "
            + "Clear it and you're paid, then you visit the shop. Miss it and the run is over.");
        HowText("The Sunday Edition is a boss: it adds a rule for that round (a smaller grid, fewer submissions…). "
            + "You can see the week's boss in the sidebar before you reach it.");
        HowHeading("A round");
        HowText($"You hold {round.HandSize} tiles. Each round you have {round.Submissions} submissions and {round.Discards} discards. "
            + "A submission plays tiles onto the board and scores them; a discard throws away the tiles you selected and draws new ones.");
        HowText("The board stays for the whole round, so later words can cross earlier ones.");
        HowHeading("Placing tiles");
        HowText("Tiles you place in one submission must sit in one row or one column; they may have board tiles between them. "
            + "After the first play, a new play must touch tiles already on the board. "
            + "Every word the play makes, across and down, must be a real word.");
        HowText("Click a hand tile, then a square (or drag it). Enter submits, Esc takes the tiles back, Space shuffles your hand. "
            + "A wild tile (\"?\") can be any letter you choose, but scores no letter chips.");
        HowHeading("Help");
        HowText("The Hint button shows a decent play for your hand — never the best one. The Answer Key Stationery reveals the best play.");
    }

    private void ScoringTab(ScoringConfig scoring)
    {
        HowHeading("Chips × Mult");
        HowText("Every play scores chips × mult. The longest word you make sets the starting chips and mult; "
            + "every word you make adds its letters' chips; then bonuses add more.");
        HowHeading("Word length");
        var tiers = scoring.Tiers;
        HowText(string.Join("\n", tiers.Select((t, i) =>
            $"{t.Label(i == tiers.Length - 1)}: {t.BaseChips} chips × {t.BaseMult:0.##} mult  ·  Style Guide: {StyleGuideNames.For(t.MinLength, t.Label(i == tiers.Length - 1))}")));
        HowText("A Style Guide (bought in the shop) permanently levels its word length up for the rest of the run. Tab opens the Style Guides drawer.");
        HowHeading("Letters");
        HowText(string.Join("   ·   ", scoring.LetterValues.GroupBy(kv => kv.Value).OrderBy(g => g.Key)
            .Select(g => $"{g.Key}: {string.Join(" ", g.Select(kv => kv.Key).Order())}")));
        HowHeading("Premium squares");
        HowText("2L and 3L double or triple a letter; 2W and 3W double or triple the whole word. They only count under tiles placed "
            + "in that play — once covered, a premium is used up.");
        HowHeading("Crossings");
        HowText($"Each new tile that sits in both an across word and a down word adds +{scoring.IntersectionMult:0.##} mult. "
            + "Building into the grid usually beats one long word on its own.");
        HowHeading("Enhanced tiles");
        HowText(string.Join("\n", new[] { TileEnhancement.Bold, TileEnhancement.Italic, TileEnhancement.Gilded }.Select(scoring.Describe)));
    }

    private void DeskItemsTab()
    {
        HowHeading("They fire left to right");
        HowText("Desk Items change every play's score, one after another from slot 1 to the last. Order matters: a ×Mult item "
            + "multiplies only the mult that's already there.");
        HowText("Example: a play worth 10 × 2. Red Pen (+2 mult) then Pulitzer (×1.5) gives 10 × (2 + 2) × 1.5 = 60. "
            + "Pulitzer first gives 10 × (2 × 1.5 + 2) = 50.");
        HowText("You don't have to arrange them: the game keeps your Desk Items in the best order — +Chips first, then +Mult, "
            + "then ×Mult, then ×Chips ×Mult — every time one is bought or sold. The tag on each card says which kind it is.");
        foreach (var kind in Enum.GetValues<DeskItemKind>().OrderBy(DeskOrder.Rank))
        {
            var items = DeskItemCatalog.All.Where(item => item.Kind == kind).ToList();
            if (items.Count == 0)
                continue;
            HowHeading($"{DeskItemKinds.Tag(kind)}  —  {DeskItemKinds.Explain(kind)}");
            foreach (var item in items.OrderBy(i => i.Rarity).ThenBy(i => i.Name))
                HowRich($"[b]{item.Name}[/b]  [color=#{UiKit.TextMuted.ToHtml(false)}]{item.Rarity}[/color]  {item.Description}");
        }
    }

    private void ShopTab(RunConfig config)
    {
        var shop = config.Shop;
        HowHeading("Stationery");
        HowText($"Stationery are one-use items. You can hold {RunState.MaxStationerySlots}; use them during a round with their Use button "
            + "(some need you to select hand tiles or click the board first).");
        foreach (var item in StationeryCatalog.All)
            HowRich($"[b]{item.Name}[/b]  [color=#{UiKit.Money.ToHtml(false)}]${shop.PriceOf(item)}[/color]  {item.Description}");
        HowHeading("The shop");
        HowText($"After every cleared puzzle the shop offers {shop.DeskItemOffers} Desk Items, {shop.EditOffers} deck edits, "
            + $"{shop.StyleGuideOffers} Style Guide and {shop.StationeryOffers} Stationery. Reroll for new offers "
            + $"(${shop.RerollBaseCost}, then $1 more each time). Anything you own sells for half its price.");
        HowText($"Desk Items cost ${shop.CommonPrice} (Common), ${shop.UncommonPrice} (Uncommon), ${shop.RarePrice} (Rare), "
            + $"${shop.EpicPrice} (Epic) or ${shop.LegendaryPrice} (Legendary).");
        HowHeading("Your paycheck");
        HowText(Economy.HowToEarnMore(config.Economy));
    }

    private void HowHeading(string text)
    {
        var label = UiKit.MakeLabel(text, 18, UiKit.Text, wrap: true);
        label.CustomMinimumSize = new Vector2(900, 0);
        _howToPlayBox.AddChild(label);
    }

    private void HowText(string text)
    {
        var label = UiKit.MakeLabel(text, 14, UiKit.TextMuted, wrap: true);
        label.CustomMinimumSize = new Vector2(900, 0);
        _howToPlayBox.AddChild(label);
    }

    private void HowRich(string bbcode)
    {
        var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore };
        label.CustomMinimumSize = new Vector2(900, 0);
        label.AddThemeFontSizeOverride("normal_font_size", UiKit.FontSize(14));
        label.AddThemeFontSizeOverride("bold_font_size", UiKit.FontSize(14));
        label.AddThemeColorOverride("default_color", UiKit.TextMuted);
        label.Text = bbcode;
        _howToPlayBox.AddChild(label);
    }
}
