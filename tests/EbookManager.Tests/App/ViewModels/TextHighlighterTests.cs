using EbookManager.Presentation.Text;
using FluentAssertions;

namespace EbookManager.Tests.App.ViewModels;

public sealed class TextHighlighterTests
{
    [Fact]
    public void CreateSegments_marks_case_insensitive_matches_and_preserves_original_text()
    {
        var segments = TextHighlighter.CreateSegments("The Hobbit", "hob");

        segments.Should().BeEquivalentTo(
        [
            new TextHighlightSegment("The ", false),
            new TextHighlightSegment("Hob", true),
            new TextHighlightSegment("bit", false)
        ]);
    }
}
