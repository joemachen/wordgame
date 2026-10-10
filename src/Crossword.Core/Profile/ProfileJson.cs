using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crossword.Core.Domain;

namespace Crossword.Core.Profile;

/// <summary>
/// Reads and writes <see cref="PlayerProfile"/> as JSON (BCL <c>System.Text.Json</c>). Missing fields take their
/// defaults so older files keep loading; unreadable files and files from a newer version fail instead of being
/// silently replaced.
/// </summary>
public static class ProfileJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(PlayerProfile profile)
    {
        var stats = profile.Stats;
        var dto = new ProfileDto
        {
            Version = profile.Version,
            Name = profile.Name,
            Stats = new StatsDto
            {
                Words = stats.Words.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToDictionary(kv => kv.Key, kv => new WordUseDto { Count = kv.Value.Count, FirstPlayed = kv.Value.FirstPlayed, LastPlayed = kv.Value.LastPlayed }),
                PlaysRecorded = stats.PlaysRecorded,
                RunsStarted = stats.RunsStarted,
                RunsWon = stats.RunsWon,
                SeededRunsWon = stats.SeededRunsWon,
                BestWeekReached = stats.BestWeekReached,
                BestPlayScore = stats.BestPlayScore,
                BestPlayWords = stats.BestPlayWords,
                LongestWord = stats.LongestWord,
                TotalIntersections = stats.TotalIntersections,
                CloseCalls = stats.CloseCalls,
                BossesBeaten = stats.BossesBeaten.Count == 0 ? null : stats.BossesBeaten.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                FullSpreadRounds = stats.FullSpreadRounds,
                PressRunsWon = stats.HighestPressRunWon.Count == 0 ? null : stats.HighestPressRunWon.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
            },
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static Result<PlayerProfile, string> Deserialize(string json)
    {
        ProfileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ProfileDto>(json, Options);
        }
        catch (JsonException e)
        {
            return Result<PlayerProfile, string>.Fail($"Unreadable profile: {e.Message}");
        }
        if (dto is null)
            return Result<PlayerProfile, string>.Fail("Empty profile.");
        if (dto.Version > PlayerProfile.CurrentVersion)
            return Result<PlayerProfile, string>.Fail($"Profile version {dto.Version} is newer than this game ({PlayerProfile.CurrentVersion}).");

        var s = dto.Stats ?? new StatsDto();
        var stats = new PlayerStats
        {
            Words = (s.Words ?? new Dictionary<string, WordUseDto>())
                .ToImmutableDictionary(kv => kv.Key, kv => new WordUse(kv.Value.Count, kv.Value.FirstPlayed, kv.Value.LastPlayed)),
            PlaysRecorded = s.PlaysRecorded,
            RunsStarted = s.RunsStarted,
            RunsWon = s.RunsWon,
            SeededRunsWon = s.SeededRunsWon,
            BestWeekReached = s.BestWeekReached,
            BestPlayScore = s.BestPlayScore,
            BestPlayWords = s.BestPlayWords,
            LongestWord = s.LongestWord,
            TotalIntersections = s.TotalIntersections,
            CloseCalls = s.CloseCalls,
            BossesBeaten = (s.BossesBeaten ?? new Dictionary<string, int>()).ToImmutableDictionary(),
            FullSpreadRounds = s.FullSpreadRounds,
            HighestPressRunWon = PressRunsWon(s),
        };
        return Result<PlayerProfile, string>.Ok(new PlayerProfile(dto.Name ?? "Player", stats) { Version = PlayerProfile.CurrentVersion });
    }

    /// <summary>
    /// The highest Press Run won per deck. Profiles from before decks hold one level (<c>highestPressRunWon</c>), won with
    /// the Standard Deck; profiles from before Press Runs hold neither, and every win so far was at the base level.
    /// </summary>
    private static ImmutableDictionary<string, int> PressRunsWon(StatsDto s)
    {
        if (s.PressRunsWon is not null)
            return s.PressRunsWon.Where(kv => kv.Value > 0).ToImmutableDictionary();
        int standard = s.HighestPressRunWon ?? (s.RunsWon > 0 ? 1 : 0);
        return standard > 0
            ? ImmutableDictionary<string, int>.Empty.Add(Run.Decks.StandardId, standard)
            : ImmutableDictionary<string, int>.Empty;
    }

    private sealed class ProfileDto
    {
        public int Version { get; set; } = PlayerProfile.CurrentVersion;
        public string? Name { get; set; }
        public StatsDto? Stats { get; set; }
    }

    private sealed class StatsDto
    {
        public Dictionary<string, WordUseDto>? Words { get; set; }
        public long PlaysRecorded { get; set; }
        public int RunsStarted { get; set; }
        public int RunsWon { get; set; }
        public int SeededRunsWon { get; set; }
        public int BestWeekReached { get; set; }
        public long BestPlayScore { get; set; }
        public string? BestPlayWords { get; set; }
        public string? LongestWord { get; set; }
        public long TotalIntersections { get; set; }
        public int CloseCalls { get; set; }
        public Dictionary<string, int>? BossesBeaten { get; set; }
        public int FullSpreadRounds { get; set; }
        /// <summary>Highest Press Run won per deck id.</summary>
        public Dictionary<string, int>? PressRunsWon { get; set; }

        /// <summary>Read only, from profiles written before decks (a single level, won with the Standard Deck).</summary>
        public int? HighestPressRunWon { get; set; }
    }

    private sealed class WordUseDto
    {
        public int Count { get; set; }
        public long FirstPlayed { get; set; }
        public long LastPlayed { get; set; }
    }
}
