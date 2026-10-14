using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// One setting on the Settings tab: its name at the left, the control that changes it at the right, and a note under
/// both, with a line under the row (Styles/Controls.xaml, SettingRow). A card's settings sit in a <see cref="SettingGrid"/>.
/// </summary>
public sealed class SettingRow : ContentControl
{
    /// <summary>The setting's name.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    /// <summary>The note under the setting; empty for none.</summary>
    public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(nameof(Note), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    /// <summary>Whether the note is a warning, in the warning colour.</summary>
    public static readonly DependencyProperty NoteIsWarningProperty = DependencyProperty.Register(nameof(NoteIsWarning), typeof(bool), typeof(SettingRow), new PropertyMetadata(false));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public string Note { get => (string)GetValue(NoteProperty); set => SetValue(NoteProperty, value); }

    public bool NoteIsWarning { get => (bool)GetValue(NoteIsWarningProperty); set => SetValue(NoteIsWarningProperty, value); }
}
