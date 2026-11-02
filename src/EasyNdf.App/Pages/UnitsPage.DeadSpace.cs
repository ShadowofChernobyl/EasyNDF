using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNdf.App.Pages;

/// <summary>
/// A click on the Armory's empty space (a pane's background, a heading, a label) takes the keyboard focus away from
/// the box or list that had it, as a click outside a box does in most apps (Paul's request). A box keeps what was typed
/// in it, as when it is left with Tab.
/// </summary>
public partial class UnitsPage
{
    /// <summary>Listens for clicks the page's controls leave unhandled.</summary>
    private void LetDeadSpaceClearFocus()
    {
        // the page itself takes the focus, so the keys the Armory answers (Ctrl+F, the fold keys) still reach it
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, false);
        FocusVisualStyle = null;
        AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(DeadSpacePressed), handledEventsToo: false);
    }

    /// <summary>A click nothing handled: away from the focused box, the page takes the focus from it.</summary>
    private void DeadSpacePressed(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused || ReferenceEquals(focused, this) || !IsAncestorOf(focused)) return;
        if (e.OriginalSource is DependencyObject pressed && IsInAControl(pressed)) return;
        Focus();
    }

    /// <summary>Whether an element is a control the user works with (a box, a list, a button…), or inside one.</summary>
    private bool IsInAControl(DependencyObject element)
    {
        for (DependencyObject? at = element; at is not null && !ReferenceEquals(at, this);
             at = at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is TextBoxBase or ComboBox or ButtonBase or Selector or ScrollBar or Thumb or PasswordBox) return true;
        }
        return false;
    }
}
