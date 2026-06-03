using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;

namespace EbookManager.App.Localization;

public sealed class LocalizedStrings : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new(
        "EbookManager.App.Resources.Strings.AppResources",
        typeof(LocalizedStrings).Assembly);

    public static LocalizedStrings Current { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]")
        {
            Source = LocalizedStrings.Current,
            Mode = BindingMode.OneWay
        }.ProvideValue(serviceProvider);
}
