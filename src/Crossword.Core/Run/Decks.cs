using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;

namespace Crossword.Core.Run;

/// <summary>A starting deck: a run setup with an upside and a cost (empty for the Standard Deck). <see cref="Color"/> is a hex color for the UI.</summary>
public sealed record DeckDefinition(string Id, string Name, string Color, string Upside, string Cost);

/// <summary>
/// The numbers behind each starting deck's rules, tuned with <c>runsim … deck=id</c> (2026-10-06, 200 runs, ScoreFraction
/// 0.75 / 0.9 vs the Standard Deck's 39 / 55%). The first Crossword Draft Deck (no deadline cut) won 10 / 26%: the
/// 3-letter rule bans the 2-letter cross words most intersections form, so a bigger intersection bonus barely helped
/// (+4 Mult: 12.5%); deadlines ×0.75 → 30%, ×0.6 → 57%. The first Redactor Deck (no rare letters) won 61 / 76%: a thin
/// deck cycles every round, so Q, Z, X and J went in (37 / 57%). The first Lexicographer's Deck (4 slots only) won
/// 23.5% at 0.75: The Tech Shorthand's acronyms are worth ~+2 pts to the bot, the lost slot ~−15, so its deadlines went
/// ×0.85 (39 / 53.5%; 2026-10-07).
/// </summary>
public sealed record DeckConfig(
    decimal CrosswordDraftIntersectionBonus = 1,
    int CrosswordDraftMinWordLength = 3,
    decimal CrosswordDraftTargetScale = 0.7m,
    int RedactorDiscardsDelta = -1,
    int CopyEditorDiscardsDelta = 1,
    int CopyEditorDeskSlots = 4,
    string CopyEditorStartingItem = "red-pen",
    int LexicographerDeskSlots = 4,
    decimal LexicographerTargetScale = 0.85m)
{
    public static DeckConfig Default { get; } = new();
}

/// <summary>
/// Starting decks, unlocked one per run won in the order of <see cref="All"/> (<see cref="Profile.StatsQueries.UnlockedDecks"/>).
/// A run's deck lives in <see cref="RunState.DeckId"/>; its rules are a <see cref="RunConfig"/> transform
/// (<see cref="Apply"/>), applied before the Press Run when the run starts and again when it's loaded.
/// </summary>
public static class Decks
{
    public const string StandardId = "standard";
    public const string CrosswordDraftId = "crossword-draft";
    public const string RedactorId = "redactor";
    public const string CopyEditorId = "copy-editor";
    public const string LexicographerId = "lexicographer";

    public static ImmutableArray<DeckDefinition> All { get; } = Describe(DeckConfig.Default);

    public static DeckDefinition Standard => All[0];

    public static DeckDefinition? Find(string? id) =>
        All.FirstOrDefault(deck => string.Equals(deck.Id, id, StringComparison.OrdinalIgnoreCase));

    public static DeckDefinition Get(string id) =>
        Find(id) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "No such deck.");

    /// <summary>The rules of deck <paramref name="id"/> applied to <paramref name="config"/> (Standard = unchanged).</summary>
    public static RunConfig Apply(RunConfig config, string id, DeckConfig? decks = null)
    {
        var d = decks ?? DeckConfig.Default;
        switch (Get(id).Id)
        {
            case CrosswordDraftId:
                // The Strict Grammarian's rule would be no cost on this deck, so it never shows up.
                return config with
                {
                    Scoring = config.Scoring with { IntersectionMult = config.Scoring.IntersectionMult + d.CrosswordDraftIntersectionBonus },
                    MinWordLength = Math.Max(config.MinWordLength, d.CrosswordDraftMinWordLength),
                    ExcludedBosses = config.ExcludedBosses.Add(new StrictGrammarian().Id),
                    WeekTargets = config.WeekTargets.Select(target => Scale(target, d.CrosswordDraftTargetScale)).ToImmutableArray(),
                };
            case RedactorId:
                return config with { StartingTiles = StartingDeck.Thin(), DiscardsDelta = config.DiscardsDelta + d.RedactorDiscardsDelta };
            case CopyEditorId:
                return config with
                {
                    DiscardsDelta = config.DiscardsDelta + d.CopyEditorDiscardsDelta,
                    DeskSlots = Math.Min(config.DeskSlots, d.CopyEditorDeskSlots),
                    StartingDeskItems = config.StartingDeskItems.Add(StartingItem(d.CopyEditorStartingItem)),
                };
            case LexicographerId:
                return config with
                {
                    DeskSlots = Math.Min(config.DeskSlots, d.LexicographerDeskSlots),
                    WeekTargets = config.WeekTargets.Select(target => Scale(target, d.LexicographerTargetScale)).ToImmutableArray(),
                };
            default:
                return config;
        }
    }

    /// <summary>Whether deck <paramref name="id"/> starts with a dictionary overlay the player picks.</summary>
    public static bool TakesDictionary(string id) => Get(id).Id == LexicographerId;

    /// <summary>
    /// The dictionary overlays a run with deck <paramref name="deck"/> starts with: the picked
    /// <paramref name="dictionary"/> (default: the first in <see cref="Dictionaries.All"/>) for a deck that takes one,
    /// none otherwise. Picking one for a deck that doesn't take one is a programmer error.
    /// </summary>
    public static ImmutableArray<string> DictionariesFor(string deck, string? dictionary)
    {
        if (TakesDictionary(deck))
            return [Dictionaries.Get(dictionary ?? Dictionaries.All[0].Id).Id];
        if (dictionary is not null)
            throw new ArgumentException($"{Get(deck).Name} doesn't take a dictionary.", nameof(dictionary));
        return [];
    }

    /// <summary><paramref name="target"/> × <paramref name="scale"/>, rounded to 10.</summary>
    private static long Scale(long target, decimal scale) =>
        (long)Math.Round(target * scale / 10m, MidpointRounding.AwayFromZero) * 10;

    private static IDeskItem StartingItem(string id) =>
        DeskItemCatalog.Find(id) ?? throw new InvalidOperationException($"No Desk Item '{id}'.");

    private static ImmutableArray<DeckDefinition> Describe(DeckConfig d) =>
    [
        new(StandardId, "Standard Deck", "#E8E6E0", "The standard 100-tile deck.", ""),
        new(CrosswordDraftId, "The Crossword Draft Deck", "#2B6CB0",
            $"+{d.CrosswordDraftIntersectionBonus} Mult per intersection; deadlines ×{d.CrosswordDraftTargetScale}.",
            $"Words need {d.CrosswordDraftMinWordLength}+ letters."),
        new(RedactorId, "The Redactor Deck", "#C53030",
            $"A thin {StartingDeck.Thin().Length}-tile deck: predictable draws.",
            $"Q, Z, X and J come up every round; {-d.RedactorDiscardsDelta} fewer discard per round."),
        new(CopyEditorId, "The Copy Editor's Deck", "#2F855A",
            $"Starts with {StartingItem(d.CopyEditorStartingItem).Name} and +{d.CopyEditorDiscardsDelta} discard per round.",
            $"Only {d.CopyEditorDeskSlots} Desk Item slots."),
        new(LexicographerId, "The Lexicographer's Deck", "#6B46C1",
            $"Pick an unlocked dictionary: its words are legal this run; deadlines ×{d.LexicographerTargetScale}.",
            $"Only {d.LexicographerDeskSlots} Desk Item slots."),
    ];
}
