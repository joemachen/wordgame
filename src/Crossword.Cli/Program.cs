using Crossword.Core.Domain;
using Crossword.Core.Rules;

namespace Crossword.Cli;

/// <summary>
/// Developer QA console for exercising the headless core. Usage: Crossword.Cli [seed]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        ulong seed = args.Length > 0 && ulong.TryParse(args[0], out var parsed)
            ? parsed
            : (ulong)Environment.TickCount64;

        var state = StartRun(seed);
        Console.WriteLine("=== wordgame QA console ===  (type 'help' for commands)");
        PrintStatus(state);

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

            switch (parts[0].ToLowerInvariant())
            {
                case "help" or "?":
                    PrintHelp();
                    break;
                case "hand" or "h":
                    PrintStatus(state);
                    break;
                case "draw" or "d":
                    state = DrawRules.DrawToHandSize(state);
                    PrintStatus(state);
                    break;
                case "seed":
                    Console.WriteLine($"Seed: {state.Seed}");
                    break;
                case "new" or "n":
                    ulong newSeed = parts.Length > 1 && ulong.TryParse(parts[1], out var s)
                        ? s
                        : (ulong)Environment.TickCount64;
                    state = StartRun(newSeed);
                    PrintStatus(state);
                    break;
                case "quit" or "q" or "exit":
                    return 0;
                default:
                    Console.WriteLine($"Unknown command '{parts[0]}'. Type 'help'.");
                    break;
            }
        }
    }

    private static RunState StartRun(ulong seed)
    {
        Console.WriteLine($"Starting new run with seed {seed}");
        return DrawRules.DrawToHandSize(RunState.New(seed));
    }

    private static void PrintStatus(RunState state)
    {
        Console.WriteLine($"Hand ({state.Hand.Count}/{state.HandSize}): {state.Hand}");
        Console.WriteLine($"Bag: {state.Bag.Count} tiles remaining");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Commands:
              hand  | h        Show hand and bag
              draw  | d        Draw up to full hand size
              seed             Show the current run's seed
              new   | n [seed] Start a new run (optional seed for reproducibility)
              help  | ?        Show this help
              quit  | q        Exit
            """);
    }
}
