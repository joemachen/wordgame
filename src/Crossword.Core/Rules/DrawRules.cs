using Crossword.Core.Domain;

namespace Crossword.Core.Rules;

/// <summary>Pure transitions for drawing tiles. Each returns a new <see cref="RoundState"/>.</summary>
public static class DrawRules
{
    /// <summary>
    /// Draws from the bag until the hand reaches its size, or the bag runs out. With a round <see cref="RoundConfig.Draw"/>
    /// config the draw is balanced (<see cref="TileBag.DrawBalanced"/>); without one it is plain uniform.
    /// </summary>
    public static RoundState DrawToHandSize(RoundState state)
    {
        int needed = state.Config.HandSize - state.Hand.Count;
        if (needed <= 0)
            return state;

        var (drawn, remaining, rng) = state.Config.Draw is { IsOff: false } draw
            ? state.Bag.DrawBalanced(needed, state.Hand.Tiles, draw, state.Rng)
            : state.Bag.Draw(needed, state.Rng);
        return state with { Bag = remaining, Hand = state.Hand.Add(drawn), Rng = rng };
    }
}
