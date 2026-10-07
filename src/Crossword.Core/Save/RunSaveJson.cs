using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Run;
using Crossword.Core.Stationery;

namespace Crossword.Core.Save;

/// <summary>A loaded run: the session plus the player's hand arrangement (UI-only tile id order).</summary>
public sealed record SavedRun(GameSession Session, ImmutableArray<int> HandOrder);

/// <summary>
/// Reads and writes a run in progress as JSON (BCL <c>System.Text.Json</c>). Everything in <see cref="GameSession"/>
/// is stored — RNG streams, bag order, board, scaling Desk Items, shop offers — except the <see cref="RunConfig"/>,
/// which the loader supplies (the current tuning, with the run's deck and Press Run applied; the round in progress keeps its own
/// <see cref="RoundConfig"/>).
/// Computed properties are skipped. A save from another version is rejected rather than half-loaded.
/// </summary>
public static class RunSaveJson
{
    public const int CurrentVersion = 1;

    private static readonly Dictionary<string, Type> OfferTypes = new()
    {
        ["desk-item"] = typeof(DeskItemOffer),
        ["add-tile"] = typeof(AddTileOffer),
        ["wild"] = typeof(WildOffer),
        ["enhance"] = typeof(EnhanceOffer),
        ["strike"] = typeof(StrikeOffer),
        ["style-guide"] = typeof(StyleGuideOffer),
        ["stationery"] = typeof(StationeryOffer),
    };

    private static readonly Dictionary<Type, string> OfferTags = OfferTypes.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new LetterConverter(),
            new SortedStringSetConverter(),
            new TaggedConverter<IDeskItem>("id", item => item.Id, id => DeskItemCatalog.Find(id)?.GetType()),
            new TaggedConverter<IStationery>("id", item => item.Id, id => StationeryCatalog.Find(id)?.GetType()),
            new TaggedConverter<BossModifier>("id", boss => boss.Id, id => BossCatalog.Find(id)?.GetType()),
            new TaggedConverter<ShopOffer>("type", offer => OfferTags[offer.GetType()], tag => OfferTypes.GetValueOrDefault(tag)),
        },
    };

    public static string Serialize(GameSession session, IReadOnlyList<int>? handOrder = null)
    {
        var dto = new SaveDto
        {
            Version = CurrentVersion,
            Phase = session.Phase,
            Run = session.Run,
            Round = session.Round,
            Shop = session.Shop,
            LastPayout = session.LastPayout,
            HandOrder = handOrder?.ToArray() ?? [],
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static Result<SavedRun, string> Deserialize(string json, RunConfig config)
    {
        try
        {
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("version", out var version)
                    || !version.TryGetInt32(out int v))
                    return Result<SavedRun, string>.Fail("Unreadable save: no version.");
                if (v != CurrentVersion)
                    return Result<SavedRun, string>.Fail($"Save version {v} doesn't match this game ({CurrentVersion}).");
            }

            var dto = JsonSerializer.Deserialize<SaveDto>(json, Options);
            if (dto?.Run is null || dto.Round is null)
                return Result<SavedRun, string>.Fail("Unreadable save: missing run or round.");
            if (dto.Phase == RunPhase.Shop && dto.Shop is null)
                return Result<SavedRun, string>.Fail("Unreadable save: shop phase without a shop.");

            if (!PressRuns.IsLevel(dto.Run.PressRun))
                return Result<SavedRun, string>.Fail($"Unreadable save: no Press Run {dto.Run.PressRun}.");
            if (Decks.Find(dto.Run.DeckId) is null)
                return Result<SavedRun, string>.Fail($"Unreadable save: no deck '{dto.Run.DeckId}'.");
            if (dto.Run.Dictionaries.FirstOrDefault(id => Dictionaries.Find(id) is null) is { } unknown)
                return Result<SavedRun, string>.Fail($"Unreadable save: no dictionary '{unknown}'.");

            var session = new GameSession(RunRules.ConfigFor(config, dto.Run.DeckId, dto.Run.PressRun), dto.Run, dto.Phase, dto.Round, dto.Shop, dto.LastPayout);
            return Result<SavedRun, string>.Ok(new SavedRun(session, (dto.HandOrder ?? []).ToImmutableArray()));
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return Result<SavedRun, string>.Fail($"Unreadable save: {e.Message}");
        }
    }

    private sealed class SaveDto
    {
        public int Version { get; set; }
        public RunPhase Phase { get; set; }
        public RunState? Run { get; set; }
        public RoundState? Round { get; set; }
        public ShopState? Shop { get; set; }
        public Payout? LastPayout { get; set; }
        public int[]? HandOrder { get; set; }
    }
}
