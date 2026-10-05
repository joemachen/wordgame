using System.Diagnostics;
using Crossword.Core.Analysis;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Scoring;

namespace Crossword.Cli;

/// <summary>
/// Developer QA console for exercising the headless core. Usage: Crossword.Cli [seed]
/// The CLI is a thin UI layer: it holds references to the current immutable states and swaps them.
/// </summary>
public static class Program
{
    private const char Bom = (char)0xFEFF;
    private static readonly ScoringConfig Scoring = ScoringConfig.Default;
    private static IWordGraph _lexicon = null!;
    private static RunState _run = null!;
    private static RoundState _round = null!;

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

            string[] rest = parts[1..];
            switch (parts[0].ToLowerInvariant())
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
                        Console.WriteLine($"{word.ToUpperInvariant()}: {(_lexicon.Contains(word) ? "valid" : "NOT a word")}");
                    break;
                case "hint":
                    Hint(rest.Length > 0 && int.TryParse(rest[0], out var n) ? n : 5);
                    break;
                case "sim":
                    Simulate(rest.Length > 0 && int.TryParse(rest[0], out var r) ? r : 100);
                    break;
                case "desk":
                    PrintDesk(showCatalog: true);
                    break;
                case "give":
                    Give(rest);
                    break;
                case "sell":
                    ApplyRunChange(rest.Length == 1 && int.TryParse(rest[0], out var slot)
                        ? _run.RemoveDeskItem(slot - 1)
                        : Result<RunState, string>.Fail("Usage: sell <slot>"));
                    break;
                case "move":
                    ApplyRunChange(rest.Length == 2 && int.TryParse(rest[0], out var from) && int.TryParse(rest[1], out var to)
                        ? _run.MoveDeskItem(from - 1, to - 1)
                        : Result<RunState, string>.Fail("Usage: move <from-slot> <to-slot>"));
                    break;
                case "board" or "b":
                    PrintRound();
                    break;
                case "status" or "s" or "score":
                    Console.WriteLine(ConsoleRenderer.Status(_run, _round));
                    break;
                case "next":
                    NextRound();
                    break;
                case "seed":
                    Console.WriteLine($"Seed: {_run.Seed}");
                    break;
                case "new" or "n":
                    NewRun(rest.Length > 0 && ulong.TryParse(rest[0], out var s) ? s : RandomSeed());
                    break;
                case "quit" or "q" or "exit":
                    return 0;
                default:
                    Console.WriteLine($"Unknown command '{parts[0]}'. Type 'help'.");
                    break;
            }
        }
    }

    private static ulong RandomSeed() => (ulong)Environment.TickCount64;

    private static void NewRun(ulong seed)
    {
        Console.WriteLine($"\nStarting new run with seed {seed}");
        _run = RunState.New(seed);
        StartRound();
    }

    private static void StartRound()
    {
        (_round, _run) = RoundRules.Start(_run, RoundConfig.ForRound(_run.RoundIndex), _lexicon);
        Console.WriteLine($"--- Round {_run.RoundIndex + 1}: reach {_round.Config.TargetScore} points ---");
        PrintRound();
    }

    private static void NextRound()
    {
        if (_round.Status != RoundStatus.Won)
        {
            Console.WriteLine("Win the current round first ('new' starts a fresh run).");
            return;
        }
        _run = _run.AdvanceRound();
        StartRound();
    }

    private static void Play(string[] args)
    {
        var command = PlayCommandParser.ParsePlay(args);
        if (!command.IsOk)
        {
            Console.WriteLine(command.Error);
            return;
        }

        var tiles = PlayCommandParser.ResolveTiles(_round.Board, _round.Hand, command.Value);
        if (!tiles.IsOk)
        {
            Console.WriteLine(tiles.Error);
            return;
        }

        var result = RoundRules.Submit(_round, tiles.Value, _lexicon, _run.DeskItems, Scoring);
        if (!result.IsOk)
        {
            Console.WriteLine($"Rejected: {result.Error.Message}");
            return;
        }

        _round = result.Value.State;
        Console.WriteLine(ConsoleRenderer.ScoreBreakdown(result.Value.Score));
        PrintRound();
    }

    private static void Discard(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: discard <LETTERS>   e.g. discard QXV");
            return;
        }

        var ids = PlayCommandParser.ResolveDiscard(_round.Hand, args[0]);
        if (!ids.IsOk)
        {
            Console.WriteLine(ids.Error);
            return;
        }

        var result = RoundRules.Discard(_round, ids.Value.ToArray(), _lexicon);
        if (!result.IsOk)
        {
            Console.WriteLine($"Rejected: {result.Error.Message}");
            return;
        }

        _round = result.Value;
        PrintRound();
    }

    private static void Give(string[] args)
    {
        if (args.Length != 1 || DeskItemCatalog.Find(args[0]) is not { } item)
        {
            Console.WriteLine($"Usage: give <id>   ids: {string.Join(", ", DeskItemCatalog.All.Select(i => i.Id))}");
            return;
        }
        ApplyRunChange(_run.AddDeskItem(item));
    }

    private static void ApplyRunChange(Result<RunState, string> result)
    {
        if (!result.IsOk)
        {
            Console.WriteLine(result.Error);
            return;
        }
        _run = result.Value;
        PrintDesk(showCatalog: false);
    }

    private static void PrintDesk(bool showCatalog)
    {
        Console.WriteLine(ConsoleRenderer.Desk(_run));
        if (!showCatalog)
            return;
        Console.WriteLine("Catalog (dev: 'give <id>'):");
        foreach (var item in DeskItemCatalog.All)
            Console.WriteLine($"  {item.Id,-16} {item.Name,-16} {item.Description}");
    }

    private static void Hint(int count)
    {
        var ranked = MoveRanker.Rank(_round.Board, _round.Hand, _lexicon, _run.DeskItems, Scoring);
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
        var config = RoundConfig.ForRound(_run.RoundIndex);
        Console.WriteLine($"Simulating {rounds} rounds with greedy play (round {_run.RoundIndex + 1} settings)...");
        var sw = Stopwatch.StartNew();
        var results = Enumerable.Range(1, rounds)
            .AsParallel()
            .Select(seed => RoundSimulator.PlayRound((ulong)seed, config, _lexicon, _run.DeskItems, Scoring))
            .OrderBy(r => r.Seed)
            .ToList();
        Console.WriteLine(SimulationReport.Format(results, config.TargetScore));
        Console.WriteLine($"  ({sw.Elapsed.TotalSeconds:0.0}s)");
    }

    private static void PrintRound()
    {
        Console.WriteLine();
        Console.WriteLine(ConsoleRenderer.Board(_round.Board));
        Console.WriteLine();
        Console.WriteLine(ConsoleRenderer.Status(_run, _round));
        if (!_run.DeskItems.IsEmpty)
            Console.WriteLine(ConsoleRenderer.Desk(_run));
        Console.WriteLine(ConsoleRenderer.Hand(_round.Hand, Scoring));

        switch (_round.Status)
        {
            case RoundStatus.Won:
                Console.WriteLine("*** Round won! Type 'next' for the next round. ***");
                break;
            case RoundStatus.Lost:
                Console.WriteLine(_round.Deadlocked
                    ? "*** No legal plays and no discards left. Round lost. Type 'new' to start a new run. ***"
                    : "*** Round lost. Type 'new' to start a new run. ***");
                break;
            case RoundStatus.InProgress when !RoundRules.HasLegalPlay(_round, _lexicon):
                Console.WriteLine("(!) No legal play with this hand - discard some tiles.");
                break;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Commands:
              play  | p <cell> <a|d> <WORD>  Play a word, e.g. 'play C4 a CRANE'. Type the WHOLE word,
                                             including letters already on the board.
              discard | x <LETTERS>          Discard tiles (costs a discard), e.g. 'discard QV'
              check | c <WORD...>            Look words up in the dictionary
              hint [n]                       Show the n best legal plays (dev/QA aid)
              sim [rounds]                   Greedy-play simulation for balance tuning (default 100)
              desk                           Show your Desk Items and the catalog
              give <id>                      (dev) Add a Desk Item to the next free slot
              sell <slot>                    Remove the Desk Item in a slot
              move <from> <to>               Reorder Desk Items (they apply left to right)
              board | b                      Show board, status, and hand
              status | s                     Show score and resources
              next                           Start the next round (after winning)
              new   | n [seed]               Start a new run (optional seed for reproducibility)
              seed                           Show the current run's seed
              help  | ?                      Show this help
              quit  | q                      Exit
            Scoring: longest word sets base chips x mult; every word formed adds letter chips;
                     each new tile completing both an across and a down word adds +2 mult.
            """);
    }
}
