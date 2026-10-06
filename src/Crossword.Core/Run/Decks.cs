using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;

namespace Crossword.Core.Run;

/// <summary>A starting deck: a run setup with an upside and a cost (empty for the Standard Deck). <see cref="Color"/> is a hex color for the UI.</summary>
public sealed record DeckDefinition(string Id, string Name, string Color, string Upside, string Cost);

/// <summary>The numbers behind each starting deck's rules, tuned with <c>runsim … deck=id</c>.</summary>
public sealed record DeckConfig(
    decimal CrosswordDraftIntersectionBonus = 1,
    int CrosswordDraftMinWordLength = 3,
    int RedactorDiscardsDelta = -1,
    int CopyEditorDiscardsDelta = 1,
    int CopyEditorDeskSlots = 4,
    string CopyEditorStartingItem = "red-pen")
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
            default:
                return config;
        }
    }

    private static IDeskItem StartingItem(string id) =>
        DeskItemCatalog.Find(id) ?? throw new InvalidOperationException($"No Desk Item '{id}'.");

    private static ImmutableArray<DeckDefinition> Describe(DeckConfig d) =>
    [
        new(StandardId, "Standard Deck", "#E8E6E0", "The standard 100-tile deck.", ""),
        new(CrosswordDraftId, "The Crossword Draft Deck", "#2B6CB0",
            $"+{d.CrosswordDraftIntersectionBonus} Mult per intersection.",
            $"Words need {d.CrosswordDraftMinWordLength}+ letters."),
        new(RedactorId, "The Redactor Deck", "#C53030",
            $"A thin {StartingDeck.Thin().Length}-tile deck: no Q, Z, X or J.",
            $"{-d.RedactorDiscardsDelta} fewer discard per round."),
        new(CopyEditorId, "The Copy Editor's Deck", "#2F855A",
            $"Starts with {StartingItem(d.CopyEditorStartingItem).Name} and +{d.CopyEditorDiscardsDelta} discard per round.",
            $"Only {d.CopyEditorDeskSlots} Desk Item slots."),
    ];
}
