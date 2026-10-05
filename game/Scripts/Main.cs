using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Godot;
using GridPos = Crossword.Core.Domain.Position;

namespace Wordgame.Godot;

/// <summary>
/// Root of the game UI. Holds the current immutable <see cref="GameSession"/> plus purely visual state
/// (selected/pending tiles) and rebuilds the screen from them. All rules go through Crossword.Core.
///
/// Command-line user args (after "--"): --seed=N, --give=id,id (dev), --autoplay=N (play N best moves / leave shops),
/// --hint (pre-place the best play), --screenshot=path.png (save a screenshot after loading and quit).
/// </summary>
public partial class Main : Control
{
    private readonly RunConfig _config = RunConfig.Default;
    private IWordGraph _lexicon = null!;
    private GameSession _session = null!;

    // Visual-only state: tiles picked in the hand and tiles tentatively placed on the board.
    private readonly List<Tile> _selected = new();
    private readonly Dictionary<GridPos, Tile> _pending = new();
    private bool _animating;

    // Sidebar
    private Label _titleLabel = null!;
    private Label _bossLabel = null!;
    private Label _targetLabel = null!;
    private Label _scoreLabel = null!;
    private Label _chipsLabel = null!;
    private Label _multLabel = null!;
    private Label _resourcesLabel = null!;
    private Label _moneyLabel = null!;
    private Label _messageLabel = null!;
    private VBoxContainer _logBox = null!;
    private Label _seedLabel = null!;

    // Centre
    private HBoxContainer _deskRow = null!;
    private Control _roundArea = null!;
    private CenterContainer _boardHolder = null!;
    private HBoxContainer _handRow = null!;
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
        BuildLayout();

        ulong seed = args.TryGetValue("seed", out var s) && ulong.TryParse(s, out var parsed) ? parsed : (ulong)Time.GetTicksUsec();
        NewRun(seed);

        if (args.TryGetValue("give", out var give))
            foreach (var id in give.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (Crossword.Core.DeskItems.DeskItemCatalog.Find(id) is { } item && Run.AddDeskItem(item) is { IsOk: true } added)
                    _session = _session with { Run = added.Value };

        if (args.TryGetValue("autoplay", out var auto) && int.TryParse(auto, out int steps))
            Autoplay(steps);

        Refresh();

        if (args.ContainsKey("hint") && _session.Phase == RunPhase.InRound)
            Hint();

        if (args.TryGetValue("screenshot", out var path))
            _ = ScreenshotAndQuit(path);
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
        ClearLog();
        SetMessage("New run. Click a tile, then a square. Enter submits, Esc recalls.", UiKit.TextMuted);
        Refresh();
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        var background = new ColorRect { Color = UiKit.Background };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var margin = new MarginContainer();
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
    }

    private Control BuildSidebar()
    {
        var panel = UiKit.MakePanel(UiKit.Panel, padding: 16);
        panel.CustomMinimumSize = new Vector2(330, 0);
        var box = UiKit.VBox(10);
        panel.AddChild(box);

        // Sidebar labels wrap so long round names never widen the sidebar and squeeze the board/shop.
        _titleLabel = UiKit.MakeLabel("", 26, UiKit.Text, wrap: true);
        box.AddChild(_titleLabel);
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
        var chipsPanel = UiKit.MakePanel(UiKit.Chips, padding: 8);
        chipsPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _chipsLabel = UiKit.MakeLabel("0", 30, Colors.White, HorizontalAlignment.Center);
        chipsPanel.AddChild(_chipsLabel);
        var multPanel = UiKit.MakePanel(UiKit.Mult, padding: 8);
        multPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _multLabel = UiKit.MakeLabel("0", 30, Colors.White, HorizontalAlignment.Center);
        multPanel.AddChild(_multLabel);
        tally.AddChild(chipsPanel);
        tally.AddChild(UiKit.MakeLabel("×", 28, UiKit.Text));
        tally.AddChild(multPanel);
        box.AddChild(tally);

        _messageLabel = UiKit.MakeLabel("", 16, UiKit.TextMuted, wrap: true);
        _messageLabel.CustomMinimumSize = new Vector2(0, 44);
        box.AddChild(_messageLabel);

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
        box.AddChild(footer);
        return panel;
    }

    // ---------------------------------------------------------------- refresh

    /// <summary>Redraws everything from the current session and visual state.</summary>
    private void Refresh()
    {
        RefreshSidebar();
        RefreshDesk();

        bool inRound = _session.Phase == RunPhase.InRound;
        _roundArea.Visible = inRound;
        _shopArea.Visible = !inRound;

        if (inRound)
        {
            RefreshBoard();
            RefreshHand();
            RefreshButtons();
            if (!_animating)
                UpdatePreview();
        }
        else
        {
            RefreshShopArea();
        }
    }

    private void RefreshSidebar()
    {
        if (_session.Phase == RunPhase.Shop && _messageLabel.Text.StartsWith("New run"))
            SetMessage("Paycheck in. Spend it in the shop, then start the next round.", UiKit.Money);
        _titleLabel.Text = $"Week {_session.Week + 1}/{_config.WeekTargets.Length} · {_session.Kind.Name}";
        _bossLabel.Text = Round.Config.Boss is { } boss
            ? $"SUNDAY BOSS — {boss.Name}: {boss.Description}"
            : $"This Sunday: {_session.WeekBoss.Name} — {_session.WeekBoss.Description}";
        _bossLabel.AddThemeColorOverride("font_color", Round.Config.Boss is null ? UiKit.TextMuted : UiKit.Bad);
        _targetLabel.Text = Round.Config.TargetScore.ToString("N0");
        if (!_animating)
            _scoreLabel.Text = Round.Score.ToString("N0");
        _resourcesLabel.Text = $"Submissions {Round.SubmissionsLeft}   ·   Discards {Round.DiscardsLeft}   ·   Bag {Round.Bag.Count}";
        _moneyLabel.Text = $"${Run.Money}";
        _seedLabel.Text = $"Seed {Run.Seed}";
    }

    private void SetMessage(string text, Color color)
    {
        _messageLabel.Text = text;
        _messageLabel.AddThemeColorOverride("font_color", color);
    }

    private void ClearLog() => UiKit.ClearChildren(_logBox);

    private void AddLog(string text, Color color) => _logBox.AddChild(UiKit.MakeLabel(text, 14, color, wrap: true));
}
