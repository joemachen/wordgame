using Crossword.Core.Lexicon;
using Crossword.Core.Profile;
using Crossword.Core.Run;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// New-run picker: decks on the left, the selected deck's Press Runs on the right; clicking a Press Run starts the run.
/// A run won unlocks the next deck, and a win at level N with a deck unlocks N + 1 for that deck; locked decks and
/// levels are shown greyed so both ladders are visible. An optional seed box starts a seeded run, which unlocks nothing.
/// </summary>
public partial class Main
{
    private Control _pressRunOverlay = null!;
    private VBoxContainer _pressRunBox = null!;
    private string _pickerDeck = Decks.StandardId;
    private string _pickerDictionary = Dictionaries.All[0].Id;
    private string _pickerSeed = "";
    private Label? _pickerSeedError;

    // Set when the run that just ended unlocked a new Press Run / deck (shown on the victory screen).
    private int? _justUnlockedPressRun;
    private DeckDefinition? _justUnlockedDeck;
    private DictionaryDefinition? _justUnlockedDictionary;

    private Control BuildPressRunOverlay()
    {
        var overlay = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                overlay.Visible = false; // click outside the panel closes
        };

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(centre);

        var panel = UiKit.MakePanel(UiKit.Panel, padding: 20, radius: 12, border: UiKit.Selected, borderWidth: 2);
        panel.MouseFilter = MouseFilterEnum.Stop;
        _pressRunBox = UiKit.VBox(8);
        panel.AddChild(_pressRunBox);
        centre.AddChild(panel);
        return overlay;
    }

    /// <summary>Opens the New-run picker (deck, Press Run, optional seed).</summary>
    private void ChooseNewRun()
    {
        var stats = _profile.Profile.Stats;
        _pickerSeed = "";
        // Start from the last run's deck when it's still unlocked.
        _pickerDeck = StatsQueries.UnlockedDecks(stats).Any(d => d.Id == Run.DeckId) ? Run.DeckId : Decks.StandardId;
        var dictionaries = StatsQueries.UnlockedDictionaries(stats);
        _pickerDictionary = dictionaries.FirstOrDefault(d => Run.Dictionaries.Contains(d.Id))?.Id ?? Dictionaries.All[0].Id;
        RefreshPressRuns();
        _pressRunOverlay.Visible = true;
    }

    private void RefreshPressRuns()
    {
        var stats = _profile.Profile.Stats;
        UiKit.ClearChildren(_pressRunBox);
        _pressRunBox.AddChild(UiKit.MakeLabel("Start a new run", 26, UiKit.Text));
        var intro = UiKit.MakeLabel("Pick a deck, then a Press Run to start. Each Press Run adds its rule to every rule above it. "
            + "Every run you win unlocks the next deck (and, after The Lexicographer's Deck, the next dictionary); "
            + "a win with a deck unlocks its next Press Run.", 14, UiKit.TextMuted, wrap: true);
        intro.CustomMinimumSize = new Vector2(900, 0);
        _pressRunBox.AddChild(intro);

        var columns = UiKit.HBox(16);
        _pressRunBox.AddChild(columns);

        var decks = UiKit.VBox(8);
        decks.AddChild(UiKit.MakeLabel("Deck", 16, UiKit.TextMuted));
        foreach (var deck in Decks.All)
            decks.AddChild(DeckCard(deck, StatsQueries.WinsToUnlock(stats, deck)));
        columns.AddChild(decks);

        var levels = UiKit.VBox(8);
        if (Decks.TakesDictionary(_pickerDeck))
        {
            string warm = _pickerDictionary;
            _ = Task.Run(() => LexiconLoader.For([warm])); // build the run's word graph before a Press Run is clicked
            levels.AddChild(UiKit.MakeLabel("Dictionary", 16, UiKit.TextMuted));
            var choices = UiKit.HBox(8);
            foreach (var dictionary in Dictionaries.All)
                choices.AddChild(DictionaryButton(dictionary, StatsQueries.WinsToUnlock(stats, dictionary)));
            levels.AddChild(choices);
        }
        int unlocked = StatsQueries.UnlockedPressRun(stats, _pickerDeck);
        levels.AddChild(UiKit.MakeLabel($"Press Run · {Decks.Get(_pickerDeck).Name}", 16, UiKit.TextMuted));
        foreach (var press in PressRuns.All)
            levels.AddChild(PressRunRow(press, press.Level <= unlocked));
        columns.AddChild(levels);

        var footer = UiKit.HBox(10);
        var seed = UiKit.MakeLineEdit("Seed (optional)", 200);
        seed.Name = "SeedField";
        seed.MaxLength = 20;
        seed.Text = _pickerSeed;
        seed.TextChanged += text =>
        {
            _pickerSeed = text;
            if (_pickerSeedError is not null)
                _pickerSeedError.Text = "";
        };
        footer.AddChild(seed);
        _pickerSeedError = UiKit.MakeLabel("", 13, UiKit.Bad);
        footer.AddChild(UiKit.MakeLabel("Same seed, same boards and bosses. Seeded runs unlock nothing.", 13, UiKit.TextMuted));
        footer.AddChild(_pickerSeedError);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        footer.AddChild(spacer);
        footer.AddChild(UiKit.MakeLabel("Esc to close", 13, UiKit.TextMuted));
        _pressRunBox.AddChild(footer);
    }

    private Control DeckCard(DeckDefinition deck, int winsToUnlock)
    {
        bool open = winsToUnlock == 0;
        bool selected = deck.Id == _pickerDeck;
        var color = new Color(deck.Color);
        var card = UiKit.MakeButton("", open ? UiKit.PanelRaised : UiKit.Background, 16);
        card.Name = $"Deck_{deck.Id}";
        card.Disabled = !open;
        card.CustomMinimumSize = new Vector2(300, 84);
        card.AddThemeStyleboxOverride("normal", UiKit.Box(open ? UiKit.PanelRaised : UiKit.Background, 8,
            selected ? color : UiKit.PanelBorder, selected ? 3 : 1, 8));
        card.AddThemeStyleboxOverride("hover", UiKit.Box(UiKit.PanelRaised.Lightened(0.08f), 8, color, 3, 8));
        card.AddThemeStyleboxOverride("disabled", UiKit.Box(UiKit.Background, 8, UiKit.PanelBorder, 1, 8));
        if (open)
            card.Pressed += () =>
            {
                _pickerDeck = deck.Id;
                RefreshPressRuns();
            };

        var lines = UiKit.VBox(2);
        lines.SetAnchorsPreset(LayoutPreset.FullRect);
        lines.OffsetLeft = 12;
        lines.OffsetTop = 8;
        lines.OffsetRight = -12;
        card.AddChild(lines);
        lines.AddChild(UiKit.MakeLabel(deck.Name, 17, open ? color.Lightened(0.2f) : UiKit.TextMuted));
        if (open)
        {
            lines.AddChild(UiKit.MakeLabel($"+ {deck.Upside}", 13, UiKit.Good, wrap: true));
            if (deck.Cost.Length > 0)
                lines.AddChild(UiKit.MakeLabel($"− {deck.Cost}", 13, UiKit.Bad, wrap: true));
        }
        else
        {
            lines.AddChild(UiKit.MakeLabel(winsToUnlock == 1 ? "Win 1 more run to unlock." : $"Win {winsToUnlock} more runs to unlock.",
                13, UiKit.TextMuted));
        }
        foreach (var child in lines.FindChildren("*", "Control", owned: false).OfType<Control>().Append(lines))
            child.MouseFilter = MouseFilterEnum.Ignore;
        return card;
    }

    /// <summary>A dictionary choice for a deck that takes one; the selected one gets the deck's color.</summary>
    private Control DictionaryButton(DictionaryDefinition dictionary, int winsToUnlock)
    {
        bool open = winsToUnlock == 0;
        bool selected = open && dictionary.Id == _pickerDictionary;
        var color = new Color(Decks.Get(_pickerDeck).Color);
        var button = UiKit.MakeButton(open ? dictionary.Name : $"{dictionary.Name} (win {winsToUnlock} more)", open ? UiKit.PanelRaised : UiKit.Background, 15);
        button.Name = $"Dictionary_{dictionary.Id}";
        button.Disabled = !open;
        button.TooltipText = $"{dictionary.Kind}: {dictionary.Description}";
        button.CustomMinimumSize = new Vector2(190, 40);
        button.AddThemeStyleboxOverride("normal", UiKit.Box(UiKit.PanelRaised, 8, selected ? color : UiKit.PanelBorder, selected ? 3 : 1, 8));
        button.AddThemeStyleboxOverride("hover", UiKit.Box(UiKit.PanelRaised.Lightened(0.08f), 8, color, 3, 8));
        button.AddThemeStyleboxOverride("disabled", UiKit.Box(UiKit.Background, 8, UiKit.PanelBorder, 1, 8));
        if (open)
            button.Pressed += () =>
            {
                _pickerDictionary = dictionary.Id;
                RefreshPressRuns();
            };
        return button;
    }

    private Control PressRunRow(PressRun press, bool open)
    {
        var color = new Color(press.Color);
        var row = UiKit.MakeButton("", open ? UiKit.PanelRaised : UiKit.Background, 16);
        row.Name = $"PressRun{press.Level}";
        row.Disabled = !open;
        row.CustomMinimumSize = new Vector2(580, 46);
        row.AddThemeStyleboxOverride("normal", UiKit.Box(open ? UiKit.PanelRaised : UiKit.Background, 8, open ? color : UiKit.PanelBorder, 2, 8));
        row.AddThemeStyleboxOverride("hover", UiKit.Box(UiKit.PanelRaised.Lightened(0.08f), 8, color, 3, 8));
        row.AddThemeStyleboxOverride("disabled", UiKit.Box(UiKit.Background, 8, UiKit.PanelBorder, 1, 8));
        if (open)
            row.Pressed += () => PickPressRun(press.Level);

        var cells = UiKit.HBox(12);
        cells.SetAnchorsPreset(LayoutPreset.FullRect);
        cells.OffsetLeft = 12;
        cells.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(cells);

        var chip = new Panel { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", UiKit.Box(open ? color : UiKit.PanelBorder, 7, padding: 0));
        cells.AddChild(chip);
        cells.AddChild(Cell($"{press.Level}", 24, 18, open ? UiKit.Text : UiKit.TextMuted));
        cells.AddChild(Cell(press.Name, 150, 17, open ? color.Lightened(0.2f) : UiKit.TextMuted));
        cells.AddChild(Cell(open ? press.Adds : $"Win Press Run {press.Level - 1} with this deck to unlock.", 340, 13,
            open ? UiKit.Text : UiKit.TextMuted));
        foreach (var child in cells.GetChildren().OfType<Control>())
            child.MouseFilter = MouseFilterEnum.Ignore;
        return row;
    }

    private void PickPressRun(int level)
    {
        string typed = _pickerSeed.Trim();
        ulong seed = (ulong)Time.GetTicksUsec();
        if (typed.Length > 0 && !ulong.TryParse(typed, out seed))
        {
            if (_pickerSeedError is not null)
                _pickerSeedError.Text = "A seed is a whole number, like 42.";
            return;
        }
        _pressRunOverlay.Visible = false;
        NewRun(seed, pressRun: level, deck: _pickerDeck,
            dictionary: Decks.TakesDictionary(_pickerDeck) ? _pickerDictionary : null, seeded: typed.Length > 0);
    }

    /// <summary>The run's seed for the sidebar and end screen, marked when the player chose it.</summary>
    private string SeedText() => Run.Seeded ? $"Seed {Run.Seed} (chosen)" : $"Seed {Run.Seed}";

    /// <summary>
    /// The run's deck, dictionaries and Press Run for the sidebar footer and end screen, e.g.
    /// "The Redactor Deck · Press Run 3 · Late Edition" or "The Lexicographer's Deck · The Tech Shorthand · Press Run 1 · Proofreader".
    /// </summary>
    private string PressRunText() =>
        (Run.DeckId != Decks.StandardId ? $"{Decks.Get(Run.DeckId).Name} · " : "")
        + string.Concat(Run.Dictionaries.Select(id => $"{Dictionaries.Get(id).Name} · "))
        + $"Press Run {Run.PressRun} · {PressRuns.Get(Run.PressRun).Name}";

    /// <summary>Whether the run differs from the base game (non-standard deck or a Press Run above Proofreader).</summary>
    private bool IsCustomRun => Run.DeckId != Decks.StandardId || Run.PressRun > PressRuns.Lowest;
}
