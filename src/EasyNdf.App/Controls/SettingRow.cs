using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace EasyNdf.App.Controls;

/// <summary>
/// One setting on the Settings tab: its name at the left and, just beside it, the control that changes it
/// (Styles/Controls.xaml, SettingRow). Its description is the row's tooltip, shown wherever the mouse rests on the row;
/// only a warning (a value refused) shows under the row, as it answers what was just typed. A card's settings sit in a
/// <see cref="SettingGrid"/>. The control is named after the setting for screen readers, and a click on the name of a
/// switch flips the switch, as a click on a checkbox's own text does.
/// </summary>
public sealed class SettingRow : ContentControl
{
    /// <summary>The setting's name.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(SettingRow),
        new PropertyMetadata("", (row, _) => ((SettingRow)row).NameControl()));

    /// <summary>The setting's description, shown as the row's tooltip; empty for none.</summary>
    public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(nameof(Note), typeof(string), typeof(SettingRow),
        new PropertyMetadata("", (row, change) => ((SettingRow)row).ToolTip = string.IsNullOrEmpty((string)change.NewValue) ? null : change.NewValue));

    /// <summary>Whether the note is a warning: shown under the row too, in the warning colour.</summary>
    public static readonly DependencyProperty NoteIsWarningProperty = DependencyProperty.Register(nameof(NoteIsWarning), typeof(bool), typeof(SettingRow), new PropertyMetadata(false));

    /// <summary>The width of the names' column, the same in every row of a list (set by <see cref="SettingGrid"/>).</summary>
    public static readonly DependencyProperty NameWidthProperty = DependencyProperty.Register(nameof(NameWidth), typeof(GridLength), typeof(SettingRow),
        new PropertyMetadata(new GridLength(250)));

    /// <summary>Whether the row is shaded, as every other row in its list is (set by <see cref="SettingGrid"/>).</summary>
    public static readonly DependencyProperty IsShadedProperty = DependencyProperty.Register(nameof(IsShaded), typeof(bool), typeof(SettingRow), new PropertyMetadata(false));

    /// <inheritdoc cref="LabelProperty"/>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <inheritdoc cref="NoteProperty"/>
    public string Note { get => (string)GetValue(NoteProperty); set => SetValue(NoteProperty, value); }

    /// <inheritdoc cref="NoteIsWarningProperty"/>
    public bool NoteIsWarning { get => (bool)GetValue(NoteIsWarningProperty); set => SetValue(NoteIsWarningProperty, value); }

    /// <inheritdoc cref="NameWidthProperty"/>
    public GridLength NameWidth { get => (GridLength)GetValue(NameWidthProperty); set => SetValue(NameWidthProperty, value); }

    /// <inheritdoc cref="IsShadedProperty"/>
    public bool IsShaded { get => (bool)GetValue(IsShadedProperty); set => SetValue(IsShadedProperty, value); }

    /// <inheritdoc/>
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        NameControl();
    }

    /// <summary>
    /// Gives the row's control the setting's name, for screen readers: a switch or box has no text of its own. A name
    /// the control was given itself is kept.
    /// </summary>
    private void NameControl()
    {
        if (Content is not Control control || Label == "") return;
        object given = control.ReadLocalValue(AutomationProperties.NameProperty);
        if (given != DependencyProperty.UnsetValue && !ReferenceEquals(control.GetValue(NamedByRowProperty), given)) return;
        control.SetValue(AutomationProperties.NameProperty, Label);
        control.SetValue(NamedByRowProperty, Label);
    }

    /// <summary>The name a row gave its control, so a new label replaces it but a control's own name is never touched.</summary>
    private static readonly DependencyProperty NamedByRowProperty =
        DependencyProperty.RegisterAttached("NamedByRow", typeof(string), typeof(SettingRow));

    /// <summary>
    /// A click on the setting's name flips its switch, as a click on a checkbox's text would; the switch's own click
    /// handlers run as for a click on it.
    /// </summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (e.Handled || Content is not ToggleButton { IsEnabled: true } toggle) return;
        if (e.OriginalSource is not TextBlock { Name: "LabelText" } label || !ReferenceEquals(label.TemplatedParent, this)) return;
        e.Handled = true;
        toggle.IsChecked = toggle.IsChecked != true;
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, toggle));
        toggle.Focus();
    }
}
