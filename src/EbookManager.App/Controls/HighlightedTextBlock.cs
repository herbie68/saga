using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EbookManager.Presentation.Text;

namespace EbookManager.App.Controls;

public sealed class HighlightedTextBlock : TextBlock
{
    public static readonly DependencyProperty HighlightedTextProperty =
        DependencyProperty.Register(
            nameof(HighlightedText),
            typeof(string),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(string.Empty, OnHighlightPropertyChanged));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(
            nameof(SearchText),
            typeof(string),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(string.Empty, OnHighlightPropertyChanged));

    public static readonly DependencyProperty HighlightBrushProperty =
        DependencyProperty.Register(
            nameof(HighlightBrush),
            typeof(Brush),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(Brushes.Khaki, OnHighlightPropertyChanged));

    public string? HighlightedText
    {
        get => (string?)GetValue(HighlightedTextProperty);
        set => SetValue(HighlightedTextProperty, value);
    }

    public string? SearchText
    {
        get => (string?)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public Brush? HighlightBrush
    {
        get => (Brush?)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    private static void OnHighlightPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((HighlightedTextBlock)dependencyObject).RefreshInlines();
    }

    private void RefreshInlines()
    {
        Inlines.Clear();

        foreach (var segment in TextHighlighter.CreateSegments(HighlightedText, SearchText))
        {
            var run = new Run(segment.Text);
            if (segment.IsMatch)
            {
                run.Background = HighlightBrush;
                run.FontWeight = FontWeights.SemiBold;
            }

            Inlines.Add(run);
        }
    }
}
