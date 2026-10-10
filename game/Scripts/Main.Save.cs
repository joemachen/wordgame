using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Run;
using Crossword.Core.Save;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Save and resume: the run is saved after every transition (and on close) and offered by the title's Continue on
/// launch. A lost run deletes its save; a won run keeps it so the endless choice survives a restart.
/// </summary>
public partial class Main
{
    private RunSaveStore _runSave = null!;
    private GameSession? _lastSavedSession;

    /// <summary>Continues a saved run. Not a new run, so no run start is recorded in the stats.</summary>
    private void Resume(SavedRun saved)
    {
        _session = saved.Session;
        _lexicon = LexiconLoader.For(Run.Dictionaries);
        _lastSavedSession = _session;
        _selected.Clear();
        _pending.Clear();
        _newTileIds.Clear();
        _handOrder = saved.HandOrder;
        // Stats already recorded before the save: the won round in the shop, a finished run.
        _roundWonRecorded = Round.Status == RoundStatus.Won ? Run.RoundIndex : -1;
        _runEndRecorded = _session.Phase is RunPhase.Victory or RunPhase.Defeat;
        _justUnlockedPressRun = null;
        _justUnlockedDeck = null;
        ClearLog();
        SetMessage($"Resumed your run: Week {_session.Week + 1}, {WhereText()}.", UiKit.TextMuted);
        Refresh();
    }

    /// <summary>Where the run stands within its week, e.g. "at the shop" or "Saturday Stumper".</summary>
    private string WhereText() => _session.Phase switch
    {
        RunPhase.Shop => "at the shop",
        RunPhase.Victory => "all five weeks published",
        _ => _session.Kind.Name,
    };

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
}
