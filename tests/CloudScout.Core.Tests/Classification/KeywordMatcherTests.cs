using CloudScout.Core.Classification;
using FluentAssertions;

namespace CloudScout.Core.Tests.Classification;

public class KeywordMatcherTests
{
    [Theory]
    [InlineData("william was here", "will", false)]
    [InlineData("misstatement of facts", "statement", false)]
    [InlineData("Suicidepiano.wav", "id", false)]
    [InlineData("Discord.png", "cor", false)]
    [InlineData("Streamlabs Alert.url", "lab", false)]
    [InlineData("Labels bar.png", "lab", false)]
    [InlineData("Setup Guide.url", "id", false)]
    public void Rejects_substring_hits(string text, string word, bool expected)
        => KeywordMatcher.ContainsWord(text, word).Should().Be(expected);

    [Theory]
    [InlineData("ID.pdf", "id", true)]
    [InlineData("My ID Document.pdf", "id", true)]
    [InlineData("the will, signed in 2024,", "will", true)]
    [InlineData("IBAN: GB12", "iban", true)]
    [InlineData("(will)", "will", true)]
    [InlineData("cor.pdf", "cor", true)]
    [InlineData("Lab Results.pdf", "lab", true)]
    public void Accepts_whole_word_hits(string text, string word, bool expected)
        => KeywordMatcher.ContainsWord(text, word).Should().Be(expected);

    [Theory]
    [InlineData("id_card.png", "id card", true)]
    [InlineData("ID-Card.pdf", "id card", true)]
    [InlineData("ID Card.pdf", "id card", true)]
    [InlineData("identification.pdf", "id card", false)]
    [InlineData("log_book.pdf", "log book", true)]
    [InlineData("LogBook.pdf", "log book", false)]
    public void Multi_word_keywords_match_across_separators(string text, string word, bool expected)
        => KeywordMatcher.ContainsWord(text, word).Should().Be(expected);

    [Theory]
    [InlineData("", "id", false)]
    [InlineData("anything", "", false)]
    [InlineData(null, "id", false)]
    [InlineData("anything", null, false)]
    public void Empty_inputs_return_false(string? text, string? word, bool expected)
        => KeywordMatcher.ContainsWord(text!, word!).Should().Be(expected);
}
