using Crossword.Core.Domain;

namespace Crossword.Tests.Domain;

public class LetterTests
{
    [Theory]
    [InlineData('a', 'A')]
    [InlineData('Z', 'Z')]
    public void From_NormalisesToUppercase(char input, char expected)
    {
        Assert.Equal(expected, Letter.From(input).Char);
    }

    [Theory]
    [InlineData('1')]
    [InlineData(' ')]
    [InlineData('é')]
    public void From_RejectsNonLetters(char input)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Letter.From(input));
    }
}
