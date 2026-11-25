using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EasyNdf.App.Controls;
using EasyNdf.App.Models;
using EasyNdf.App.Text;
using EasyNdf.Data.Models;
using EasyNdf.Data.Models.Fbx;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// The 3D Model panel: the copied unit's own model, or an .fbx file of the user's own for it (see
/// <see cref="OwnModel"/>). A file picked is read with its pictures and shown in 3D as the copy the mod will carry
/// (parts renamed, points added, scaled), with the fine print that it is the file as picked. The copied unit's parts
/// that turn, fire or move are listed with the file's part for each, found by name; the checks say what is missing or
/// wrong, each with its fix where there is one. Only a model that cannot fit (a soldier's for a vehicle), or a unit
/// whose look is not a ground vehicle's, stops it being used.
/// </summary>
public partial class OwnModelPanel : UserControl
{
    private Unit? copied;
    private UnitSource? source;
    private CopiedModel? copiedModel;
    private PickedModel? picked;
    private ModelFit? fit;
    private string? midFile, lowFile;
    private bool own, texturesOpen, problemsOpenTextures;
    private int reading;

    private ModelRenderer? renderer;
    private readonly ModelCamera camera = new();
    private ModelMesh? shownMesh;

    public OwnModelPanel()
    {
        InitializeComponent();
        wantedTexturesHeight = AppData.Current.State[TexturesHeightKey]?.GetValue<double>() is double kept and >= LeastListHeight ? kept : TexturesHeight;
        TexturesBox.Height = wantedTexturesHeight;
        ChecksGrip.Target = ChecksScroller;
        ChecksGrip.HeightChosen += ChooseChecksHeight;
        ChecksGrip.HeightSettled += () =>
        {
            AppData.Current.State[ChecksHeightKey] = double.IsNaN(ChecksScroller.Height) ? null : ChecksScroller.Height;
            AppData.Current.SaveState();
        };
        if (AppData.Current.State[ChecksHeightKey]?.GetValue<double>() is double keptChecks) ChooseChecksHeight(keptChecks);
        TexturesGrip.Target = TexturesBox;
        TexturesGrip.HeightChosen += ChooseTexturesHeight;
        TexturesGrip.HeightSettled += () =>
        {
            AppData.Current.State[TexturesHeightKey] = TexturesBox.Height;
            AppData.Current.SaveState();
        };
    }

    /// <summary>Where the checks list's height is kept, as its grip leaves it.</summary>
    private const string ChecksHeightKey = "ownModelChecksHeight";

    /// <summary>
    /// The checks list's height from its grip: from two rows up to what leaves the view its least height; a
    /// double-click gives the list its own height back, up to a few rows.
    /// </summary>
    private void ChooseChecksHeight(double? height)
    {
        if (height is not double wanted)
        {
            ChecksScroller.Height = double.NaN;
            ChecksScroller.MaxHeight = 150;
            return;
        }
        ChecksScroller.MaxHeight = double.PositiveInfinity;
        double most = ActualHeight > 0 ? Math.Max(44, ActualHeight - 300) : 400;
        ChecksScroller.Height = Math.Clamp(wanted, 44, most);
    }

    /// <summary>Where the textures list's height is kept, as its grip leaves it.</summary>
    private const string TexturesHeightKey = "ownModelTexturesHeight";

    /// <summary>The textures list's own height, and the least either list keeps while the grip shares the room.</summary>
    private const double TexturesHeight = 130, LeastListHeight = 48;

    /// <summary>
    /// The grip under the textures: the list takes the height asked for, from the parts list under it, each keeping a
    /// few rows; a double-click gives it its own height back.
    /// </summary>
    private void ChooseTexturesHeight(double? height)
    {
        double most = Math.Max(LeastListHeight, TexturesBox.ActualHeight + PartsBox.ActualHeight - LeastListHeight);
        TexturesBox.Height = height is double asked ? Math.Clamp(asked, LeastListHeight, most) : TexturesHeight;
        wantedTexturesHeight = TexturesBox.Height;
    }

    /// <summary>The textures list's height as the grip left it; it is shown lower while the panel has no room for it.</summary>
    private double wantedTexturesHeight;

    private void SideResized(object sender, SizeChangedEventArgs e) => FitListsSoon();

    private void FitListsSoon() => Dispatcher.BeginInvoke(FitLists, System.Windows.Threading.DispatcherPriority.Loaded);

    /// <summary>
    /// Keeps a few rows of the parts list in view: a height kept for the textures list in a large window left the
    /// parts list none at all in a small one. The textures list gives up what the parts list is short of, and takes
    /// its own height back when there is room again.
    /// </summary>
    private void FitLists()
    {
        if (TexturesBox.Visibility != Visibility.Visible || !IsVisible) return;
        double height = TexturesBox.Height, room = PartsBox.ActualHeight - LeastListHeight;
        double fitted = room < -0.5 ? Math.Max(LeastListHeight, height + room)
            : room > 0.5 && height < wantedTexturesHeight ? Math.Min(wantedTexturesHeight, height + room) : height;
        if (Math.Abs(fitted - height) < 0.5) return;
        TexturesBox.Height = fitted;
        // a parts list with no height at all hides how much it is short of: looked at again once laid out
        FitListsSoon();
    }

    /// <summary>Raised when the choice or its checks change, for the window's buttons and hint.</summary>
    public event Action? Changed;

    /// <summary>Whether a file of the user's own is chosen (else the copied unit's model is kept).</summary>
    public bool UsesOwn => own;

