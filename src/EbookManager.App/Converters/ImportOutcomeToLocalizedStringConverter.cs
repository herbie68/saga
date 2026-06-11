using System.Globalization;
using System.Windows.Data;
using EbookManager.App.Localization;
using EbookManager.Domain.Importing;

namespace EbookManager.App.Converters;

public sealed class ImportOutcomeToLocalizedStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ImportOutcome outcome)
        {
            return string.Empty;
        }

        var key = outcome switch
        {
            ImportOutcome.Added => "ImportOutcomeAdded",
            ImportOutcome.ExactDuplicate => "ImportOutcomeExactDuplicate",
            ImportOutcome.PossibleDuplicate => "ImportOutcomePossibleDuplicate",
            ImportOutcome.Failed => "ImportOutcomeFailed",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(key) ? outcome.ToString() : LocalizedStrings.Current[key];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
