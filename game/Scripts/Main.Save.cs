using Crossword.Core.Domain;
using Crossword.Core.Run;
using Crossword.Core.Save;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Save and resume: the run is saved after every transition (and on close) and resumed on launch. A lost run
/// deletes its save; a won run keeps it so the endless choice survives a restart.
/// </summary>
public partial class Main
{
    private RunSaveStore _runSave = null!;
    private GameSession? _lastSavedSession;
    private Button _newRunButton = null!;
    private ulong _newRunArmedUntil;

    /// <summary>Continues a saved run. Not a new run, so no run start is recorded in the stats.</summary>
    private void Resume(SavedRun saved)
    {
        _session = saved.Session;
        _lastSavedSession = _session;
        _selected.Clear();
        _pending.Clear();
        _newTileIds.Clear();
        _handOrder = saved.HandOrder;
        // Stats already recorded before the save: the won round in the shop, a finished run.
        _roundWonRecorded = Round.Status == RoundStatus.Won ? Run.RoundIndex : -1;
        _runEndRecorded = _session.Phase is RunPhase.Victory or RunPhase.Defeat;
        _justUnlockedPressRun = null;
        ClearLog();
        string where = _session.Phase switch
        {
            RunPhase.Shop => "at the shop",
            RunPhase.Victory => "all five weeks published",
            _ => _session.Kind.Name,
        };
        SetMessage($"Resumed your run: Week {_session.Week + 1}, {where}.", UiKit.TextMuted);
        Refresh();
    }

    /// <summary>Saves the run if the session changed since the last save (called on every refresh).</summary>
    private void PersistRunIfChanged()
    {
        if (!ReferenceEquals(_session, _lastSavedSession))
            PersistRun();
    }

    private void PersistRun()
    {
        _lastSavedSession = _session;
        if (_session.Phase == RunPhase.Defeat)
            _runSave.Delete();
        else
            _runSave.Save(_session, _handOrder);
    }

    /// <summary>The sidebar's New run button: abandoning a run in progress takes a second click within 3 s.</summary>
    private void PressNewRun()
    {
        ulong now = Time.GetTicksMsec();
        if (_session.Phase is RunPhase.InRound or RunPhase.Shop && now >= _newRunArmedUntil)
        {
            _newRunArmedUntil = now + 3000;
            _newRunButton.Text = "Abandon run?";
            SetMessage("Click again to abandon this run and start a new one.", UiKit.Bad);
            GetTree().CreateTimer(3.0).Timeout += () =>
            {
                if (Time.GetTicksMsec() >= _newRunArmedUntil)
                    _newRunButton.Text = "New run";
            };
            return;
        }
        _newRunArmedUntil = 0;
        _newRunButton.Text = "New run";
        ChooseNewRun();
    }
}
