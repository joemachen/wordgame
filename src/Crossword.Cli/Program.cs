using System.Diagnostics;
using Crossword.Core.Analysis;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Run;

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
                    rest.Skip(2).Any(a => a.Equals("frac", StringComparison.OrdinalIgnoreCase)) ? SkillModel.ScoreFraction : SkillModel.Percentile);
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
                NewRun(rest.Length > 0 && ulong.TryParse(rest[0], out var s) ? s : RandomSeed());
                break;
            case "quit" or "q" or "exit":
                return false;
            default:
                Console.WriteLine($"Unknown command '{command}'. Type 'help'.");
                break;
        }
        return true;
    }

    private static ulong RandomSeed() => (ulong)Environment.TickCount64;

    private static void NewRun(ulong seed)
    {
        Console.WriteLine($"\nStarting new run with seed {seed}");
        _session = RunRules.NewGame(seed, Config, _lexicon);
        PrintPhase();
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
        if (args.Length != 1 || DeskItemCatalog.Find(args[0]) is not { } item)
        {
            Console.WriteLine($"Usage: give <id>   ids: {string.Join(", ", DeskItemCatalog.All.Select(i => i.Id))}");
            return;
        }
        ApplyRun(Run.AddDeskItem(item));
    }

    private static void PrintDesk(bool showCatalog)
    {
        Console.WriteLine(ConsoleRenderer.Desk(Run, Config.Shop));
        if (!showCatalog)
            return;
        Console.WriteLine("Catalog (dev: 'give <id>'):");
        foreach (var item in DeskItemCatalog.All)
            Console.WriteLine($"  {item.Id,-16} {item.Name,-16} ${Config.Shop.PriceOf(item)} {item.Description}");
    }

    private static void Hint(int count)
    {
        if (!RequirePhase(RunPhase.InRound))
            return;

        var ranked = MoveRanker.Rank(Round.Board, Round.Hand, _lexicon, Run.DeskItems,
            Round.Config.EffectiveScoring(_session.Scoring), Round.Config.MinWordLength, RoundRules.Environment(Round, Run.Money));
        if (ranked.Count == 0)
        {
            Console.WriteLine("No legal plays with this hand.");
            return;
        }

        Console.WriteLine($"{ranked.Count} legal plays. Best {Math.Min(count, ranked.Count)}:");
        foreach (var (play, score) in ranked.Take(count))
        {
            var main = play.Words[0];
            string command = $"play {main.Cells[0].Position} {(main.Direction == Direction.Across ? 'a' : 'd')} {main.Text}";
            Console.WriteLine($"  {score.Total,5}  {command,-24} words: {string.Join(", ", play.Words.Select(w => w.Text))}");
        }
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

    private static void SimulateRuns(int runs, double skill, ShopStrategy strategy, SkillModel model)
    {
        if (skill is <= 0 or > 1)
        {
            Console.WriteLine("Skill must be in (0, 1], e.g. 'runsim 50 0.9'.");
            return;
        }
        Console.WriteLine($"Simulating {runs} full runs at skill {skill:0.00}...");
        var sw = Stopwatch.StartNew();
        var results = Enumerable.Range(1, runs)
            .AsParallel()
            .Select(seed => RunSimulator.PlayRun((ulong)seed, Config, _lexicon, skill, strategy, model: model))
            .ToList();
        Console.WriteLine(SimulationReport.FormatRuns(results, Config, skill, strategy, model));
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
                    Console.WriteLine(ConsoleRenderer.Desk(Run, Config.Shop));
                Console.WriteLine(ConsoleRenderer.Hand(Round.Hand, Round.Config.EffectiveScoring(_session.Scoring)));
                if (!RoundRules.HasLegalPlay(Round, _lexicon))
                    Console.WriteLine("(!) No legal play with this hand - discard some tiles.");
                break;

            case RunPhase.Shop:
                Console.WriteLine(ConsoleRenderer.Header(_session));
                Console.WriteLine(ConsoleRenderer.Desk(Run, Config.Shop));
                Console.WriteLine(ConsoleRenderer.Shop(_session));
                break;

            case RunPhase.Victory:
                Console.WriteLine($"*** YOU WON THE RUN! All {Config.WeekTargets.Length} weeks published. " +
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
        if (DefinitionLoader.Default.Define(word) is not { } definition)
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
              hint [n]                       Show the n best legal plays (dev/QA aid)
              sim [rounds]                   Greedy-play simulation of the current round settings
              runsim [runs] [skill] [naive] [frac]  Full-run simulation with a shop bot (default 50, 0.9, smart bot;
                                    frac = skill is a fraction of the best play's score, not a percentile)
            In the shop:
              buy <n> [LETTERS]              Buy offer n; LETTERS picks deck tiles for Enhance/Strike
              reroll                         New offers (cost rises each reroll)
              leave | next                   Start the next round
            Any time:
              check | c <WORD...>            Look words up in the dictionary (with definitions)
              desk / deck                    Show Desk Items (+catalog) / your tile deck
              sell <slot>                    Sell a Desk Item for half price
              move <from> <to>               Reorder Desk Items (they apply left to right)
              give <id>                      (dev) Add a Desk Item for free
              board | status                 Redraw the current screen
              continue                       Endless mode after winning
              new [seed] / seed / quit
            Scoring: longest word sets base chips x mult; every word formed adds letter chips;
                     enhanced tiles trigger per word they're in; each new tile completing both an
                     across and a down word adds +{Config.Scoring.IntersectionMult} mult.
            Run: {Config.WeekTargets.Length} weeks x ({string.Join(", ", Config.Days.Select(d => d.Name))}). Sunday has a boss twist.
            """);
    }
}
