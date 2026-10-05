using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Profile;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Godot;
using GridPos = Crossword.Core.Domain.Position;

namespace Wordgame.Godot;

/// <summary>
/// Root of the game UI. Holds the current immutable <see cref="GameSession"/> plus purely visual state
/// (selected/pending tiles) and rebuilds the screen from them. All rules go through Crossword.Core.
///
/// Command-line user args (after "--"): --seed=N, --give=id,id (dev: Desk Items or Stationery, e.g. answer-key), --autoplay=N (play N best moves / leave shops),
/// --hint (pre-place the best play), --dev (the Hint button shows the best play), --screenshot=path.png (save a screenshot after loading and quit),
/// --selftest (drive the UI with simulated input, print PASS/FAIL, quit), --profile=name (player profile to load/save;
/// default "Player"). --selftest, --screenshot and --autoplay keep the profile in memory so QA runs never touch stats.
/// </summary>
public partial class Main : Control
{
    private readonly RunConfig _config = RunConfig.Default;
    private IWordGraph _lexicon = null!;
    private GameSession _session = null!;

    // Visual-only state: tiles picked in the hand and tiles tentatively placed on the board.
    private readonly List<Tile> _selected = new();
    private readonly Dictionary<GridPos, Tile> _pending = new();

    // Player's arrangement of the hand (tile ids). Cosmetic, so its shuffle RNG is separate from the game's.
    private IReadOnlyList<int> _handOrder = Array.Empty<int>();
    private Crossword.Core.Random.Rng _arrangementRng = Crossword.Core.Random.Rng.FromSeed(Time.GetTicksUsec());
    private bool _animating;
    private bool _devMode;

    // Hand drag in progress: the dragged tile (shown as a ghost in the row), its starting slot, the latest cursor.
    private int? _handDragId;

    // A pending board tile being dragged (to another square or back to the hand).
    private int? _boardDragId;

    // Tiles drawn by the last hand-changing action (highlighted until the next one), and those already animated in.
    private readonly HashSet<int> _newTileIds = new();
    private readonly HashSet<int> _animatedNewTiles = new();
    private int _newTilesRound = -1;
    private int _newTagGeneration;

    // Next alphabetical sort direction.
    private bool _sortDescending;
    private int _ghostHome;
    private Vector2 _cursor;

    // Sidebar
    private Label _titleLabel = null!;
    private HBoxContainer _weekPips = null!;
    private HBoxContainer _dayStrip = null!;
    private Label _bossLabel = null!;
    private Label _targetLabel = null!;
    private Label _scoreLabel = null!;
    private Label _chipsLabel = null!;
    private Label _multLabel = null!;
    private PanelContainer _chipsPanel = null!;
    private PanelContainer _multPanel = null!;

    // Scoring effects: a layer above the UI for floating text/confetti/stamps, and the container that shakes.
    private Control _fxLayer = null!;
    private MarginContainer _shakeRoot = null!;
    private Label _resourcesLabel = null!;
    private Label _moneyLabel = null!;
    private Label _messageLabel = null!;
    private RichTextLabel _definitionsLabel = null!;
    private VBoxContainer _logBox = null!;
    private Label _seedLabel = null!;
    private Button _statsButton = null!;

    // Centre
    private HBoxContainer _deskRow = null!;
    private Label _deskCaption = null!;
    private Control _roundArea = null!;
    private CenterContainer _boardHolder = null!;
    private HandRow _handRow = null!;
    private Button _submitButton = null!;
    private Button _recallButton = null!;
    private Button _discardButton = null!;
    private Button _hintButton = null!;
    private ScrollContainer _shopArea = null!;
    private VBoxContainer _shopContent = null!;

    private RoundState Round => _session.Round;
    private RunState Run => _session.Run;

