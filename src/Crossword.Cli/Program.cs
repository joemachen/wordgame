using System.Diagnostics;
using Crossword.Core.Analysis;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Save;
using Crossword.Core.Stationery;

namespace Crossword.Cli;

/// <summary>
/// Developer QA console for exercising the headless core. Usage: Crossword.Cli [seed]
/// The CLI is a thin UI layer: it holds the current immutable <see cref="GameSession"/> and swaps it.
/// </summary>
public static class Program
{
    private const char Bom = (char)0xFEFF;
    private static readonly RunConfig Config = RunConfig.Default;
    private static IWordGraph _lexicon = null!;
    private static GameSession _session = null!;

    private static RunState Run => _session.Run;
    private static RoundState Round => _session.Round;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== wordgame QA console ===  (type 'help' for commands)");

        var sw = Stopwatch.StartNew();
        _lexicon = LexiconLoader.Enable;
        Console.WriteLine($"Dictionary loaded: {_lexicon.WordCount:N0} words in {sw.ElapsedMilliseconds} ms");

        ulong seed = args.Length > 0 && ulong.TryParse(args[0], out var parsed) ? parsed : RandomSeed();
        NewRun(seed);

        while (true)
        {
            Console.Write("> ");
            string? line = Console.ReadLine();
            if (line is null)
                return 0; // stdin closed

            // TrimStart BOM: piped input from some shells (e.g. PowerShell) is prefixed with U+FEFF.
            string[] parts = line.TrimStart(Bom).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            if (!Dispatch(parts[0].ToLowerInvariant(), parts[1..]))
                return 0;
        }
    }

    /// <summary>Runs one command; returns false to quit.</summary>
    private static bool Dispatch(string command, string[] rest)
    {
        switch (command)
        {
            case "help" or "?":
                PrintHelp();
                break;
            case "play" or "p":
                Play(rest);
                break;
            case "discard" or "x":
                Discard(rest);
                break;
            case "check" or "c":
                foreach (var word in rest)
                    PrintWordCheck(word);
                break;
            case "hint":
                Hint(rest.Length > 0 && int.TryParse(rest[0], out var n) ? n : 5);
                break;
            case "sim":
                Simulate(rest.Length > 0 && int.TryParse(rest[0], out var r) ? r : 100);
                break;
            case "runsim":
                SimulateRuns(rest.Length > 0 && int.TryParse(rest[0], out var runs) ? runs : 50,
                    rest.Length > 1 && double.TryParse(rest[1], System.Globalization.CultureInfo.InvariantCulture, out var skill) ? skill : 0.9,
                    rest.Skip(2).Any(a => a.Equals("naive", StringComparison.OrdinalIgnoreCase)) ? ShopStrategy.Naive : ShopStrategy.Evaluating,
                    rest.Skip(2).Any(a => a.Equals("frac", StringComparison.OrdinalIgnoreCase)) ? SkillModel.ScoreFraction : SkillModel.Percentile,
                    PressRunArg(rest.Skip(2)) ?? PressRuns.Lowest,
                    DeckArg(rest.Skip(2)) ?? Decks.StandardId,
                    DictionaryArg(rest.Skip(2)));
                break;
            case "shop":
                PrintPhase();
                break;
            case "buy":
                Buy(rest);
                break;
            case "reroll":
                Apply(ShopRules.Reroll(_session), PrintPhase);
                break;
            case "leave" or "next":
                Apply(RunRules.LeaveShop(_session, _lexicon), PrintPhase);
                break;
            case "continue":
                Apply(RunRules.ContinueEndless(_session), PrintPhase);
                break;
            case "desk":
                PrintDesk(showCatalog: true);
                break;
            case "deck":
                Console.WriteLine(ConsoleRenderer.Deck(Run));
                Console.WriteLine(ConsoleRenderer.Tiers(_session));
                break;
            case "give":
                Give(rest);
                break;
            case "sell":
                if (rest.Length == 1 && int.TryParse(rest[0], out var slot))
                    Apply(ShopRules.Sell(_session, slot - 1), () => PrintDesk(showCatalog: false));
                else
                    Console.WriteLine("Usage: sell <slot>");
                break;
            case "use" or "u":
                UseStationery(rest);
                break;
            case "sellst":
                if (rest.Length == 1 && int.TryParse(rest[0], out var stationerySlot))
                    Apply(ShopRules.SellStationery(_session, stationerySlot - 1, _lexicon), PrintPhase);
                else
                    Console.WriteLine("Usage: sellst <slot>");
                break;
            case "save":
                Save(rest.Length > 0 ? string.Join(' ', rest) : DefaultSavePath);
                break;
            case "load":
                Load(rest.Length > 0 ? string.Join(' ', rest) : DefaultSavePath);
                break;
            case "move":
                if (rest.Length == 2 && int.TryParse(rest[0], out var from) && int.TryParse(rest[1], out var to))
                    ApplyRun(Run.MoveDeskItem(from - 1, to - 1));
                else
                    Console.WriteLine("Usage: move <from-slot> <to-slot>");
                break;
            case "board" or "b" or "status" or "s":
                PrintPhase();
                break;
            case "seed":
                Console.WriteLine($"Seed: {Run.Seed}");
                break;
            case "new" or "n":
                string newDeck = DeckArg(rest) ?? Run.DeckId;
                NewRun(rest.Length > 0 && ulong.TryParse(rest[0], out var s) ? s : RandomSeed(), PressRunArg(rest) ?? Run.PressRun,
                    newDeck, DictionaryArg(rest) ?? CarriedDictionary(newDeck));
                break;
            case "quit" or "q" or "exit":
                return false;
            default:
                Console.WriteLine($"Unknown command '{command}'. Type 'help'.");
                break;
        }
        return true;
    }

    private const string DefaultSavePath = "wordgame-run.json";

    private static ulong RandomSeed() => (ulong)Environment.TickCount64;

    /// <summary>Writes the run to <paramref name="path"/> in the game's save format.</summary>
    private static void Save(string path)
    {
        try
        {
            File.WriteAllText(path, RunSaveJson.Serialize(_session), new System.Text.UTF8Encoding(false));
            Console.WriteLine($"Saved to {Path.GetFullPath(path)}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.WriteLine($"Couldn't save to {path}: {e.Message}");
        }
    }

    /// <summary>Loads a run saved by 'save' or by the game (user://saves/&lt;profile&gt;.json).</summary>
    private static void Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.WriteLine($"Couldn't read {path}: {e.Message}");
            return;
        }

        var saved = RunSaveJson.Deserialize(json, Config);
        if (!saved.IsOk)
        {
            Console.WriteLine($"Couldn't load {path}: {saved.Error}");
            return;
        }
        _session = saved.Value.Session;
        _lexicon = LexiconLoader.For(Run.Dictionaries);
        Console.WriteLine($"Loaded {Path.GetFullPath(path)}: seed {Run.Seed}, {Decks.Get(Run.DeckId).Name}, Press Run {Run.PressRun}.");
        PrintPhase();
    }

    private static void NewRun(ulong seed, int pressRun = PressRuns.Lowest, string deck = Decks.StandardId, string? dictionary = null)
    {
        var press = PressRuns.Get(pressRun);
        dictionary = DictionaryFor(deck, dictionary);
        string words = Decks.TakesDictionary(deck) ? $" ({Dictionaries.Get(dictionary ?? Dictionaries.All[0].Id).Name})" : "";
        Console.WriteLine($"\nStarting new run with seed {seed}: {Decks.Get(deck).Name}{words}, Press Run {press.Level} ({press.Name})");
        _session = RunRules.NewGame(seed, Config, LexiconLoader.Enable, pressRun: pressRun, deck: deck, dictionary: dictionary);
        _lexicon = LexiconLoader.For(Run.Dictionaries);
        PrintPhase();
    }

    /// <summary><paramref name="dictionary"/> when <paramref name="deck"/> takes one, else null (reported unless it's carried over).</summary>
    private static string? DictionaryFor(string deck, string? dictionary, bool quiet = false)
    {
        if (dictionary is null || Decks.TakesDictionary(deck))
            return dictionary;
        if (!quiet)
            Console.WriteLine($"{Decks.Get(deck).Name} doesn't take a dictionary; ignoring '{dictionary}'.");
        return null;
    }

    /// <summary>A "press=N" argument (N = 1–8), or null when absent or out of range (which is reported).</summary>
    private static int? PressRunArg(IEnumerable<string> args)
    {
        foreach (string arg in args)
        {
            if (!arg.StartsWith("press=", StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(arg["press=".Length..], out int level) && PressRuns.IsLevel(level))
                return level;
            Console.WriteLine($"Press Run must be {PressRuns.Lowest}–{PressRuns.Highest}; using the default.");
        }
        return null;
    }

    /// <summary>A "deck=id" argument, or null when absent or unknown (which is reported).</summary>
    private static string? DeckArg(IEnumerable<string> args)
    {
        foreach (string arg in args)
        {
            if (!arg.StartsWith("deck=", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Decks.Find(arg["deck=".Length..]) is { } deck)
                return deck.Id;
            Console.WriteLine($"Unknown deck; ids: {string.Join(", ", Decks.All.Select(d => d.Id))}. Using the default.");
        }
        return null;
    }

    /// <summary>The current run's dictionary when the new run's <paramref name="deck"/> takes one.</summary>
    private static string? CarriedDictionary(string deck) => DictionaryFor(deck, Run.Dictionaries.FirstOrDefault(), quiet: true);

    /// <summary>A "dict=id" argument (a dictionary overlay), or null when absent or unknown (which is reported).</summary>
    private static string? DictionaryArg(IEnumerable<string> args)
    {
        foreach (string arg in args)
        {
            if (!arg.StartsWith("dict=", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Dictionaries.Find(arg["dict=".Length..]) is { } dictionary)
                return dictionary.Id;
            Console.WriteLine($"Unknown dictionary; ids: {string.Join(", ", Dictionaries.All.Select(d => d.Id))}. Using the default.");
        }
        return null;
    }

    /// <summary>Swaps in a successful session or prints the error.</summary>
    private static void Apply(Result<GameSession, string> result, Action onSuccess)
    {
        if (!result.IsOk)
        {
            Console.WriteLine(result.Error);
            return;
        }
        _session = result.Value;
        onSuccess();
    }

    private static void ApplyRun(Result<RunState, string> result) =>
        Apply(result.IsOk ? Result<GameSession, string>.Ok(_session with { Run = result.Value })
                          : Result<GameSession, string>.Fail(result.Error),
            () => PrintDesk(showCatalog: false));

    private static bool RequirePhase(RunPhase phase)
    {
        if (_session.Phase == phase)
            return true;
        Console.WriteLine(_session.Phase switch
        {
            RunPhase.Shop => "You're in the shop ('leave' to start the next round).",
            RunPhase.InRound => "You're mid-round.",
            RunPhase.Victory => "Run won! 'continue' for endless mode or 'new' for a new run.",
            _ => "Run over. Type 'new' to start again.",
        });
        return false;
    }

    private static void Play(string[] args)
    {
        if (!RequirePhase(RunPhase.InRound))
            return;

        var command = PlayCommandParser.ParsePlay(args);
        if (!command.IsOk)
        {
            Console.WriteLine(command.Error);
            return;
        }

        var tiles = PlayCommandParser.ResolveTiles(Round.Board, Round.Hand, command.Value);
        if (!tiles.IsOk)
        {
            Console.WriteLine(tiles.Error);
            return;
        }

        var result = RunRules.Submit(_session, tiles.Value, _lexicon);
        if (!result.IsOk)
        {
            Console.WriteLine($"Rejected: {result.Error.Message}");
            return;
        }

        _session = result.Value.Session;
        Console.WriteLine(ConsoleRenderer.ScoreBreakdown(result.Value.Score));
        if (_session.Phase is RunPhase.Shop or RunPhase.Victory && _session.LastPayout is { } payout)
        {
            Console.WriteLine();
            Console.WriteLine($"*** {_session.Kind.Name} cleared with {Round.Score}/{Round.Config.TargetScore}! ***");
            Console.WriteLine(ConsoleRenderer.Paycheck(payout, Run.Money));
        }
        PrintPhase();
    }

    private static void Discard(string[] args)
    {
        if (!RequirePhase(RunPhase.InRound))
            return;
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: discard <LETTERS>   e.g. discard QXV");
            return;
        }

        var ids = PlayCommandParser.ResolveDiscard(Round.Hand, args[0]);
        if (!ids.IsOk)
        {
            Console.WriteLine(ids.Error);
            return;
        }

        var result = RunRules.Discard(_session, ids.Value.ToArray(), _lexicon);
        if (!result.IsOk)
        {
            Console.WriteLine($"Rejected: {result.Error.Message}");
            return;
        }

        _session = result.Value;
        PrintPhase();
    }

    private static void Buy(string[] args)
    {
        if (!RequirePhase(RunPhase.Shop))
            return;
        if (args.Length is < 1 or > 2 || !int.TryParse(args[0], out int number))
        {
            Console.WriteLine("Usage: buy <n> [LETTERS]   LETTERS picks deck tiles for Enhance/Strike, e.g. 'buy 3 E' or 'buy 4 QV'");
            return;
        }

        IReadOnlyList<int> tileIds = [];
        if (args.Length == 2)
        {
            var picked = PlayCommandParser.ResolveDeckTiles(Run.Deck, args[1]);
            if (!picked.IsOk)
            {
                Console.WriteLine(picked.Error);
                return;
            }
            tileIds = picked.Value;
        }

        Apply(ShopRules.Buy(_session, number - 1, tileIds.ToArray()), () =>
        {
            Console.WriteLine($"Bought. You have ${Run.Money}.");
            PrintPhase();
        });
    }

    private static void Give(string[] args)
    {
        if (args.Length == 1 && DeskItemCatalog.Find(args[0]) is { } item)
            ApplyRun(Run.AddDeskItem(item, _session.Config.DeskSlots));
        else if (args.Length == 1 && StationeryCatalog.Find(args[0]) is { } stationery)
            ApplyRun(Run.AddStationery(stationery));
        else
            Console.WriteLine("Usage: give <id>   ids: "
                + string.Join(", ", DeskItemCatalog.All.Select(i => i.Id).Concat(StationeryCatalog.All.Select(i => i.Id))));
    }

    private static void UseStationery(string[] args)
    {
        if (!RequirePhase(RunPhase.InRound))
            return;
        if (args.Length == 0 || !int.TryParse(args[0], out int slot) || slot < 1 || slot > Run.Stationery.Length)
        {
            Console.WriteLine(Run.Stationery.IsEmpty
                ? "You have no stationery."
                : $"Usage: use <slot> [LETTERS|cell]   slots 1-{Run.Stationery.Length}");
            return;
        }

        var item = Run.Stationery[slot - 1];
        var target = PlayCommandParser.ParseStationeryUse(item, Round.Hand, args[1..]);
        if (!target.IsOk)
        {
            Console.WriteLine(target.Error);
            return;
        }

        var result = RunRules.UseStationery(_session, slot - 1, _lexicon, target.Value.TileIds, target.Value.Cell);
        if (!result.IsOk)
        {
            Console.WriteLine($"Rejected: {result.Error}");
            return;
        }

        _session = result.Value.Session;
        Console.WriteLine($"Used {item.Name}.");
        if (result.Value.Play is var (play, score))
            Console.WriteLine($"  Best play: {score.Total}  {PlayCommandText(play)}   words: {string.Join(", ", play.Words.Select(w => w.Text))}");
        PrintPhase();
    }

    /// <summary>The 'play' command that makes <paramref name="play"/>.</summary>
    private static string PlayCommandText(PlayAnalysis play)
    {
        var main = play.Words[0];
        return $"play {main.Cells[0].Position} {(main.Direction == Direction.Across ? 'a' : 'd')} {main.Text}";
    }

    private static void PrintDesk(bool showCatalog)
    {
        Console.WriteLine(ConsoleRenderer.Desk(Run, _session.Config.Shop, _session.Config.DeskSlots));
        Console.WriteLine(ConsoleRenderer.Stationery(Run, _session.Config.Shop));
        if (!showCatalog)
            return;
        Console.WriteLine("Catalog (dev: 'give <id>'):");
        foreach (var item in DeskItemCatalog.All)
            Console.WriteLine($"  {item.Id,-16} {item.Name,-16} ${_session.Config.Shop.PriceOf(item)} {item.Description}");
        foreach (var item in StationeryCatalog.All)
            Console.WriteLine($"  {item.Id,-16} {item.Name,-16} ${_session.Config.Shop.PriceOf(item)} {item.Description} (Stationery)");
    }

    private static void Hint(int count)
    {
        if (!RequirePhase(RunPhase.InRound))
            return;

        var ranked = MoveRanker.Rank(Round.Board, Round.Hand, _lexicon, Run.DeskItems,
            Round.Config.EffectiveScoring(_session.Scoring), Round.Config.MinWordLength, RoundRules.Environment(Round, Run.Money),
            Round.Config.CensoredLetter);
        if (ranked.Count == 0)
        {
            Console.WriteLine("No legal plays with this hand.");
            return;
        }

        Console.WriteLine($"{ranked.Count} legal plays. Best {Math.Min(count, ranked.Count)}:");
        foreach (var (play, score) in ranked.Take(count))
            Console.WriteLine($"  {score.Total,5}  {PlayCommandText(play),-24} words: {string.Join(", ", play.Words.Select(w => w.Text))}");
    }

    private static void Simulate(int rounds)
    {
        var config = Round.Config;
        Console.WriteLine($"Simulating {rounds} rounds with greedy play (current round settings, your desk)...");
        var sw = Stopwatch.StartNew();
        var results = Enumerable.Range(1, rounds)
            .AsParallel()
            .Select(seed => RoundSimulator.PlayRound((ulong)seed, config, _lexicon, Run.DeskItems, _session.Scoring))
            .OrderBy(r => r.Seed)
            .ToList();
        Console.WriteLine(SimulationReport.Format(results, config.TargetScore));
        Console.WriteLine($"  ({sw.Elapsed.TotalSeconds:0.0}s)");
    }

    private static void SimulateRuns(int runs, double skill, ShopStrategy strategy, SkillModel model, int pressRun, string deck,
        string? dictionary)
    {
        dictionary = DictionaryFor(deck, dictionary);
        if (skill is <= 0 or > 1)
        {
            Console.WriteLine("Skill must be in (0, 1], e.g. 'runsim 50 0.9'.");
            return;
        }
        Console.WriteLine($"Simulating {runs} full runs at skill {skill:0.00}, {Decks.Get(deck).Name}"
            + $"{(Decks.TakesDictionary(deck) ? $" ({Dictionaries.Get(dictionary ?? Dictionaries.All[0].Id).Name})" : "")}, Press Run {pressRun} ({PressRuns.Get(pressRun).Name})...");
        var sw = Stopwatch.StartNew();
        var results = Enumerable.Range(1, runs)
            .AsParallel()
            .Select(seed => RunSimulator.PlayRun((ulong)seed, Config, LexiconLoader.Enable, skill, strategy, model: model, pressRun: pressRun,
                deck: deck, dictionary: dictionary))
            .ToList();
        Console.WriteLine(SimulationReport.FormatRuns(results, RunRules.ConfigFor(Config, deck, pressRun), skill, strategy, model));
        Console.WriteLine($"  ({sw.Elapsed.TotalSeconds:0.0}s)");
    }

    /// <summary>Shows whatever screen the current phase needs.</summary>
    private static void PrintPhase()
    {
        Console.WriteLine();
        switch (_session.Phase)
        {
            case RunPhase.InRound:
                Console.WriteLine(ConsoleRenderer.Header(_session));
                Console.WriteLine(ConsoleRenderer.Board(Round.Board));
                Console.WriteLine(ConsoleRenderer.Status(Round));
                if (!Run.DeskItems.IsEmpty)
                    Console.WriteLine(ConsoleRenderer.Desk(Run, _session.Config.Shop, _session.Config.DeskSlots));
                if (!Run.Stationery.IsEmpty)
                    Console.WriteLine(ConsoleRenderer.Stationery(Run, _session.Config.Shop));
                Console.WriteLine(ConsoleRenderer.Hand(Round.Hand, Round.Config.EffectiveScoring(_session.Scoring)));
                if (!RoundRules.HasLegalPlay(Round, _lexicon))
                    Console.WriteLine("(!) No legal play with this hand - discard some tiles.");
                break;

            case RunPhase.Shop:
                Console.WriteLine(ConsoleRenderer.Header(_session));
                Console.WriteLine(ConsoleRenderer.Desk(Run, _session.Config.Shop, _session.Config.DeskSlots));
                Console.WriteLine(ConsoleRenderer.Stationery(Run, _session.Config.Shop));
                Console.WriteLine(ConsoleRenderer.Shop(_session));
                break;

            case RunPhase.Victory:
                Console.WriteLine($"*** YOU WON THE RUN! All {_session.Config.WeekTargets.Length} weeks published. " +
                                  "'continue' for endless mode, 'new' for a new run. ***");
                break;

            case RunPhase.Defeat:
                Console.WriteLine(ConsoleRenderer.Board(Round.Board));
                Console.WriteLine(ConsoleRenderer.Status(Round));
                Console.WriteLine(Round.Deadlocked
                    ? "*** No legal plays and no discards left. Run over. ***"
                    : "*** Missed the deadline. Run over. ***");
                Console.WriteLine($"Reached Week {_session.Week + 1}, {_session.Kind.Name}. Type 'new' to start again.");
                break;
        }
    }

    private static void PrintWordCheck(string word)
    {
        Console.WriteLine($"{word.ToUpperInvariant()}: {(_lexicon.Contains(word) ? "valid" : "NOT a word")}");
        if ((Dictionaries.Define(word, Run.Dictionaries) ?? DefinitionLoader.Default.Define(word)) is not { } definition)
            return;
        if (definition.InflectionOf is not null)
            Console.WriteLine($"  {definition.Form} {definition.InflectionOf}");
        foreach (var sense in definition.Senses)
            Console.WriteLine($"  {sense}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"""
            In a round:
              play  | p <cell> <a|d> <WORD>  Play a word, e.g. 'play C4 a CRANE'. Type the WHOLE word,
                                             including letters already on the board.
              discard | x <LETTERS>          Discard tiles (costs a discard), e.g. 'discard QV'
              use | u <slot> [LETTERS|cell]  Use Stationery: letters for Scissors / Fountain Pen
                                             (e.g. 'use 1 QX'), a cell for White-Out ('use 2 D4')
              hint [n]                       Show the n best legal plays (dev/QA aid)
              sim [rounds]                   Greedy-play simulation of the current round settings
              runsim [runs] [skill] [naive] [frac] [press=N] [deck=id] [dict=id]  Full-run simulation with a shop bot
                                    (default 50, 0.9, smart bot; frac = skill is a fraction of the best play's score,
                                    not a percentile; press=N plays at Press Run N, 1-8; deck=id with that starting
                                    deck; dict=id with that dictionary, for a deck that takes one)
            In the shop:
              buy <n> [LETTERS]              Buy offer n; LETTERS picks deck tiles for Enhance/Strike
              reroll                         New offers (cost rises each reroll)
              leave | next                   Start the next round
            Any time:
              check | c <WORD...>            Look words up in the dictionary (with definitions)
              desk / deck                    Show Desk Items (+catalog) / your tile deck
              sell <slot>                    Sell a Desk Item for half price
              sellst <slot>                  Sell a Stationery item for half price
              move <from> <to>               Reorder Desk Items (they apply left to right)
              give <id>                      (dev) Add a Desk Item or Stationery for free
              save / load [path]             Save or load the run (default {DefaultSavePath}; the
                                             game's own saves load too)
              board | status                 Redraw the current screen
              continue                       Endless mode after winning
              new [seed] [press=N] [deck=id] [dict=id] / seed / quit  (press=N: Press Run 1-8;
                                    deck=id: {string.Join(", ", Decks.All.Select(d => d.Id))};
                                    dict=id (for {Decks.Get(Decks.LexicographerId).Name}): {string.Join(", ", Dictionaries.All.Select(d => d.Id))};
                                    default: the current run's)
            Scoring: longest word sets base chips x mult; every word formed adds letter chips;
                     enhanced tiles trigger per word they're in; each new tile completing both an
                     across and a down word adds +{Config.Scoring.IntersectionMult} mult.
            Run: {Config.WeekTargets.Length} weeks x ({string.Join(", ", Config.Days.Select(d => d.Name))}). Sunday has a boss twist.
            """);
    }
}
