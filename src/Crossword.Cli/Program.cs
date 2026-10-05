using System.Diagnostics;
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
    private static readonly ScoringConfig Scoring = ScoringConfig.Default;
    private static ILexicon _lexicon = null!;
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
            string[] parts = line.TrimStart('﻿').Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
        (_round, _run) = RoundRules.Start(_run, RoundConfig.ForRound(_run.RoundIndex));
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

        var result = RoundRules.Discard(_round, ids.Value.ToArray());
        if (!result.IsOk)
        {
            Console.WriteLine($"Rejected: {result.Error.Message}");
            return;
        }

        _round = result.Value;
        PrintRound();
    }

    private static void PrintRound()
    {
        Console.WriteLine();
        Console.WriteLine(ConsoleRenderer.Board(_round.Board));
        Console.WriteLine();
        Console.WriteLine(ConsoleRenderer.Status(_run, _round));
        Console.WriteLine(ConsoleRenderer.Hand(_round.Hand, Scoring));

        switch (_round.Status)
        {
            case RoundStatus.Won:
                Console.WriteLine("*** Round won! Type 'next' for the next round. ***");
                break;
            case RoundStatus.Lost:
                Console.WriteLine("*** Round lost. Type 'new' to start a new run. ***");
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
