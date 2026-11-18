using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Pages;

/// <summary>
/// The two ways a line of the Rules tab's mod and rule set lists is drawn: an item (a mod, a rule set), or a group's
/// heading, as the rule list's group headings are.
/// </summary>
public sealed class ListLineTemplates : DataTemplateSelector
{
    /// <summary>A mod's or a rule set's line.</summary>
    public DataTemplate? Item { get; set; }

    /// <summary>A group's heading.</summary>
    public DataTemplate? Group { get; set; }

    /// <inheritdoc/>
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is RulesPage.ModGroupLine or RulesPage.PresetGroupLine ? Group : Item;
}
