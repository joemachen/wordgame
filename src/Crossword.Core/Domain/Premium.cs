namespace Crossword.Core.Domain;

/// <summary>Grid premium squares. Only take effect under tiles placed in the current play.</summary>
public enum Premium
{
    None,
    DoubleLetter,
    TripleLetter,
    DoubleWord,
    TripleWord,
}
