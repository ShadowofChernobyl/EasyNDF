using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Weapons;

/// <summary>
/// The Weapon pane's ✎ menu: "Picture of Another Weapon…" opens a list of every weapon picture the game has, under a
/// box to find one by name; a click on one gives it to the weapon. Once changed, "Its Own Picture Again" takes the
/// change back. The pictures are the game's own, so nothing new is added to the mod's files.
/// </summary>
public static class WeaponPicturePicker
{
    /// <summary>The list's size, and each picture's width in it.</summary>
    private const double ListWidth = 300, ListHeight = 360, PictureWidth = 110;

    /// <summary>Opens the ✎ menu under the pencil, or under the Picture value's cell.</summary>
    /// <param name="under">The ✎ button, or the cell, the menu opens under.</param>
    /// <param name="pictures">Every weapon picture of the game: its file, by texture name.</param>
    /// <param name="isChanged">Whether the open mod gives the weapon another picture now.</param>
    /// <param name="choose">Gives the weapon a picture, by texture name; null for its own again.</param>
    public static void OpenMenu(FrameworkElement under, IReadOnlyDictionary<string, string> pictures, bool isChanged, Action<string?> choose)
    {
        var menu = new ContextMenu { PlacementTarget = under, Placement = PlacementMode.Bottom };
        var another = new MenuItem { Header = Texts.Get("Weapons.Picture.Another"), ToolTip = Texts.Get("Weapons.Picture.Another.Tip") };
        another.Click += (_, _) => OpenList(under, pictures, choose);
        menu.Items.Add(another);
        if (isChanged)
        {
            var own = new MenuItem { Header = Texts.Get("Weapons.Picture.Own"), ToolTip = Texts.Get("Weapons.Picture.Own.Tip") };
            own.Click += (_, _) => choose(null);
            menu.Items.Add(own);
        }
        menu.IsOpen = true;
    }

    /// <summary>The list of pictures, in a box under the pencil (or the cell), that closes when a picture is chosen or the user clicks elsewhere.</summary>
    private static void OpenList(FrameworkElement under, IReadOnlyDictionary<string, string> pictures, Action<string?> choose)
    {
        var popup = new Popup { PlacementTarget = under, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var find = new TextBox { Tag = Texts.Get("Weapons.Picture.Find.Hint"), ToolTip = Texts.Get("Weapons.Picture.Find.Tip"), Margin = new Thickness(0, 0, 0, 6) };
        // only the rows in view are made, and so only their pictures read
        var list = new ListBox { Height = ListHeight, BorderThickness = new Thickness(0) };
        list.ItemTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PictureRow)) };
        list.SetResourceReference(Control.BackgroundProperty, "well");
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        List<string> names = [.. pictures.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        void Fill()
        {
            string typed = find.Text.Trim();
            list.ItemsSource = names.Where(name => typed == "" || name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .Select(name => new PictureChoice(name, pictures[name])).ToList();
        }
        find.TextChanged += (_, _) => Fill();
        list.MouseLeftButtonUp += (_, e) =>
        {
            if ((e.OriginalSource as DependencyObject) is not DependencyObject clicked || ItemsControl.ContainerFromElement(list, clicked) is not ListBoxItem item) return;
            if (item.Content is PictureChoice chosen)
            {
                popup.IsOpen = false;
                choose(chosen.Texture);
            }
        };
        Fill();
        var box = new Bevel { Child = new DockPanel { Children = { find, list } }, Padding = new Thickness(8), Width = ListWidth };
        DockPanel.SetDock(find, Dock.Top);
        box.SetResourceReference(Bevel.TopLeftProperty, "hi");
        box.SetResourceReference(Bevel.BottomRightProperty, "lo");
        box.SetResourceReference(Bevel.BackgroundProperty, "panel");
        popup.Child = box;
        popup.Opened += (_, _) => Keyboard.Focus(find);
        popup.IsOpen = true;
    }

    /// <summary>A picture in the list: its texture's name and its file.</summary>
    private sealed record PictureChoice(string Texture, string Source);

    /// <summary>A row of the list: the picture, with its name after "Texture_Interface_Weapon_" beside it.</summary>
    private sealed class PictureRow : DockPanel
    {
        private const string Prefix = "Texture_Interface_Weapon_";
        private readonly WeaponPicture picture = new() { Width = PictureWidth };
        private readonly TextBlock name = new()
        {
            FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 0, 0),
        };

        public PictureRow()
        {
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            SetDock(picture, Dock.Left);
            Children.Add(picture);
            Children.Add(name);
            DataContextChanged += (_, _) => Show();
        }

        private void Show()
        {
            if (DataContext is not PictureChoice choice) return;
            picture.Source = choice.Source;
            name.Text = choice.Texture.StartsWith(Prefix, StringComparison.Ordinal) ? choice.Texture[Prefix.Length..] : choice.Texture;
            ToolTip = choice.Texture;
        }
    }
}
