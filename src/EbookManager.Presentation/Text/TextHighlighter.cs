namespace EbookManager.Presentation.Text;

public static class TextHighlighter
{
    public static IReadOnlyList<TextHighlightSegment> CreateSegments(string? text, string? searchText)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return [new TextHighlightSegment(text, false)];
        }

        var segments = new List<TextHighlightSegment>();
        var search = searchText.Trim();
        var currentIndex = 0;

        while (currentIndex < text.Length)
        {
            var matchIndex = text.IndexOf(search, currentIndex, StringComparison.CurrentCultureIgnoreCase);
            if (matchIndex < 0)
            {
                segments.Add(new TextHighlightSegment(text[currentIndex..], false));
                break;
            }

            if (matchIndex > currentIndex)
            {
                segments.Add(new TextHighlightSegment(text[currentIndex..matchIndex], false));
            }

            segments.Add(new TextHighlightSegment(text.Substring(matchIndex, search.Length), true));
            currentIndex = matchIndex + search.Length;
        }

        return segments;
    }
}
