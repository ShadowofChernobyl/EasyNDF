using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace EasyNdf.App.Text;

/// <summary>
/// A text by its name, in screens: <c>Content="{t:Text Files.AddFolder}"</c>, with
/// <c>xmlns:t="clr-namespace:EasyNdf.App.Text"</c> at the top of the file. The text follows the language chosen on the
/// Settings tab, changing as soon as another is chosen.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    /// <summary>A text by its name.</summary>
    public TextExtension(string name) => Name = name;

    /// <summary>The text's name, such as "Files.AddFolder".</summary>
    [ConstructorArgument("name")]
    public string Name { get; set; }

    /// <summary>
    /// A binding to the language in use that turns into this text; when the language changes, the binding fetches the
    /// text again.
    /// </summary>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding(nameof(TextSource.Language))
        {
            Source = Texts.Source,
            Mode = BindingMode.OneWay,
            Converter = NameToText.Instance,
            ConverterParameter = Name,
        };
        return binding.ProvideValue(serviceProvider);
    }

    /// <summary>Turns the language in use into the text whose name is the binding's parameter.</summary>
    private sealed class NameToText : IValueConverter
    {
        public static readonly NameToText Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Texts.Get((string)parameter);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
