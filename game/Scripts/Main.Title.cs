using Crossword.Core.Run;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Title menu, shown on launch: Continue (the default; Enter) resumes the profile's run, New run opens the New-run
/// picker (abandoning a run in progress takes a second click), Profile switches or creates profiles, Stats,
/// Settings, Quit.
/// The sidebar's Menu button brings it back. QA and dev-setup launches skip it and go straight into a run.
/// </summary>
public partial class Main
{
    private Control _titleOverlay = null!;
    private Button _titleContinue = null!;
    private Label _titleContinueDetail = null!;
    private Button _titleNewRun = null!;
    private Button _titleProfile = null!;
    private Button _titleSettings = null!;
    private Label _titlePlayingAs = null!;
    private Label _titleNotice = null!;
    private ulong _newRunArmedUntil;

    private Control BuildTitleOverlay()
    {
        var overlay = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = UiKit.Background, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(background);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(centre);
        var box = UiKit.VBox(12);
        box.CustomMinimumSize = new Vector2(440, 0);
        centre.AddChild(box);

        box.AddChild(UiKit.MakeLabel("wordgame", 64, UiKit.Text, HorizontalAlignment.Center));
        box.AddChild(UiKit.MakeLabel("A crossword roguelike", 18, UiKit.TextMuted, HorizontalAlignment.Center));
        _titlePlayingAs = UiKit.MakeLabel("", 16, UiKit.TextMuted, HorizontalAlignment.Center);
        box.AddChild(_titlePlayingAs);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        _titleContinue = TitleButton("Continue", UiKit.Good.Darkened(0.25f), ContinueRun);
        box.AddChild(_titleContinue);
        _titleContinueDetail = UiKit.MakeLabel("", 14, UiKit.TextMuted, HorizontalAlignment.Center, wrap: true);
        box.AddChild(_titleContinueDetail);
        _titleNewRun = TitleButton("New run", UiKit.PanelRaised, PressTitleNewRun);
        box.AddChild(_titleNewRun);
        _titleProfile = TitleButton("Profile", UiKit.PanelRaised, ShowProfiles);
        box.AddChild(_titleProfile);
        box.AddChild(TitleButton("Stats", UiKit.PanelRaised, ToggleStats));
        _titleSettings = TitleButton("Settings", UiKit.PanelRaised, ShowSettings);
        box.AddChild(_titleSettings);
        box.AddChild(TitleButton("Quit", UiKit.PanelRaised, QuitGame));

        _titleNotice = UiKit.MakeLabel("", 14, UiKit.Bad, HorizontalAlignment.Center, wrap: true);
        box.AddChild(_titleNotice);
        return overlay;
    }

    private static Button TitleButton(string text, Color color, Action pressed)
    {
        var button = UiKit.MakeButton(text, color, 22);
        button.CustomMinimumSize = new Vector2(440, 56);
        button.Pressed += pressed;
        return button;
    }

    /// <summary>Whether there is a run to continue (a won run is kept for the endless choice; a lost one is deleted).</summary>
    private bool HasRunToContinue => _session is not null && _session.Phase != RunPhase.Defeat;

    private void ShowTitle(string? notice = null)
    {
        _statsOverlay.Visible = false;
        _styleGuidesOverlay.Visible = false;
        _titleNotice.Text = notice ?? "";
        RefreshTitle();
        _titleOverlay.Visible = true;
    }

    private void RefreshTitle()
    {
        _titlePlayingAs.Text = $"Playing as {_profile.Profile.Name}";
        _titleProfile.Text = $"Profile: {_profile.Profile.Name}";
        _titleContinue.Visible = HasRunToContinue;
        _titleContinueDetail.Visible = HasRunToContinue;
        if (HasRunToContinue)
            _titleContinueDetail.Text = $"Week {_session.Week + 1}, {WhereText()}  ·  {PressRunText()}";
        DisarmNewRun();
    }

    private void ContinueRun()
    {
        if (!HasRunToContinue)
            return;
        _titleOverlay.Visible = false;
        Refresh();
    }

    /// <summary>The title's New run button: abandoning a run in progress takes a second click within 3 s.</summary>
    private void PressTitleNewRun()
    {
        ulong now = Time.GetTicksMsec();
        if (_session is not null && _session.Phase is RunPhase.InRound or RunPhase.Shop && now >= _newRunArmedUntil)
        {
            _newRunArmedUntil = now + 3000;
            _titleNewRun.Text = "Abandon run?";
            _titleNotice.Text = "Click again to abandon your run and start a new one.";
            GetTree().CreateTimer(3.0).Timeout += () =>
            {
                if (Time.GetTicksMsec() >= _newRunArmedUntil)
                    DisarmNewRun();
            };
            return;
        }
        DisarmNewRun();
        ChooseNewRun();
    }

    private void DisarmNewRun()
    {
        _newRunArmedUntil = 0;
        _titleNewRun.Text = "New run";
        if (_titleNotice.Text.StartsWith("Click again"))
            _titleNotice.Text = "";
    }

    private void QuitGame()
    {
        if (_session is not null)
            PersistRun();
        GetTree().Quit();
    }

    /// <summary>Title keys: Enter continues the run (or starts a new one when there is none).</summary>
    private void HandleTitleKey(InputEventKey key)
    {
        if (key.Keycode is not (Key.Enter or Key.KpEnter))
            return;
        if (HasRunToContinue)
            ContinueRun();
        else
            ChooseNewRun();
    }
}
