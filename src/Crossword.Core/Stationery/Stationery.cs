using System.Collections.Immutable;

namespace Crossword.Core.Stationery;

/// <summary>
/// One-shot items held in <see cref="Domain.RunState.Stationery"/> slots and used during a round (Balatro's Tarot
/// cards). Their effects live in <see cref="Run.RunRules.UseStationery"/>.
/// </summary>
public interface IStationery
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
}

/// <summary>Reveals the best play for the current hand, scored with your Desk Items.</summary>
public sealed record AnswerKey : IStationery
{
    public string Id => "answer-key";
    public string Name => "Answer Key";
    public string Description => "Use during a round: reveals the best play for your current hand.";
}

public static class StationeryCatalog
{
    public static ImmutableArray<IStationery> All { get; } = [new AnswerKey()];

    public static IStationery? Find(string id) => All.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
