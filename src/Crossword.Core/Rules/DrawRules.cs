using Crossword.Core.Domain;

namespace Crossword.Core.Rules;

/// <summary>Pure transitions for drawing tiles. Each returns a new <see cref="RunState"/>.</summary>
public static class DrawRules
{
    /// <summary>Draws from the bag until the hand reaches its size, or the bag runs out.</summary>
    public static RunState DrawToHandSize(RunState state)
    {
        int needed = state.HandSize - state.Hand.Count;
        if (needed <= 0)
            return state;

        var (drawn, remaining, rng) = state.Bag.Draw(needed, state.Rng);
        return state with { Bag = remaining, Hand = state.Hand.Add(drawn), Rng = rng };
    }
}