    public override void _Ready()
    {
        var args = ParseArgs();
        _lexicon = LexiconLoader.Enable;
        _ = Task.Run(() => DefinitionLoader.Default); // warm up off the main thread so the first preview doesn't hitch
        _devMode = args.ContainsKey("dev");
        string profileName = args.TryGetValue("profile", out var named) && named.Length > 0 ? named : "Player";
        bool qaRun = args.ContainsKey("selftest") || args.ContainsKey("screenshot") || args.ContainsKey("autoplay");
        _profile = qaRun ? ProfileStore.InMemory(profileName) : ProfileStore.Load(profileName);
        BuildLayout();

        ulong seed = args.TryGetValue("seed", out var s) && ulong.TryParse(s, out var parsed) ? parsed : (ulong)Time.GetTicksUsec();
        NewRun(seed);
        if (_profile.Notice is { } notice)
            SetMessage(notice, UiKit.Bad);

        if (args.TryGetValue("give", out var give))
            foreach (var id in give.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (Crossword.Core.DeskItems.DeskItemCatalog.Find(id) is { } item && Run.AddDeskItem(item) is { IsOk: true } added)
                    _session = _session with { Run = added.Value };
                else if (Crossword.Core.Stationery.StationeryCatalog.Find(id) is { } stationery && Run.AddStationery(stationery) is { IsOk: true } held)
                    _session = _session with { Run = held.Value };

        if (args.TryGetValue("autoplay", out var auto) && int.TryParse(auto, out int steps))
            Autoplay(steps);

        Refresh();

        if (args.ContainsKey("hint") && _session.Phase == RunPhase.InRound)
            ShowHint(best: true);

        if (args.TryGetValue("screenshot", out var path))
            _ = ScreenshotAndQuit(path);
        else if (args.ContainsKey("selftest"))
            _ = RunSelfTest();
    }

    private static Dictionary<string, string> ParseArgs()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            var trimmed = arg.TrimStart('-');
            int eq = trimmed.IndexOf('=');
            if (eq > 0)
                result[trimmed[..eq]] = trimmed[(eq + 1)..];
            else
                result[trimmed] = "";
        }
        return result;
    }

    private async Task ScreenshotAndQuit(string path)
    {
        for (int i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GetTree().Quit();
    }

    /// <summary>Dev/QA: plays the best move or leaves the shop, <paramref name="steps"/> times, without animation.</summary>
    private void Autoplay(int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            if (_session.Phase == RunPhase.Shop)
            {
                _session = RunRules.LeaveShop(_session, _lexicon).Value;
                continue;
            }
            if (_session.Phase != RunPhase.InRound || BestPlay() is not { } best)
                return;
            _session = RunRules.Submit(_session, best.Play.Placed, _lexicon).Value.Session;
        }
    }

    private void NewRun(ulong seed)
    {
        _session = RunRules.NewGame(seed, _config, _lexicon);
        _selected.Clear();
        _pending.Clear();
        _newTileIds.Clear();
        _handOrder = Array.Empty<int>();
        _profile.Update(StatsRules.RecordRunStart);
        _runEndRecorded = false;
        _roundWonRecorded = -1;
        ClearLog();
        SetMessage("New run. Click or drag a tile onto a square. Drag tiles in your hand to reorder; Space shuffles.", UiKit.TextMuted);
        Refresh();
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        var background = new ColorRect { Color = UiKit.Background };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var margin = new MarginContainer();
        _shakeRoot = margin;
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 18);
        AddChild(margin);

        var columns = UiKit.HBox(18);
        margin.AddChild(columns);
        columns.AddChild(BuildSidebar());

        var centre = UiKit.VBox(14);
        centre.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(centre);

        _deskRow = UiKit.HBox(10);
        centre.AddChild(_deskRow);
        _deskCaption = UiKit.MakeLabel(
            "Desk Items apply left to right: put +Chips and +Mult items before ×Mult ones. Hover ◀ ▶ to compare scores.",
            13, UiKit.TextMuted);
        centre.AddChild(_deskCaption);

        var content = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        centre.AddChild(content);

        _roundArea = BuildRoundArea();
        _roundArea.SetAnchorsPreset(LayoutPreset.FullRect);
        content.AddChild(_roundArea);

        _shopArea = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _shopArea.SetAnchorsPreset(LayoutPreset.FullRect);
        _shopContent = UiKit.VBox(16);
        _shopContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _shopArea.AddChild(_shopContent);
        content.AddChild(_shopArea);

        _fxLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _fxLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_fxLayer);

        _styleGuidesOverlay = BuildStyleGuidesOverlay();
        AddChild(_styleGuidesOverlay);
        _statsOverlay = BuildStatsOverlay();
        AddChild(_statsOverlay);
        _wildOverlay = BuildWildOverlay();
        AddChild(_wildOverlay);
    }

    private Control BuildSidebar()
    {
        var panel = UiKit.MakePanel(UiKit.Panel, padding: 16);
        panel.CustomMinimumSize = new Vector2(330, 0);
        var box = UiKit.VBox(10);
        panel.AddChild(box);

        // Sidebar labels wrap so long round names never widen the sidebar and squeeze the board/shop.
        _titleLabel = UiKit.MakeLabel("", 22, UiKit.Text, wrap: true);
        box.AddChild(_titleLabel);
        _weekPips = UiKit.HBox(6);
        box.AddChild(_weekPips);
        _dayStrip = UiKit.HBox(6);
        box.AddChild(_dayStrip);
        _bossLabel = UiKit.MakeLabel("", 15, UiKit.TextMuted, wrap: true);
        box.AddChild(_bossLabel);
        box.AddChild(new HSeparator());

        box.AddChild(UiKit.MakeLabel("DEADLINE", 13, UiKit.TextMuted));
        _targetLabel = UiKit.MakeLabel("", 34, UiKit.Mult);
        box.AddChild(_targetLabel);
        box.AddChild(UiKit.MakeLabel("SCORE", 13, UiKit.TextMuted));
        _scoreLabel = UiKit.MakeLabel("", 34, UiKit.Text);
        box.AddChild(_scoreLabel);

        var tally = UiKit.HBox(8);
        _chipsPanel = UiKit.MakePanel(UiKit.Chips, padding: 8);
        _chipsPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _chipsLabel = UiKit.MakeLabel("0", 30, Colors.White, HorizontalAlignment.Center);
        _chipsPanel.AddChild(_chipsLabel);
        _multPanel = UiKit.MakePanel(UiKit.Mult, padding: 8);
        _multPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _multLabel = UiKit.MakeLabel("0", 30, Colors.White, HorizontalAlignment.Center);
        _multPanel.AddChild(_multLabel);
        tally.AddChild(_chipsPanel);
        tally.AddChild(UiKit.MakeLabel("×", 28, UiKit.Text));
        tally.AddChild(_multPanel);
        box.AddChild(tally);

        _messageLabel = UiKit.MakeLabel("", 16, UiKit.TextMuted, wrap: true);
        _messageLabel.CustomMinimumSize = new Vector2(0, 44);
        box.AddChild(_messageLabel);

        // Definitions of the words in the pending play (Open English WordNet + hand-written supplement).
        _definitionsLabel = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore };
        _definitionsLabel.AddThemeFontSizeOverride("normal_font_size", 14);
        _definitionsLabel.AddThemeFontSizeOverride("bold_font_size", 14);
        _definitionsLabel.AddThemeFontSizeOverride("italics_font_size", 14);
        _definitionsLabel.AddThemeColorOverride("default_color", UiKit.TextMuted);
        box.AddChild(_definitionsLabel);

        _resourcesLabel = UiKit.MakeLabel("", 17, UiKit.Text, wrap: true);
        box.AddChild(_resourcesLabel);
        _moneyLabel = UiKit.MakeLabel("", 24, UiKit.Money);
        box.AddChild(_moneyLabel);
        box.AddChild(new HSeparator());

        var logScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _logBox = UiKit.VBox(2);
        _logBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        logScroll.AddChild(_logBox);
        box.AddChild(logScroll);

        var footer = UiKit.HBox(8);
        _seedLabel = UiKit.MakeLabel("", 13, UiKit.TextMuted);
        _seedLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(_seedLabel);
        var newRun = UiKit.MakeButton("New run", UiKit.PanelRaised, 14);
        newRun.Pressed += () => NewRun((ulong)Time.GetTicksUsec());
        footer.AddChild(newRun);
        var guides = UiKit.MakeButton("Guides  Tab", UiKit.PanelRaised, 14);
        guides.TooltipText = "Style Guides: every word tier's level and chips × mult";
        guides.Pressed += ToggleStyleGuides;
        footer.AddChild(guides);
        _statsButton = UiKit.MakeButton("Stats", UiKit.PanelRaised, 14);
        _statsButton.Pressed += ToggleStats;
        footer.AddChild(_statsButton);
        box.AddChild(footer);
        return panel;
    }

    // ---------------------------------------------------------------- refresh

    /// <summary>Redraws everything from the current session and visual state.</summary>
    private void Refresh()
    {
        RecordRoundWonIfDone();
        RecordRunEndIfOver();
        RefreshSidebar();
        RefreshDesk();

        // A winning play keeps the board on screen until its scoring animation finishes.
        bool inRound = _session.Phase == RunPhase.InRound || _animating;
        _roundArea.Visible = inRound;
        _shopArea.Visible = !inRound;

        if (inRound)
        {
            RefreshBoard();
            RefreshClues();
            RefreshHand();
            RefreshButtons();
            if (!_animating)
                UpdatePreview();
        }
        else
        {
            RefreshShopArea();
        }

        if (_styleGuidesOverlay.Visible)
            RefreshStyleGuides();
    }

    private void RefreshSidebar()
    {
        if (_session.Phase == RunPhase.Shop && _messageLabel.Text.StartsWith("New run"))
            SetMessage("Paycheck in. Spend it in the shop, then start the next round.", UiKit.Money);
        RefreshProgress();
        _targetLabel.Text = Round.Config.TargetScore.ToString("N0");
        if (!_animating)
            _scoreLabel.Text = Round.Score.ToString("N0");
        _resourcesLabel.Text = $"Submissions {Round.SubmissionsLeft}   ·   Discards {Round.DiscardsLeft}   ·   Bag {Round.Bag.Count}"
            + (Round.Config.BonusMult > 0 ? $"   ·   Red ink +{Round.Config.BonusMult:0.##} mult" : "");
        _moneyLabel.Text = $"${Run.Money}";
        _seedLabel.Text = $"Seed {Run.Seed}";
    }

    /// <summary>Week header, week pips, this week's three puzzles and how far away the boss is.</summary>
    private void RefreshProgress()
    {
        var progress = RunProgress.For(_config, Run.RoundIndex);
        bool solved = _session.Phase != RunPhase.InRound && Round.Status == RoundStatus.Won;
        var boss = Round.Config.Boss ?? _session.WeekBoss;
        _titleLabel.Text = progress.Endless ? $"ENDLESS · WEEK {progress.Week + 1}" : $"WEEK {progress.Week + 1} OF {progress.WeekCount}";

        UiKit.ClearChildren(_weekPips);
        _weekPips.Visible = !progress.Endless;
        for (int week = 0; week < progress.WeekCount && !progress.Endless; week++)
        {
            var color = week < progress.Week ? UiKit.Good : week == progress.Week ? UiKit.Selected : UiKit.PanelBorder;
            var pip = new Panel { CustomMinimumSize = new Vector2(0, 6), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            pip.AddThemeStyleboxOverride("panel", UiKit.Box(color, 3, padding: 0));
            _weekPips.AddChild(pip);
        }

        UiKit.ClearChildren(_dayStrip);
        for (int day = 0; day < progress.Days.Length; day++)
        {
            var kind = progress.Days[day];
            bool done = day < progress.Day || (day == progress.Day && solved);
            bool current = day == progress.Day && !solved;
            var accent = kind.IsBoss ? UiKit.Bad : UiKit.Selected;
            var step = UiKit.MakePanel(current ? UiKit.PanelRaised : UiKit.Panel, padding: 6, radius: 6,
                border: current ? accent : done ? UiKit.Good : UiKit.PanelBorder, borderWidth: current ? 2 : 1);
            step.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            step.SizeFlagsStretchRatio = 1;
            string text = (done ? "DONE\n" : current ? "NOW\n" : "\n") + kind.Name + (kind.IsBoss ? $"\n{boss.Name}" : "");
            var color = current ? UiKit.Text : done ? UiKit.Good : kind.IsBoss ? UiKit.Bad : UiKit.TextMuted;
            step.AddChild(UiKit.MakeLabel(text, 12, color, HorizontalAlignment.Center, wrap: true));
            _dayStrip.AddChild(step);
        }

        string bossDay = progress.Days[progress.BossDay].Name;
        int left = progress.PuzzlesUntilBoss - (solved ? 1 : 0);
        _bossLabel.Text = progress.IsBossDay
            ? (solved ? "Week complete!" : $"BOSS ROUND — {boss.Name}: {boss.Description}")
            : $"{left} puzzle{(left == 1 ? "" : "s")} until the {bossDay} — {boss.Name}: {boss.Description}";
        _bossLabel.AddThemeColorOverride("font_color", progress.IsBossDay && !solved ? UiKit.Bad : UiKit.TextMuted);
    }

    /// <summary>Remembers which hand tiles a transition drew, so they can be highlighted until the next one.</summary>
    private void MarkNewTiles(IReadOnlyCollection<int> handBefore)
    {
        _newTileIds.Clear();
        _animatedNewTiles.Clear();
        _newTilesRound = Run.RoundIndex;
        if (_session.Phase != RunPhase.InRound)
            return;
        foreach (var tile in Round.Hand.Tiles)
            if (!handBefore.Contains(tile.Id))
                _newTileIds.Add(tile.Id);
        int generation = ++_newTagGeneration;
        if (_newTileIds.Count > 0)
            GetTree().CreateTimer(Juice.NewTagSeconds).Timeout += () => FadeNewTags(generation);
    }

    /// <summary>Fades out the NEW tags from <paramref name="generation"/> (unless a newer action replaced them).</summary>
    private void FadeNewTags(int generation)
    {
        if (generation != _newTagGeneration)
            return;
        var marks = NewTagNodes();
        if (marks.Count == 0)
        {
            _newTileIds.Clear();
            return;
        }
        var tween = CreateTween().SetParallel();
        foreach (var mark in marks)
            tween.TweenProperty(mark, "modulate:a", 0f, Juice.NewTagFadeSeconds);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            if (generation == _newTagGeneration)
                _newTileIds.Clear();
        }));
    }

    /// <summary>The first touch of the hand (select, drag, place, sort, shuffle) clears the NEW tags at once.</summary>
    private void DismissNewTags()
    {
        if (_newTileIds.Count == 0)
            return;
        _newTileIds.Clear();
        _newTagGeneration++;
        foreach (var mark in NewTagNodes())
            mark.QueueFree();
    }

    private List<Control> NewTagNodes() => _handRow.GetChildren().OfType<TileButton>()
        .SelectMany(b => b.GetChildren().OfType<Control>().Where(c => c.Name == "NewTag" || c.Name == "NewOutline"))
        .ToList();

    private int[] HandIds() => Round.Hand.Tiles.Select(t => t.Id).ToArray();

    private void SetMessage(string text, Color color)
    {
        _messageLabel.Text = text;
        _messageLabel.AddThemeColorOverride("font_color", color);
    }

    private void ClearLog() => UiKit.ClearChildren(_logBox);

    private Label AddLog(string text, Color color)
    {
        var label = UiKit.MakeLabel(text, 14, color, wrap: true);
        _logBox.AddChild(label);
        return label;
    }
}
