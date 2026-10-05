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
                BestWeekReached = stats.BestWeekReached,
                BestPlayScore = stats.BestPlayScore,
                BestPlayWords = stats.BestPlayWords,
                LongestWord = stats.LongestWord,
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
            BestWeekReached = s.BestWeekReached,
            BestPlayScore = s.BestPlayScore,
            BestPlayWords = s.BestPlayWords,
            LongestWord = s.LongestWord,
        };
        return Result<PlayerProfile, string>.Ok(new PlayerProfile(dto.Name ?? "Player", stats) { Version = PlayerProfile.CurrentVersion });
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
        public int BestWeekReached { get; set; }
        public long BestPlayScore { get; set; }
        public string? BestPlayWords { get; set; }
        public string? LongestWord { get; set; }
    }

    private sealed class WordUseDto
    {
        public int Count { get; set; }
        public long FirstPlayed { get; set; }
        public long LastPlayed { get; set; }
    }
}