    /// <summary>Whether the choice cannot be used: a file of the user's own not picked, not read, or that cannot fit the unit.</summary>
    public bool Blocks => own && (picked is null || fit is null || fit.Blocks || copiedModel?.Look is null || copiedModel?.Mesh is null);

    /// <summary>What the window's foot says of the choice.</summary>
    public string Hint
    {
        get
        {
            if (!own) return Texts.Get("Units.OwnModel.Hint.Copied");
            if (picked is null) return Texts.Get("Units.OwnModel.Hint.Pick");
            if (Blocks) return Texts.Get("Units.OwnModel.Hint.Blocked");
            int problems = fit?.Checks(picked.Mesh).Count(check => check.Level == CheckLevel.Problem) ?? 0;
            return problems > 0 ? Texts.Count("Units.OwnModel.Hint.Problems", problems, ("count", problems)) : Texts.Get("Units.OwnModel.Hint.Rules");
        }
    }

    /// <summary>Shows the panel for a unit: its own model, or the model of the user's own it has.</summary>
    /// <param name="unit">The unit copied (for a new unit) or changed: its model's parts are the ones a file must have.</param>
    /// <param name="from">Where its model is read from.</param>
    /// <param name="current">The model of the user's own it has; null for its own.</param>
    public void Open(Unit unit, UnitSource from, OwnModel? current)
    {
        copied = unit;
        source = from;
        copiedModel = null;
        picked = null;
        fit = null;
        keptName = current?.Model("High")?.Name;
        midFile = Found(current?.Model("Mid")?.Kept);
        lowFile = Found(current?.Model("Low")?.Kept);
        own = current is not null;
        texturesOpen = false;
        problemsOpenTextures = false;
        BaseButton.ToolTip = Texts.Get("Units.OwnModel.Copied.Tip", ("unit", unit.Name));
        UsingText.Text = Texts.Get("Units.OwnModel.Using", ("unit", unit.Name));
        camera.Yaw = 310;
        camera.Pitch = 18;
        camera.Zoom = 1;
        camera.Pan = Vector3.Zero;
        StatusText.Text = Texts.Get("Units.OwnModel.Reading");
        ShowAll();
        int attempt = ++reading;
        _ = ReadCopied(unit, from, attempt);
        // a kept model's pictures carry the app's names: each is found by the name the model gives it
        keptPictures = current is null ? null : current.Files.Where(file => file.Level is null)
            .GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => Found(group.First().Kept)!, StringComparer.OrdinalIgnoreCase);
        if (Found(current?.Model("High")?.Kept) is string kept) _ = ReadPicked(kept, attempt);
    }

    /// <summary>The name the model the unit has goes into the mod under; null once another file is picked.</summary>
    private string? keptName;

    /// <summary>The pictures of the model the unit has, by the names the model gives them; null once another file is picked.</summary>
    private Dictionary<string, string>? keptPictures;

    /// <summary>
    /// Where a kept file is now: at its path, or, once the app has moved it into its mod's folder (as it does a unit's
    /// pictures), found there by its name; the path as kept when it is in neither, so reading it says so.
    /// </summary>
    private static string? Found(string? kept)
    {
        if (kept is null || File.Exists(kept)) return kept;
        return ModFolders.FindPicture(System.IO.Path.GetFileName(kept)) ?? kept;
    }

    /// <summary>The descriptor of the unit the model is for, once known, for the folder its files go into.</summary>
    public string? Descriptor { get; set; }

    /// <summary>
    /// Whether the unit is still being made and has no name yet (the New Unit window, whose 3D Model step comes before
    /// its Name step): the folder is then shown with the name to come, not with the copied unit's, which it never is.
    /// </summary>
    public bool AwaitsName { get; set; }

    /// <summary>Shows the folder the model's files go into again, after <see cref="Descriptor"/> changed.</summary>
    public void ShowDestination() =>
        DestinationPath.Text = Destination(Descriptor is null ? null : OwnModel.KeyFor(Descriptor)) ?? "—";

    /// <summary>Lets go of the 3D view and the files read, when the window closes.</summary>
    public void Close()
    {
        reading++;
        renderer?.Dispose();
        renderer = null;
        Picture.Source = null;
        picked = null;
        fit = null;
        copiedModel = null;
        shownMesh = null;
        Marks.Children.Clear();
    }

    private async Task ReadCopied(Unit unit, UnitSource from, int attempt)
    {
        CopiedModel read;
        try
        {
            read = await Task.Run(() => CopiedModel.Read(unit, from));
        }
        catch (Exception problem)
        {
            // whatever stops the game's model being read is said, rather than "Reading…" left up for good
            SessionLog.Write("Error", problem.ToString());
            read = new CopiedModel(null, [], null, null, null, problem.Message);
        }
        if (attempt != reading) return;
        copiedModel = read;
        if (picked is not null && read.Mesh is not null) fit = new ModelFit(picked.Scene, read.Mesh);
        ShowAll();
    }

    /// <summary>Counts the files picked: a slow file's read, finishing after a file picked later, is dropped.</summary>
    private int picking;

    private async Task ReadPicked(string file, int attempt)
    {
        int pickedAttempt = ++picking;
        StatusText.Text = Texts.Get("Units.OwnModel.Reading");
        try
        {
            IReadOnlyDictionary<string, string>? known = keptPictures;
            PickedModel read = await Task.Run(() => PickedModel.Read(file, known));
            if (attempt != reading || picking != pickedAttempt) return;
            picked = read;
            fit = copiedModel?.Mesh is ModelMesh mesh ? new ModelFit(read.Scene, mesh) : null;
            // a model missing maps opens its pictures' list, so what is missing shows
            problemsOpenTextures = read.Materials.Any(material => material.Missing.Any());
            texturesOpen = problemsOpenTextures;
        }
        catch (Exception problem)
        {
            // any file may be picked: whatever stops it being read is said, rather than "Reading…" left up for good
            if (problem is not (IOException or InvalidDataException or UnauthorizedAccessException)) SessionLog.Write("Error", problem.ToString());
            if (attempt != reading || picking != pickedAttempt) return;
            picked = null;
            fit = null;
            StatusText.Text = Texts.Get("Units.OwnModel.CannotRead", ("problem", problem.Message));
        }
        ShowAll();
    }

    // ---- showing ----

    private void ShowAll()
    {
        ImportButton.Visibility = own ? Visibility.Collapsed : Visibility.Visible;
        BaseButton.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
        UsingText.Visibility = own ? Visibility.Collapsed : Visibility.Visible;
        FileBox.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
        RealityText.Visibility = own && picked is not null ? Visibility.Visible : Visibility.Collapsed;
        FilePath.Text = picked?.File ?? Texts.Get("Units.OwnModel.NoFile");
        MidPath.Text = midFile is null ? Texts.Get("Units.OwnModel.None") : midFile;
        LowPath.Text = lowFile is null ? Texts.Get("Units.OwnModel.None") : lowFile;
        DestinationPath.Text = Destination(Descriptor is null ? null : OwnModel.KeyFor(Descriptor)) ?? "—";
        ShowTextures();
        ShowParts();
        ShowChecks();
        ShowModel();
        Changed?.Invoke();
    }

    /// <summary>The folder the files go into under GameData: the copied model's army and kind, then the unit's own key.</summary>
    private string? Destination(string? key = null)
    {
        if (copiedModel?.Path is not string path) return null;
        string[] parts = path.Split('/');
        string folder = parts.Length >= 3 ? string.Join('/', parts[..^2]) : "Assets/3D/Units";
        key ??= AwaitsName ? "EZ_" + Texts.Get("Units.OwnModel.NameToCome") : OwnModel.KeyFor("Descriptor_Unit_" + (copied?.Descriptor.Split("Descriptor_Unit_")[^1] ?? "Unit"));
        return folder + "/" + key + "/";
    }

    private void ShowTextures()
    {
        TexturesList.Children.Clear();
        if (picked is null)
        {
            TexturesText.Text = "";
            TexturesButton.Visibility = Visibility.Collapsed;
            TexturesBox.Visibility = Visibility.Collapsed;
            TexturesGrip.Visibility = Visibility.Collapsed;
            TexturesArea.Visibility = Visibility.Collapsed;
            return;
        }
        int missing = picked.Materials.Count(material => material.Missing.Any());
        TexturesText.Text = missing > 0
            ? Texts.Get("Units.OwnModel.Textures.Missing", ("missing", missing), ("count", picked.Materials.Count))
            : Texts.Count("Units.OwnModel.Textures.All", picked.Materials.Count, ("count", picked.Materials.Count));
        TexturesText.SetResourceReference(TextBlock.ForegroundProperty, missing > 0 ? "caution" : "muted");
        TexturesButton.Visibility = Visibility.Visible;
        TexturesButton.Content = Texts.Get(texturesOpen ? "Units.OwnModel.Hide" : "Units.OwnModel.Show");
        TexturesBox.Visibility = texturesOpen ? Visibility.Visible : Visibility.Collapsed;
        TexturesGrip.Visibility = TexturesBox.Visibility;
        FitListsSoon();
        // shown under the detail levels while a file of the user's own is used
        TexturesArea.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
        foreach (MaterialMaps material in picked.Materials)
        {
            var row = new DockPanel { Margin = new Thickness(6, 2, 6, 2) };
            var maps = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(maps, Dock.Right);
            foreach ((string suffix, string? file) in material.Maps)
            {
                var label = new TextBlock { Text = suffix == "" ? Texts.Get("Units.OwnModel.Colour") : suffix, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11 };
                if (file is null) label.TextDecorations = TextDecorations.Strikethrough;
                label.SetResourceReference(TextBlock.ForegroundProperty, file is null ? "caution" : "ok");
                var chip = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(3, 0, 0, 0), Child = label,
                    ToolTip = file ?? Texts.Get("Units.OwnModel.MapMissing") };
                chip.SetResourceReference(Border.BorderBrushProperty, "line");
                maps.Children.Add(chip);
            }
            row.Children.Add(maps);
            row.Children.Add(new TextBlock { Text = material.Material, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            TexturesList.Children.Add(row);
        }
    }

    /// <summary>The copied unit's parts, each with the file's part chosen for it (or, for its own model, its own).</summary>
    private void ShowParts()
    {
        PartsList.Children.Clear();
        List<ExpectedPart> expected = fit?.Expected ?? (copiedModel?.Mesh is ModelMesh mesh ? ModelFit.ExpectedOf(mesh) : []);
        PartsNote.Text = expected.Count == 0 ? "" : Texts.Get(own ? "Units.OwnModel.Parts.Note" : "Units.OwnModel.Parts.Note.Base", ("unit", copied?.Name ?? ""));
        PartsHeader.Child = expected.Count == 0 ? null : PartsHeaderRow();
        for (int i = 0; i < expected.Count; i++) PartsList.Children.Add(PartRow(expected[i], i));
    }

    /// <summary>The columns of a parts row: the unit's part, the part for it, and the mark.</summary>
    private static Grid PartColumns()
    {
        var row = new Grid { Margin = new Thickness(6, 3, 6, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        return row;
    }

    /// <summary>
    /// What each column of the parts list is: the unit's part, then the base unit's model part for it; with a model of
    /// the user's own, the part of their file, and whether one is found.
    /// </summary>
    private Grid PartsHeaderRow()
    {
        Grid row = PartColumns();
        string[] heads = own
            ? [Texts.Get("Units.OwnModel.Parts.UnitPart"), Texts.Get("Units.OwnModel.Parts.FilePart"), Texts.Get("Units.OwnModel.Parts.Found")]
            : [Texts.Get("Units.OwnModel.Parts.UnitPart"), Texts.Get("Units.OwnModel.Parts.ModelPart")];
        for (int column = 0; column < heads.Length; column++)
        {
            var head = new TextBlock { Text = heads[column], FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = column == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            head.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            Grid.SetColumn(head, column);
            row.Children.Add(head);
        }
        return row;
    }

    /// <summary>The header's columns stay over the rows' when the list gets a scrollbar: it gives up the scrollbar's width.</summary>
    private void PartsScrolled(object sender, ScrollChangedEventArgs e) =>
        PartsHeader.Padding = new Thickness(0, 0, Math.Max(0, PartsScroller.ActualWidth - PartsScroller.ViewportWidth), 0);

    private FrameworkElement PartRow(ExpectedPart part, int index)
    {
        Grid row = PartColumns();
        if (part.Role is PartRole.Turret or PartRole.Gun or PartRole.GunMount or PartRole.FiringPoint)
        {
            row.Background = Brushes.Transparent;
            row.MouseEnter += (_, _) => LightTurret(part.Number);
            row.MouseLeave += (_, _) => LightTurret(-1);
        }
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = PartName(part), TextTrimming = TextTrimming.CharacterEllipsis });
        var code = new TextBlock { Text = part.IsGroup ? Texts.Count("Units.OwnModel.Part.Count", part.Names.Count, ("count", part.Names.Count)) : part.Names[0],
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        code.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        // the base unit's model: its own part beside each of the unit's, with nothing to pick or to find
        bool baseModel = !own;
        if (baseModel)
        {
            if (part.IsGroup) code.ToolTip = string.Join(", ", part.Names);
            Grid.SetColumn(code, 1);
            row.Children.Add(code);
        }
        else name.Children.Add(code);
        row.Children.Add(name);

        string mark;
        string colour;
        if (!own || fit is null || picked is null) (mark, colour) = ("", "muted");
        else if (part.IsGroup)
        {
            int found = fit.FoundOf(part);
            var text = new TextBlock { Text = Texts.Get("Units.OwnModel.Part.Found", ("found", found), ("count", part.Names.Count)), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = string.Join(", ", part.Names) };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            (mark, colour) = found == part.Names.Count ? ("✓", "ok") : ("!", "caution");
        }
        else
        {
            var pick = new ComboBox { VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
            pick.Items.Add(new ComboBoxItem { Content = Texts.Get("Units.OwnModel.Part.None"), Tag = 0L });
            if (part.Role == PartRole.FiringPoint) pick.Items.Add(new ComboBoxItem { Content = Texts.Get("Units.OwnModel.Part.GunsEnd"), Tag = -1L });
            foreach (FbxPart each in picked.Scene.Parts.OrderBy(each => each.Name, StringComparer.OrdinalIgnoreCase))
                pick.Items.Add(new ComboBoxItem { Content = each.Name, Tag = each.Id });
            long chosen = fit.AtGunsEnd.Contains(index) ? -1 : fit.Chosen.GetValueOrDefault(index);
            pick.SelectedItem = pick.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (long)item.Tag == chosen) ?? pick.Items[0];
            pick.SelectionChanged += (_, _) =>
            {
                if (fit is null || pick.SelectedItem is not ComboBoxItem { Tag: long id }) return;
                fit.Chosen.Remove(index);
                fit.AtGunsEnd.Remove(index);
                fit.BySimilarName.Remove(index);
                if (id > 0) fit.Chosen[index] = id;
                else if (id == -1) fit.AtGunsEnd.Add(index);
                Dispatcher.BeginInvoke(ShowAll);
            };
            var cell = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            cell.Children.Add(pick);
            if (fit.BySimilarName.Contains(index))
            {
                var similar = new TextBlock { Text = Texts.Get("Units.OwnModel.Part.Similar"), FontSize = 11 };
                similar.SetResourceReference(TextBlock.ForegroundProperty, "accent");
                cell.Children.Add(similar);
            }
            Grid.SetColumn(cell, 1);
            row.Children.Add(cell);
            bool met = fit.IsMet(index);
            bool problem = part.Role is PartRole.Hull or PartRole.FiringPoint;
            (mark, colour) = met ? ("✓", "ok") : problem ? ("✕", "warn") : ("!", "caution");
        }
        var sign = new TextBlock { Text = mark, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        sign.SetResourceReference(TextBlock.ForegroundProperty, colour);
        Grid.SetColumn(sign, 2);
        row.Children.Add(sign);
        var line = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1) };
        line.SetResourceReference(Border.BorderBrushProperty, "line");
        return line;
    }

    private static string PartName(ExpectedPart part) => part.Role switch
    {
        PartRole.Hull => Texts.Get("Units.OwnModel.Part.Hull"),
        PartRole.Turret => Texts.Get("Units.OwnModel.Part.Turret", ("number", part.Number)),
        PartRole.GunMount => Texts.Get("Units.OwnModel.Part.GunMount", ("number", part.Number)),
        PartRole.Gun => Texts.Get("Units.OwnModel.Part.Gun", ("number", part.Number)),
        PartRole.FiringPoint => Texts.Get("Units.OwnModel.Part.FiringPoint", ("number", part.Number)),
        PartRole.Wheels => Texts.Get("Units.OwnModel.Part.Wheels"),
        PartRole.Tracks => Texts.Get("Units.OwnModel.Part.Tracks"),
        PartRole.Skirts => Texts.Get("Units.OwnModel.Part.Skirts"),
        _ => Texts.Get("Units.OwnModel.Part.Effects"),
    };

    /// <summary>The checks: what is right, what is worth knowing and what is wrong, each wrong one with its fix where there is one.</summary>
    private void ShowChecks()
    {
        ChecksPanel.Children.Clear();
        var checks = new List<FitCheck>();
        if (!own) checks.Add(new FitCheck(CheckLevel.Good, "Units.OwnModel.Check.Copied", [("unit", copied?.Name ?? "")]));
        else if (copiedModel is not null && copiedModel.Look is null) checks.Add(new FitCheck(CheckLevel.Problem, "Units.OwnModel.Check.NotVehicle", []));
        else if (copiedModel is not null && copiedModel.Mesh is null) checks.Add(new FitCheck(CheckLevel.Problem, "Units.OwnModel.Check.NoCopiedModel", [("unit", copied?.Name ?? "")]));
        else if (picked is not null && fit is not null)
        {
            checks.AddRange(fit.Checks(picked.Mesh));
            foreach (MaterialMaps material in picked.Materials.Where(material => material.Missing.Any()))
                checks.Add(new FitCheck(CheckLevel.Caution, "Units.OwnModel.Check.Maps", [("material", material.Material),
                    ("maps", string.Join(", ", material.Missing.Select(suffix => suffix == "" ? Texts.Get("Units.OwnModel.Colour") : suffix)))]));
            if (midFile is null && lowFile is null) checks.Add(new FitCheck(CheckLevel.Caution, "Units.OwnModel.Check.NoLevels", []));
            else checks.Add(new FitCheck(CheckLevel.Good, "Units.OwnModel.Check.Levels", []));
        }
        // what is wrong first, then what is worth knowing, then what is right: a long list shows what needs doing at its top
        foreach (FitCheck check in checks.OrderByDescending(check => check.Level)) ChecksPanel.Children.Add(CheckRow(check));
        CheckCounts.Children.Clear();
        foreach (CheckLevel level in new[] { CheckLevel.Problem, CheckLevel.Caution, CheckLevel.Good })
        {
            int count = checks.Count(check => check.Level == level);
            if (count == 0) continue;
            var said = new TextBlock { Text = SignOf(level) + " " + Texts.Number(count), FontWeight = FontWeights.Bold, Margin = new Thickness(10, 0, 0, 0) };
            said.SetResourceReference(TextBlock.ForegroundProperty, ColourOf(level));
            CheckCounts.Children.Add(said);
        }
    }

    private static string SignOf(CheckLevel level) => level switch { CheckLevel.Good => "✓", CheckLevel.Caution => "!", _ => "✕" };

    private static string ColourOf(CheckLevel level) => level switch { CheckLevel.Good => "ok", CheckLevel.Caution => "caution", _ => "warn" };

    private FrameworkElement CheckRow(FitCheck check)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var sign = new TextBlock { Text = SignOf(check.Level), Width = 18, FontWeight = FontWeights.Bold };
        sign.SetResourceReference(TextBlock.ForegroundProperty, ColourOf(check.Level));
        DockPanel.SetDock(sign, Dock.Left);
        row.Children.Add(sign);
        var body = new StackPanel();
        // a check that counts (its {count}) is said in the form its number takes: "The part was found.", "All 3 parts found."
        string said = check.Blanks.FirstOrDefault(blank => blank.Name == "count").Value is int count
            ? Texts.Count(check.Text, count, [.. check.Blanks]) : Texts.Get(check.Text, [.. check.Blanks]);
        body.Children.Add(new TextBlock { Text = said, TextWrapping = TextWrapping.Wrap });
        if (check.Fix is string fix && check.FixKind is string kind)
        {
            var button = new Button { Content = Texts.Get(check.Fix, [.. check.Blanks.Concat(FixBlanks(kind))]), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0) };
            button.Click += (_, _) => TakeFix(kind);
            body.Children.Add(button);
        }
        row.Children.Add(body);
        return row;
    }

    /// <summary>The blanks a fix's button fills: the length a scale gives.</summary>
    private IEnumerable<(string, object?)> FixBlanks(string kind)
    {
        if (kind.StartsWith("Scale:", StringComparison.Ordinal) && picked is not null && fit is not null
            && double.TryParse(kind[6..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double by))
            yield return ("scaled", Math.Round(fit.FileLength(picked.Mesh) * fit.Scale * by, 1));
    }

    private void TakeFix(string kind)
    {
        if (fit is null) return;
        if (kind.StartsWith("GunsEnd:", StringComparison.Ordinal) && int.TryParse(kind[8..], out int index)) fit.AtGunsEnd.Add(index);
        else if (kind.StartsWith("Scale:", StringComparison.Ordinal)
            && double.TryParse(kind[6..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double by)) fit.Scale *= by;
        ShowAll();
    }

    // ---- the 3D view ----

    /// <summary>Draws the model chosen: the copied unit's, or the copy of the file the mod will carry.</summary>
    private void ShowModel()
    {
        ModelMesh? mesh;
        List<MaterialPictures> pictures;
        if (!own)
        {
            mesh = copiedModel?.Mesh;
            pictures = copiedModel?.Pictures ?? [];
            if (copiedModel is not null && mesh is null) StatusText.Text = copiedModel.Problem is string problem
                ? Texts.Get("Units.OwnModel.CannotRead", ("problem", problem)) : Texts.Get("Units.Model.NoModel");
        }
        else if (picked is null)
        {
            mesh = null;
            pictures = [];
            if (StatusText.Text == "" || StatusText.Text == Texts.Get("Units.OwnModel.Reading")) StatusText.Text = Texts.Get("Units.OwnModel.Hint.Pick");
        }
        else
        {
            byte[] copy = fit?.MakeCopy(picked.Bytes) ?? picked.Bytes;
            (mesh, pictures) = picked.Drawn(copy);
        }
        if (mesh is null)
        {
            shownMesh = null;
            Picture.Source = null;
            Marks.Children.Clear();
            SizeText.Text = "";
            TriangleText.Text = "";
            return;
        }
        StatusText.Text = "";
        string? why = null;
        if (renderer is null && (renderer = ModelRenderer.Create(out why)) is not null)
        {
            renderer.PictureBack += Draw;
            renderer.ShowsFloor = false;
            renderer.ShowsGrid = true;
        }
        if (renderer is null)
        {
            StatusText.Text = Texts.Get("Units.Model.CannotDraw", ("problem", why ?? ""));
            return;
        }
        if (mesh.Nodes.Count > ModelRenderer.MostNodes) mesh = mesh with { Nodes = [.. mesh.Nodes.Take(ModelRenderer.MostNodes)] };
        renderer.Load(mesh, pictures);
        shownMesh = mesh;
        Vector3 size = (mesh.High - mesh.Low) / FbxScene.GameUnitsAMetre;
        SizeText.Text = Texts.Get("Units.OwnModel.Size", ("length", Math.Round(size.X, 1)), ("width", Math.Round(size.Y, 1)), ("height", Math.Round(size.Z, 1)));
        TriangleText.Text = Texts.Count("Units.OwnModel.Triangles", mesh.TriangleCount, ("count", mesh.TriangleCount.ToString("N0", Texts.Catalog.Culture)));
        ViewResizedNow();
    }

    private void ViewResized(object sender, SizeChangedEventArgs e) => ViewResizedNow();

    private void ViewResizedNow()
    {
        if (renderer is null || ViewFrame.ActualWidth <= 0 || ViewFrame.ActualHeight <= 0) return;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        renderer.Resize((int)Math.Round(ViewFrame.ActualWidth * dpi.DpiScaleX), (int)Math.Round(ViewFrame.ActualHeight * dpi.DpiScaleY));
        FitModel();
        Draw();
    }

    /// <summary>
    /// Narrows the view for a model too tall to fit (a soldier, a mast), as the Armory's viewer does: its bounding
    /// box's corners all inside 90% of the picture, seen as the view opens on it, at the view's size now.
    /// </summary>
    private void FitModel()
    {
        if (shownMesh is not ModelMesh mesh) return;
        double width = ViewFrame.ActualWidth, height = ViewFrame.ActualHeight;
        var start = new ModelCamera { Yaw = 310, Pitch = 18 };
        start.Aim((mesh.Low + mesh.High) / 2, RadiusOf(mesh), width, height);
        double most = 0;
        for (int corner = 0; corner < 8; corner++)
        {
            var point = new Vector3((corner & 1) != 0 ? mesh.High.X : mesh.Low.X, (corner & 2) != 0 ? mesh.High.Y : mesh.Low.Y, (corner & 4) != 0 ? mesh.High.Z : mesh.Low.Z);
            (double x, double y, _) = start.Project(point);
            if (double.IsNaN(x)) continue;
            most = Math.Max(most, Math.Max(Math.Abs(x - width / 2) / (width / 2), Math.Abs(y - height / 2) / (height / 2)));
        }
        camera.Fit = most > 0 ? Math.Min(1, 0.9 / most) : 1;
    }

    /// <summary>The radius the camera stands back from: half the model's bounding box, corner to corner.</summary>
    private static float RadiusOf(ModelMesh mesh) => Math.Max(50, (mesh.High - mesh.Low).Length() / 2);

    private void Draw()
    {
        if (renderer is null || shownMesh is not ModelMesh mesh || ViewFrame.ActualWidth <= 0 || ViewFrame.ActualHeight <= 0) return;
        double width = ViewFrame.ActualWidth, height = ViewFrame.ActualHeight;
        Vector3 centre = (mesh.Low + mesh.High) / 2;
        camera.Aim(centre, RadiusOf(mesh), width, height);
        var pose = new Matrix4x4[Math.Max(1, mesh.Nodes.Count)];
        Array.Fill(pose, Matrix4x4.Identity);
        var view = new FrameView(camera.ViewProjection, camera.Eye, camera.Far, 0, Vector3.Zero);
        renderer.Draw(view, pose);
        if (!ReferenceEquals(Picture.Source, renderer.Picture)) Picture.Source = renderer.Picture;
        // which marks the model hides, as in the unit's 3D view: checked now and then while the view is dragged, and when it stops
        long now = Environment.TickCount64;
        if (Marks.Visibility == Visibility.Visible && (!dragging || now - lastHiddenCheck > 150))
        {
            lastHiddenCheck = now;
            (hiddenDistances, hiddenWidth, hiddenHeight) = renderer.Distances(view, pose);
        }
        PlaceMarks(mesh);
    }

    private long lastHiddenCheck;
    private float[]? hiddenDistances;
    private int hiddenWidth, hiddenHeight;

    /// <summary>Whether the marks are the firing points (or the turrets' centres), and the turret whose row is under the mouse.</summary>
    private bool showFiringPoints = true;
    private int litTurret = -1;

    /// <summary>
    /// The marks of the unit's 3D view (<see cref="ModelViewer.NumberDisc"/>): a round number, the turret's, on each
    /// firing point or on each turret's centre, as chosen under the view; those of the turret whose row is under the
    /// mouse are larger. Names beside the marks ran into each other on a model with several turrets: the Parts list
    /// names each one.
    /// </summary>
    private void PlaceMarks(ModelMesh mesh)
    {
        Marks.Children.Clear();
        foreach (ModelNode node in mesh.Nodes)
        {
            bool isFiringPoint = node.Name.StartsWith("fx_tourelle", StringComparison.Ordinal) && node.Name.Contains("_tir_", StringComparison.Ordinal);
            bool isTurret = node.Name.StartsWith("tourelle", StringComparison.Ordinal);
            if (showFiringPoints ? !isFiringPoint : !isTurret) continue;
            (double x, double y, double depth) = camera.Project(node.Place);
            if (double.IsNaN(x) || depth <= 0) continue;
            int turret = Number(node.Name);
            // dimmed when the model is in front of it, unless its turret's row is under the mouse; while the view is
            // dragged the last check stands until the next one
            bool hidden = false;
            if (hiddenDistances is float[] distances)
            {
                int px = (int)(x / Marks.ActualWidth * hiddenWidth), py = (int)(y / Marks.ActualHeight * hiddenHeight);
                if (px >= 0 && py >= 0 && px < hiddenWidth && py < hiddenHeight && Marks.ActualWidth > 0)
                    hidden = distances[py * hiddenWidth + px] * camera.Far < Vector3.Distance(node.Place, camera.Eye) - RadiusOf(mesh) * 0.06;
            }
            Border disc = ModelViewer.NumberDisc(turret, hidden && turret != litTurret ? 0.5 : 1);
            Canvas.SetLeft(disc, x - 9);
            Canvas.SetTop(disc, y - 9);
            if (turret == litTurret)
            {
                disc.RenderTransformOrigin = new Point(0.5, 0.5);
                disc.RenderTransform = new ScaleTransform(1.45, 1.45);
                Panel.SetZIndex(disc, 2);
            }
            Marks.Children.Add(disc);
        }
    }

    /// <summary>Firing Points or Turret Centres under the view: one is always pressed.</summary>
    private void MarkKindClicked(object sender, RoutedEventArgs e)
    {
        showFiringPoints = ReferenceEquals(sender, FiringPointsChoice);
        FiringPointsChoice.IsChecked = showFiringPoints;
        TurretCentresChoice.IsChecked = !showFiringPoints;
        Draw();
    }

    /// <summary>A part's row under the mouse: its turret's marks grow, as a turret's row does in the unit's 3D view.</summary>
    private void LightTurret(int turret)
    {
        if (litTurret == turret) return;
        litTurret = turret;
        if (shownMesh is ModelMesh mesh && Marks.Visibility == Visibility.Visible) PlaceMarks(mesh);
    }

    private static int Number(string name)
    {
        string digits = new([.. name.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(char.IsAsciiDigit)]);
        return int.TryParse(digits, out int number) ? number : 1;
    }

    private bool dragging;
    private Point lastPoint;

    private void ViewPressed(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        dragging = ViewFrame.CaptureMouse();
        lastPoint = e.GetPosition(ViewFrame);
        e.Handled = true;
    }

    private void ViewDragged(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        Point point = e.GetPosition(ViewFrame);
        camera.Yaw += (point.X - lastPoint.X) * 0.555;
        camera.Pitch = Math.Clamp(camera.Pitch + (point.Y - lastPoint.Y) * 0.2, -10, 85);
        lastPoint = point;
        Draw();
    }

    private void ViewReleased(object sender, MouseButtonEventArgs e)
    {
        if (dragging && e.ChangedButton == MouseButton.Left)
        {
            ViewFrame.ReleaseMouseCapture();
            dragging = false;
            Draw();
        }
    }

    private void ViewCaptureLost(object sender, MouseEventArgs e) => dragging = false;

    private void ViewScrolled(object sender, MouseWheelEventArgs e)
    {
        camera.Zoom = Math.Clamp(camera.Zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), ModelCamera.LeastZoom, ModelCamera.MostZoom);
        Draw();
        e.Handled = true;
    }

    private void ResetClicked(object sender, RoutedEventArgs e)
    {
        camera.Yaw = 310;
        camera.Pitch = 18;
        camera.Zoom = 1;
        camera.Pan = Vector3.Zero;
        Draw();
    }

    // ---- choosing ----

    private void CopiedClicked(object sender, RoutedEventArgs e)
    {
        own = false;
        ShowAll();
    }

    private void OwnClicked(object sender, RoutedEventArgs e)
    {
        // a file picked before (and the base unit's model chosen since) comes back as it was; only with none is one asked
        // for, and closing that without a file changes nothing: the base unit's model stays up
        if (picked is null)
        {
            PickFile();
            return;
        }
        own = true;
        ShowAll();
    }

    private void BrowseClicked(object sender, RoutedEventArgs e) => PickFile();

    /// <summary>Asks for an .fbx file and reads it; false when none was picked.</summary>
    private bool PickFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Texts.Get("Units.OwnModel.Pick.Title"), Filter = Texts.Get("Units.OwnModel.Pick.Filter") + "|*.fbx" };
        if (picked is not null) dialog.InitialDirectory = System.IO.Path.GetDirectoryName(picked.File);
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return false;
        own = true;
        keptPictures = null;
        keptName = null;
        string file = dialog.FileName;
        // detail levels beside it, named as the game's are (_MID, _LOW), here or in a LODS folder
        midFile = LevelBeside(file, "_MID") ?? midFile;
        lowFile = LevelBeside(file, "_LOW") ?? lowFile;
        _ = ReadPicked(file, reading);
        return true;
    }

    private static string? LevelBeside(string file, string suffix)
    {
        string folder = System.IO.Path.GetDirectoryName(file) ?? "", stem = System.IO.Path.GetFileNameWithoutExtension(file);
        foreach (string place in new[] { System.IO.Path.Combine(folder, stem + suffix + ".fbx"), System.IO.Path.Combine(folder, "LODS", stem + suffix + ".fbx"),
            System.IO.Path.Combine(folder, "LODS", stem, stem + suffix + ".fbx") })
            if (File.Exists(place)) return place;
        return null;
    }

    private void BrowseLevelClicked(object sender, RoutedEventArgs e)
    {
        string level = (string)((FrameworkElement)sender).Tag;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Texts.Get("Units.OwnModel.PickLevel.Title"), Filter = Texts.Get("Units.OwnModel.Pick.Filter") + "|*.fbx" };
        if (picked is not null) dialog.InitialDirectory = System.IO.Path.GetDirectoryName(picked.File);
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (level == "Mid") midFile = dialog.FileName;
        else lowFile = dialog.FileName;
        ShowAll();
    }

    private void ClearLevelClicked(object sender, RoutedEventArgs e)
    {
        if ((string)((FrameworkElement)sender).Tag == "Mid") midFile = null;
        else lowFile = null;
        ShowAll();
    }

    /// <summary>The Parts switch under the view: shows or hides the marks on the model.</summary>
    private void MarksSwitchClicked(object sender, RoutedEventArgs e)
    {
        Marks.Visibility = MarksSwitch.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Draw();
    }

    private void TexturesClicked(object sender, RoutedEventArgs e)
    {
        texturesOpen = !texturesOpen;
        ShowTextures();
    }

    // ---- keeping ----

    /// <summary>
    /// The model of the user's own for a unit, its files kept with the app's pictures (the .fbx as the copy the mod
    /// carries); null when the copied unit's model is kept. Throws when a file cannot be kept.
    /// </summary>
    /// <param name="descriptor">The unit's descriptor, which its look's key is made from.</param>
    /// <param name="keep">Keeps a file's bytes under a name, with the app's pictures; returns where.</param>
    /// <exception cref="IOException">A file could not be read or kept.</exception>
    public OwnModel? Keep(string descriptor, Func<byte[], string, string> keep)
    {
        if (!own || Blocks || picked is null || fit is null || copiedModel?.Look is not (string depiction, string alternatives) || copiedModel.Key is not string copiedKey) return null;
        string key = OwnModel.KeyFor(descriptor);
        var model = new OwnModel
        {
            Key = key, Folder = (Destination(key) ?? "Assets/3D/Units/" + key + "/").TrimEnd('/'), CopiedKey = copiedKey, Depiction = depiction, Alternatives = alternatives,
        };
        // a kept file keeps the name the model was picked with
        string name = keptName ?? System.IO.Path.GetFileName(picked.File);
        model.Files.Add(new OwnModelFile(keep(fit.MakeCopy(picked.Bytes), key + " - " + name), name, "High"));
        var pictures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in picked.Materials.SelectMany(material => material.Found)) pictures.TryAdd(System.IO.Path.GetFileName(file), file);
        foreach ((string level, string? file) in new[] { ("Mid", midFile), ("Low", lowFile) })
        {
            if (file is null) continue;
            string levelName = System.IO.Path.GetFileName(file);
            if (string.Equals(levelName, name, StringComparison.OrdinalIgnoreCase)) levelName = System.IO.Path.GetFileNameWithoutExtension(name) + "_" + level.ToUpperInvariant() + ".fbx";
            model.Files.Add(new OwnModelFile(keep(File.ReadAllBytes(file), key + " - " + levelName), levelName, level));
            try
            {
                foreach (string map in PickedModel.MapsBeside(file, keptPictures).SelectMany(material => material.Found)) pictures.TryAdd(System.IO.Path.GetFileName(map), map);
            }
            catch (InvalidDataException) { }
        }
        // a kept picture keeps the name the model gives it
        foreach ((string given, string file) in keptPictures ?? []) if (pictures.Remove(System.IO.Path.GetFileName(file))) pictures[given] = file;
        // a picture the unit's model has already is used as it is kept
        var keptAlready = new HashSet<string>((IEnumerable<string>?)keptPictures?.Values ?? [], StringComparer.OrdinalIgnoreCase);
        foreach ((string pictureName, string file) in pictures)
            model.Files.Add(new OwnModelFile(keptAlready.Contains(file) ? file : keep(File.ReadAllBytes(file), key + " - " + pictureName), pictureName));
        return model;
    }
}
