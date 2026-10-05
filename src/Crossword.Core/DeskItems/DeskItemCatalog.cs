using System.Collections.Immutable;
using Crossword.Core.Effects;

namespace Crossword.Core.DeskItems;

/// <summary>Every Desk Item available in the game, in default tuning.</summary>
public static class DeskItemCatalog
{
    public static ImmutableArray<IDeskItem> All { get; } =
    [
        new RedPen(),
        new Thesaurus(),
        new Inkwell(),
        new CrossReference(),
        new Broadsheet(),
        new RareInk(),
        new MarginNotes(),
        new VowelSound(),
        new ShortStory(),
        new GridLock(),
        new SavingsBond(),
        new WordCount(),
        new DeadlineRush(),
        new PremiumStock(),
        new Syndication(),
        new Archive(),
        new EditorInChief(),
        new Pulitzer(),
    ];

    public static IDeskItem? Find(string id) =>
        All.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
}
