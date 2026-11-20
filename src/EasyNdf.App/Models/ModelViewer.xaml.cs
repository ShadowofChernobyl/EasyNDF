using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Effects;
using EasyNdf.Data.Game;
using EasyNdf.Data.Models;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Models;

/// <summary>
/// The 3D model viewer (see ModelViewer.xaml). Nothing is read until it opens: the game's archives are listed, the
/// mesh pack opened and the model read in the background, then drawn on the graphics card, its turrets aimed and fired
/// and its moving parts run as its files say. All of it is let go when the viewer closes.
/// </summary>
public partial class ModelViewer : UserControl
{
    /// <summary>What is being shown: a unit (with its turrets) or a missile.</summary>
    /// <param name="Name">The title.</param>
    /// <param name="Source">Where its files are: the game, or a game mod with its own copies.</param>
    /// <param name="FindModels">Finds its models in the game's files: their paths and their pictures' folders.</param>
    /// <param name="Changed">Its values as the Armory shows them: with the open mod's changes.</param>
    /// <param name="Original">Its values as the game's files have them, before the open mod's changes; null when the mod adds the unit.</param>
    /// <param name="Flies">Whether it is an aircraft, whose turrets do not move on the model.</param>
    /// <param name="Yaw">Which way round the camera starts, in degrees.</param>
    /// <param name="DepictionKey">Its depiction's BlackHoleKey, for its sounds and its moving parts; null for none.</param>
    /// <param name="NoModelText">What the viewer says when no model is found; null for the general text.</param>
    private sealed record Subject(string Name, UnitSource Source, Func<ModelLinks, IReadOnlyList<ModelFile>> FindModels,
        SubjectValues Changed, SubjectValues? Original, bool Flies, double Yaw, string? DepictionKey, string? NoModelText = null);

    /// <summary>The values of a unit the viewer shows: its turrets with their weapons, and how it drives.</summary>
    /// <param name="Turrets">Its turrets; none for a missile.</param>
    /// <param name="Drive">How it drives; null when its files do not say.</param>
    private sealed record SubjectValues(IReadOnlyList<TurretGiven> Turrets, DriveMotion? Drive)
    {
        /// <summary>No turrets, and no driving.</summary>
        public static readonly SubjectValues None = new([], null);

        /// <summary>Every value the viewer uses, written out, to tell whether two sets differ.</summary>
        public string Written => string.Join("\n", Turrets.Select(turret => $"{turret.Turret.Tag} {turret.Turret.Type} {turret.Turret.Motion} {turret.Weapons} {turret.Salvo} {turret.Fires} "
            + string.Join(",", turret.Turret.Mounts.Select(mount => mount.Count)))) + "\n" + Drive;
    }

    /// <summary>The game's files the viewer reads, kept open while it is open.</summary>
    private sealed class OpenFiles(GameArchives archives, IReadOnlyList<IModelPack> packs, GameDataFiles files) : IDisposable
    {
        public GameArchives Archives { get; } = archives;

        /// <summary>Where models are read from: WARNO's mesh packs (a game mod's own first, then the game's), or Steel Division 2's Modding Assets.</summary>
        public IReadOnlyList<IModelPack> Packs { get; } = packs;
        public GameDataFiles GameFiles { get; } = files;
        public ModelLinks Links { get; } = new(files);
        public DepictionReader Depictions { get; } = new(files);
        public FxLibrary Effects { get; } = new(files);
        public FxTextures EffectTextures { get; } = new(archives);

        /// <summary>A sound by its path in the archives, unpacked; null when it is missing or cannot be unpacked.</summary>
        public GameSound? Sound(string path)
        {
            // newer archives keep some sounds without the AllPlatforms/Sound/ folder at the front
            ArchiveFile? file = Archives.Find(path) ?? Archives.Find(path.Replace("AllPlatforms/Sound/", "", StringComparison.OrdinalIgnoreCase));
            if (file is null) return null;
            try
            {
                return EssSound.Decode(file.Read());
            }
            catch (Exception problem) when (problem is InvalidDataException or IOException)
            {
                return null;
            }
        }

        /// <summary>A model from the first pack that has it (a mod's own model, else the game's), with its pictures' folder.</summary>
        public ModelMesh? ReadModel(ModelFile file) =>
            Packs.FirstOrDefault(pack => pack.Has(file.Path))?.Read(file.Path) is ModelMesh mesh ? mesh with { Textures = file.Textures } : null;

        public void Dispose()
        {
            foreach (IModelPack pack in Packs) pack.Dispose();
            GameFiles.Dispose();
        }
    }

    private Subject? subject;
    private OpenFiles? files;
    private UnitDepiction? depiction;
    private ModelSounds? sounds;
    private ModelRenderer? renderer;
    private EffectRenderer? effectRenderer;
    private FxPlayer? effects;
    private ModelScene? scene;
    private IReadOnlyList<ModelFile> modelFiles = [];
    private int shownModel;
    private readonly ModelCamera camera = new();
    private int opening;

    /// <summary>Makes the viewer, closed, with the volume kept from last time.</summary>
    public ModelViewer()
    {
        InitializeComponent();
        settingVolume = true;
        VolumeSlider.Value = Math.Clamp((double?)AppData.Current.State[VolumeKey] ?? 100, 0, 100);
        settingVolume = false;
        ShowVolume();
    }

    // ---- opening ----

    /// <summary>Whether the viewer is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>
    /// Opens the viewer on a unit's model: its turrets are those of <paramref name="loadout"/>, as the open mod has it,
    /// each with its weapons' names; the game's own values are kept beside them, for Mod Changes turned off.
    /// </summary>
    /// <param name="unit">The unit, as the Armory shows it (with the open mod's changes).</param>
    /// <param name="original">The unit as the game's files have it; null when the open mod adds it.</param>
    /// <param name="source">Where the Armory reads it from.</param>
    /// <param name="loadout">Its loadout as shown; null when it has none.</param>
    /// <param name="originalLoadout">Its loadout as the game's files have it; null when it has none.</param>
    /// <param name="weapons">The weapons as the Armory shows them (the open mod's), by their ammunition's descriptor.</param>
    /// <param name="originalWeapons">The weapons as the game's files have them, by their ammunition's descriptor.</param>
    public void OpenUnit(Unit unit, Unit? original, UnitSource source, Loadout? loadout, Loadout? originalLoadout,
        IReadOnlyDictionary<string, Weapon> weapons, IReadOnlyDictionary<string, Weapon> originalWeapons)
    {
        UnitLook? look = unit.Look;
        bool infantry = look?.Mesh is null && look?.Mimetic is not null;
        Open(new Subject(unit.Name, source, links => look is null ? [] : links.ModelsOf(look), ValuesOf(unit, loadout, weapons),
            original is null ? null : ValuesOf(original, originalLoadout, originalWeapons), unit.Flies, infantry ? 340 : unit.Flies ? 305 : 310, infantry ? null : look?.Depiction));
    }

    /// <summary>A unit's turrets, each with its weapons' names and how its first weapon fires, and how it drives.</summary>
    private static SubjectValues ValuesOf(Unit unit, Loadout? loadout, IReadOnlyDictionary<string, Weapon> weapons)
    {
        List<TurretGiven> turrets = [];
        foreach (LoadoutTurret turret in loadout?.Turrets ?? [])
        {
            IEnumerable<string> names = turret.Mounts.Select(mount => weapons.GetValueOrDefault(mount.Ammunition)?.Name ?? mount.Ammunition).Distinct(StringComparer.Ordinal);
            Weapon? first = turret.Mounts.Count > 0 ? weapons.GetValueOrDefault(turret.Mounts[0].Ammunition) : null;
            // a missile or rocket would leave its launcher in flight, which the viewer does not show
            bool launched = first?.Missile is not null || first?.Kind is WeaponKind.Missile or WeaponKind.Rocket;
            turrets.Add(new TurretGiven(turret, string.Join(", ", names), SalvoOf(first), !launched));
        }
        return new SubjectValues(turrets, DriveOf(unit));
    }

    /// <summary>How a weapon fires a salvo, from its ammunition's values; a single shot when it has none.</summary>
    private static SalvoPattern SalvoOf(Weapon? weapon)
    {
        if (weapon is null) return SalvoPattern.Single;
        static double? Seconds(string? text) =>
            double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : null;
        double between = Seconds(weapon.FileValue("TimeBetweenTwoShots")) ?? 0.5;
        return new SalvoPattern(Math.Max(1, (int)Math.Round(weapon.ShotsPerSalvo ?? 1)), between,
            Seconds(weapon.FileValue("TimeBetweenTwoFx")) ?? between, Math.Max(1, (int)(Seconds(weapon.FileValue("SimultaneousShotsCount")) ?? 1)));
    }

    /// <summary>
    /// The game's GRU a second for each km/h: its bomb settings (BombTrajectoryConfigs.ndf) have a plane at 600 km/h
    /// carry a bomb 1,370 GRU in 4.3 seconds. (A smoke grenade's 140 GRU a second is then some 73 m/s, as a real one.)
    /// </summary>
    private const double GruASecondEachKmh = 1370 / 4.3 / 600;

    /// <summary>
    /// The model's units in one of the game's GRU: SmokeDescriptor.ndf turns a smoke's radius in GRU into them as
    /// "53 * 26 * 2.83".
    /// </summary>
    private const double UnitsAGru = 26 * 2.83;

    /// <summary>
    /// How a unit drives, as the open mod has it: at its off-road top speed (MaxSpeedInKmph), speeding up and slowing
    /// down as fast as its MaxAccelerationGRU and MaxDecelerationGRU; null when it has no speed.
    /// </summary>
    private static DriveMotion? DriveOf(Unit unit)
    {
        if (unit.Speed is not (> 0 and double kilometresAnHour)) return null;
        double speed = kilometresAnHour * GruASecondEachKmh * UnitsAGru;
        return new DriveMotion(speed, unit.AccelerationGru is > 0 and double up ? up * UnitsAGru : speed / 2, unit.DecelerationGru is > 0 and double down ? down * UnitsAGru : speed);
    }

    /// <summary>
    /// Opens the viewer on a weapon's own model: a missile's, by its descriptor in MissileDescriptors.ndf; otherwise a
    /// weapon soldiers hold or a bomb or rocket pod an aircraft carries, by its ammunition. Guns have none: they are part
    /// of the model of the unit carrying them, and the viewer says no model is available.
    /// </summary>
    public void OpenWeapon(string name, string ammunition, string? missile, UnitSource source) =>
        Open(new Subject(name, source,
            links => ((missile is not null ? links.MissileModel(missile) : null) ?? links.CarriedModel(ammunition)) is ModelFile file ? [file] : [],
            SubjectValues.None, null, Flies: false, Yaw: 310, DepictionKey: null, NoModelText: Texts.Get("Units.Model.NoWeaponModel")));

    /// <summary>
    /// Whether models of this game can be shown: WARNO's from its mesh pack, Steel Division 2's from its Modding Assets
    /// (its own pack stores its vertices in a way not yet read).
    /// </summary>
    public static bool CanShow(GameTitle game) => game == GameTitle.Warno || game == GameTitle.SteelDivision2;

    private void Open(Subject shown)
    {
        CloseFiles();
        subject = shown;
        TitleText.Text = shown.Name;
        FileText.Text = "";
        TriangleText.Text = "";
        SizeText.Text = "";
        SoldierChoice.Visibility = Visibility.Collapsed;
        SidePanel.Children.Clear();
        ShowSidePanel(false);
        Marks.Children.Clear();
        Picture.Source = null;
        StatusText.Text = Texts.Get("Units.Model.Reading");
        camera.Yaw = shown.Yaw;
        camera.Pitch = 18;
        camera.Zoom = 1;
        camera.Pan = Vector3.Zero;
        StopGlide();
        turntable = false;
        if (Visibility != Visibility.Visible) outsideAtOpen = OutsideDotNetNow();
        Visibility = Visibility.Visible;
        FitToWindow();
        Dispatcher.BeginInvoke(() => Box.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), System.Windows.Threading.DispatcherPriority.Input);
        _ = ReadAndShow(shown, ++opening);
    }

    /// <summary>Lists the game's archives, opens the mesh pack, finds the models, then shows the first.</summary>
    private async Task ReadAndShow(Subject shown, int attempt)
    {
        try
        {
            // the game as the source, with the mods' changes shown: the files as the mods that run make them, and their
            // export folders' generated models (see ModdedModelFiles)
            string? cache = AppData.Current.Folders?.Cache;
            ModdedModelFiles.Work? modded = shown.Source.Mod is null && Pages.UnitsPage.ArmoryShowsModChanges && MainView.Find(this) is MainView main
                ? ModdedModelFiles.Prepare([.. main.FilesTab.Model.Entries], shown.Source.Game, cache) : null;
            (OpenFiles opened, IReadOnlyList<ModelFile> models) = await Task.Run(() =>
            {
                ModdedModelFiles.Found? found = modded is null || cache is null ? null : ModdedModelFiles.Run(modded, cache);
                static bool Wanted(ArchiveFile file) =>
                    MeshPack.IsPackFile(file.Name) || ModelTextures.IsModelTextureFile(file.Name) || IsSoundFile(file.Name) || FxTextures.IsEffectTextureFile(file.Name);
                bool steelDivision = shown.Source.Game == GameTitle.SteelDivision2;
                // Steel Division 2's models and their textures come from its Modding Assets, not its archives
                GameArchives archives = GameArchives.Open(shown.Source.GameFolder, steelDivision ? _ => false : Wanted);
                IReadOnlyList<IModelPack> packs;
                if (steelDivision)
                    packs = [ModdingAssets.Find(shown.Source.GameFolder) ?? throw new InvalidDataException(Texts.Get("Units.Model.NoAssets"))];
                else
                {
                    // a game mod's own models and textures, as the game's mod tools build them into its Gen folder
                    if (shown.Source.Mod?.Folder is string modFolder) archives.AddLooseFiles(Path.Combine(modFolder, "Gen"), Wanted);
                    foreach (string gen in found?.GenFolders ?? []) archives.AddLooseFiles(gen, Wanted);
                    MeshPack pack = MeshPack.Open(archives) ?? throw new InvalidDataException(Texts.Get("Units.Model.NoPack"));
                    packs = [.. MeshPack.OpenModPacks(archives), pack];
                }
                var gameFiles = new GameDataFiles(GameInstall.BaseZipOf(shown.Source.GameFolder), shown.Source.Mod?.Folder, changedOnDisk: found?.Files);
                var open = new OpenFiles(archives, packs, gameFiles);
                return (open, shown.FindModels(open.Links));
            });
            UnitDepiction? read = shown.DepictionKey is string key ? await Task.Run(() => opened.Depictions.Read(key)) : null;
            // the depiction files read take some 24 MB, and are not needed again
            opened.Depictions.Release();
            if (attempt != opening)
            {
                opened.Dispose();
                return;
            }
            files = opened;
            modelFiles = models;
            depiction = read;
            sounds = new ModelSounds(opened.Sound) { IsOn = SoundSwitch.IsChecked == true, Loudness = LoudnessOf(VolumeSlider.Value) };
            if (models.Count == 0)
            {
                StatusText.Text = shown.NoModelText ?? Texts.Get("Units.Model.NoModel");
                return;
            }
            string? problem = null;
            if (renderer is null && (renderer = ModelRenderer.Create(out problem)) is not null) renderer.PictureBack += Redraw;
            if (renderer is null)
            {
                StatusText.Text = Texts.Get("Units.Model.CannotDraw", ("problem", problem ?? ""));
                return;
            }
            renderer.ShowsFloor = showFloor;
            renderer.ShowsGrid = showGrid;
            renderer.ShowsWireframe = showWireframe;
            effectRenderer = new EffectRenderer(renderer, opened.EffectTextures);
            await ShowModel(0, attempt);
        }
        catch (Exception problem) when (problem is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (attempt == opening) StatusText.Text = Texts.Get("Units.Model.CannotRead", ("problem", problem.Message));
        }
    }

    /// <summary>
    /// Reads one of the subject's models (a squad's soldier) and its pictures in the background, with what its
    /// depiction hangs on it (a launcher's turret, pylons, pods, missiles and bombs), then draws it.
    /// </summary>
    private async Task ShowModel(int index, int attempt)
    {
        if (files is null || renderer is null || subject is null) return;
        shownModel = index;
        ModelFile file = modelFiles[index];
        string path = file.Path;
        OpenFiles reading = files;
        UnitDepiction? hanging = depiction;
        ModelAssembly.Assembled? assembled = await Task.Run(() =>
        {
            ModelMesh? read = reading.ReadModel(file);
            return read is null ? null : ModelAssembly.Assemble(read, PicturesOf(reading, read), hanging, reading.Links.MeshPath, reading.ReadModel, model => PicturesOf(reading, model));
        });
        if (attempt != opening || renderer is null) return;
        if (assembled is null)
        {
            StatusText.Text = Texts.Get(reading.Packs.Any(pack => pack is ModdingAssets) ? "Units.Model.NotInAssets" : "Units.Model.NotInPack", ("model", path));
            return;
        }
        Show(assembled, depiction, path);
    }

    /// <summary>Draws a model read, with its munitions, moving as its depiction says.</summary>
    private void Show(ModelAssembly.Assembled assembled, UnitDepiction? shownDepiction, string path)
    {
        if (files is null || renderer is null || subject is null) return;
        ModelMesh mesh = assembled.Mesh;
        // a model with nothing in it, such as main_nue.fbx (an empty hand) that grenades name: the game draws nothing
        if (mesh.TriangleCount == 0)
        {
            FileText.Text = Path.GetFileName(path);
            TriangleText.Text = SizeText.Text = "";
            StatusText.Text = Texts.Get("Units.Model.EmptyModel", ("model", Path.GetFileName(path)));
            return;
        }
        renderer.Load(mesh, assembled.Pictures);
        hungLaunchers = assembled.Launchers;
        scene = new ModelScene(mesh, Values.Turrets, subject.Flies, shownDepiction, Values.Drive, hungLaunchers);
        scene.ShowsBlur = !hideEffects;
        effects?.Clear();
        effects = new FxPlayer(files.Effects);
        movementEffects.Clear();
        rotorBlurs.Clear();
        PrepareSounds();
        FileText.Text = Path.GetFileName(path);
        TriangleText.Text = Texts.Count("Units.Model.Triangles", mesh.TriangleCount, ("count", Texts.Number(mesh.TriangleCount)));
        SizeText.Text = SizeOf(mesh);
        StatusText.Text = "";
        ShowSoldierChoice();
        BuildSidePanel();
        FitModel();
        ViewResizedNow();
    }

    // ---- the side panel: the turrets, their marks, aiming ----

    private readonly List<(FrameworkElement Mark, int Node, bool IsFiringPoint, int Turret)> marks = [];
    // the marks start hidden, so the model is seen as it is
    private bool showMarks, showFiringPoints = true;
    private int pickedTurret, litTurret = -1;
    private Border? aimBox;
    private readonly List<Border> turretRows = [];

    /// <summary>How wide the side panel is: wide for a model with turrets, narrow for one with only switches.</summary>
    private double sideWidth = WideSide;

    private const double WideSide = 252, NarrowSide = 150;

    /// <summary>
    /// Shows or hides the side panel, wide for turrets or narrow, the view taking the width a narrow panel leaves, so
    /// the viewer stays the same size.
    /// </summary>
    private void ShowSidePanel(bool show, bool wide = true)
    {
        sideWidth = wide ? WideSide : NarrowSide;
        SideScroller.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SideGap.Width = new GridLength(show ? 12 : 0);
        SideColumn.Width = new GridLength(show ? sideWidth : 0);
    }

    /// <summary>How wide the view is at its full size: 880, and the width a narrow side panel leaves.</summary>
    private double ViewWidth => 880 + WideSide - sideWidth;

    // ---- the open mod's changes ----

    /// <summary>Whether the unit is shown with the open mod's changes (Mod Changes), or as the game's files have it.</summary>
    private bool showModChanges = true;

    /// <summary>The values shown: with the open mod's changes, or the game's own when Mod Changes is off.</summary>
    private SubjectValues Values => subject is null ? SubjectValues.None : showModChanges || subject.Original is null ? subject.Changed : subject.Original;

    /// <summary>
    /// Mod Changes, at the top of the side panel: on, the unit turns, fires and drives as the open mod's changes make
    /// it, before they are exported; off, as the game's files have it. Dim when the mod adds the unit, or its changes
    /// touch nothing the viewer shows.
    /// </summary>
    private void AddModChangesSwitch()
    {
        if (subject is null) return;
        bool differs = subject.Original is SubjectValues original && original.Written != subject.Changed.Written;
        var toggle = new CheckBox
        {
            Style = (Style)FindResource("Toggle"),
            Content = Texts.Get("Units.Model.ModChanges"),
            IsChecked = showModChanges || !differs,
            IsEnabled = differs,
            Margin = new Thickness(0, 2, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = Texts.Get(differs ? "Units.Model.ModChanges.Tip" : subject.Original is null ? "Units.Model.ModChanges.Added.Tip" : "Units.Model.ModChanges.Same.Tip"),
        };
        ToolTipService.SetShowOnDisabled(toggle, true);
        toggle.Click += (_, _) =>
        {
            showModChanges = toggle.IsChecked == true;
            UseValues();
        };
        SidePanel.Children.Add(toggle);
    }

    /// <summary>The model moved with the values now chosen: its scene made again, its side panel too.</summary>
    private void UseValues()
    {
        if (scene is null || subject is null) return;
        StopMovementEffects();
        rotorBlurs.Clear();
        effects?.Clear();
        firing.Clear();
        running?.FadeOut(0.2);
        running = null;
        scene = new ModelScene(scene.Mesh, Values.Turrets, subject.Flies, depiction, Values.Drive, hungLaunchers);
        BuildSidePanel();
        Redraw();
    }

    /// <summary>
    /// The side panel: for a model with turrets, Show on Model, Firing Points or Turret Centres, the turrets (a click
    /// picks one to aim; hovering one enlarges its marks), and the picked turret's aiming; then its moving parts, and
    /// how the model is shown.
    /// </summary>
    private void BuildSidePanel()
    {
        SidePanel.Children.Clear();
        turretRows.Clear();
        Marks.Children.Clear();
        marks.Clear();
        IReadOnlyList<ViewTurret> turrets = scene?.Turrets ?? [];
        ShowSidePanel(true, wide: turrets.Count > 0);
        AddModChangesSwitch();
        if (turrets.Count == 0)
        {
            AddEngineSwitch();
            AddViewSwitches();
            FitToWindow();
            return;
        }
        pickedTurret = Math.Clamp(pickedTurret, 0, turrets.Count - 1);
        SidePanel.Children.Add(Heading(Texts.Get("Units.Model.Turrets"), first: SidePanel.Children.Count == 0));

        var showSwitch = new CheckBox { Style = (Style)FindResource("Toggle"), Content = Texts.Get("Units.Model.ShowOnModel"), IsChecked = showMarks, Margin = new Thickness(0, 2, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        showSwitch.Click += (_, _) =>
        {
            showMarks = showSwitch.IsChecked == true;
            Redraw();
        };
        SidePanel.Children.Add(showSwitch);
        SidePanel.Children.Add(MarkKindChoice());

        var list = new StackPanel();
        for (int i = 0; i < turrets.Count; i++) list.Children.Add(TurretRow(turrets[i], i));
        SidePanel.Children.Add(new Bevel
        {
            TopLeft = (Brush)FindResource("lo"),
            BottomRight = (Brush)FindResource("hi"),
            Background = (Brush)FindResource("well"),
            Child = list,
        });
        aimBox = new Border();
        SidePanel.Children.Add(aimBox);
        ShowPicked();
        BuildAim();
        AddEngineSwitch();
        AddViewSwitches();
        FitToWindow();

        foreach (ViewTurret turret in turrets)
        {
            foreach (int point in turret.FiringPoints) AddMark(point, true, turret.Number);
            if (turret.Centre >= 0) AddMark(turret.Centre, false, turret.Number);
        }
    }

    // ---- how the model is shown ----

    private bool turntable, showGrid = true, showFloor = true, showWireframe, hideEffects;
    private CheckBox? turntableSwitch;

    /// <summary>View, under the rest: Turntable, Ground Grid, Floor, Wireframe and Hide Effects.</summary>
    private void AddViewSwitches()
    {
        SidePanel.Children.Add(Heading(Texts.Get("Units.Model.ViewHeading"), first: SidePanel.Children.Count == 0));
        turntableSwitch = AddSwitch("Units.Model.Turntable", "Units.Model.Turntable.Tip", turntable, on =>
        {
            turntable = on;
            if (on) StartMoving();
        });
        AddSwitch("Units.Model.Grid", "Units.Model.Grid.Tip", showGrid, on =>
        {
            showGrid = on;
            if (renderer is not null) renderer.ShowsGrid = on;
            Redraw();
        });
        AddSwitch("Units.Model.Floor", "Units.Model.Floor.Tip", showFloor, on =>
        {
            showFloor = on;
            if (renderer is not null) renderer.ShowsFloor = on;
            Redraw();
        });
        AddSwitch("Units.Model.Wireframe", "Units.Model.Wireframe.Tip", showWireframe, on =>
        {
            showWireframe = on;
            if (renderer is not null) renderer.ShowsWireframe = on;
            Redraw();
        });
        // no dust, smoke, flashes or rotor blur: the blades show turning instead; sounds play on
        AddSwitch("Units.Model.HideEffects", "Units.Model.HideEffects.Tip", hideEffects, on =>
        {
            hideEffects = on;
            if (scene is not null) scene.ShowsBlur = !on;
            if (on)
            {
                StopMovementEffects();
                foreach (FxEffect blur in rotorBlurs.Values) effects?.Stop(blur);
                rotorBlurs.Clear();
                effects?.Clear();
            }
            else if (engineSwitch?.IsChecked == true) StartMovementEffects();
            Redraw();
        });
        GridScale.Visibility = showGrid ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A switch of the side panel, with its text and tip.</summary>
    private CheckBox AddSwitch(string text, string tip, bool on, Action<bool> switched)
    {
        var toggle = new CheckBox { Style = (Style)FindResource("Toggle"), Content = Texts.Get(text), ToolTip = Texts.Get(tip), IsChecked = on, Margin = new Thickness(0, 2, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
        toggle.Click += (_, _) =>
        {
            switched(toggle.IsChecked == true);
            GridScale.Visibility = showGrid ? Visibility.Visible : Visibility.Collapsed;
        };
        SidePanel.Children.Add(toggle);
        return toggle;
    }

    /// <summary>The game's units in a metre (GameUnitsPerLBU).</summary>
    private const float UnitsAMetre = 215;

    /// <summary>The model's length, width and height in metres, as the game draws it.</summary>
    private static string SizeOf(ModelMesh mesh)
    {
        Vector3 size = (mesh.High - mesh.Low) / UnitsAMetre;
        string Metres(float value) => value.ToString("0.0", Texts.Catalog.Culture);
        return Texts.Get("Units.Model.Size", ("length", Metres(size.X)), ("width", Metres(size.Y)), ("height", Metres(size.Z)));
    }

    private Bevel Heading(string text, bool first = false) =>
        new() { Style = (Style)FindResource("Heading"), Child = new TextBlock { Text = text }, Margin = new Thickness(0, first ? 0 : 10, 0, 6) };

    /// <summary>Firing Points or Turret Centres: which marks show.</summary>
    private FrameworkElement MarkKindChoice()
    {
        var firing = new ToggleButton { Style = (Style)FindResource("PressedToggle"), Content = Texts.Get("Units.Model.FiringPoints"), IsChecked = showFiringPoints, Padding = new Thickness(4, 3, 4, 3) };
        var centres = new ToggleButton { Style = (Style)FindResource("PressedToggle"), Content = Texts.Get("Units.Model.TurretCentres"), IsChecked = !showFiringPoints, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(-1, 0, 0, 0) };
        void Choose(bool firingPoints)
        {
            showFiringPoints = firingPoints;
            firing.IsChecked = firingPoints;
            centres.IsChecked = !firingPoints;
            Redraw();
        }
        firing.Click += (_, _) => Choose(true);
        centres.Click += (_, _) => Choose(false);
        var row = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(firing);
        row.Children.Add(centres);
        return row;
    }

    /// <summary>One turret in the list: its number, its name and its weapons. A click picks it to aim.</summary>
    private Border TurretRow(ViewTurret turret, int index)
    {
        var name = new TextBlock { Text = Texts.Get("Units.Model.Turret", ("number", turret.Number)) };
        var weapons = new TextBlock { Text = turret.Weapons, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        weapons.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var text = new StackPanel { Children = { name, weapons } };
        Border disc = NumberDisc(turret.Number, 1);
        disc.Margin = new Thickness(0, 1, 7, 0);
        DockPanel.SetDock(disc, Dock.Left);
        // the row's line under it, and inside it the accent edge the picked row has
        var edge = new Border { Padding = new Thickness(7, 5, 7, 5), Child = new DockPanel { Children = { disc, text } } };
        edge.SetResourceReference(Border.BorderBrushProperty, "accent");
        var row = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Cursor = Cursors.Hand, Focusable = true, Child = edge };
        row.SetResourceReference(Border.BorderBrushProperty, "line");
        System.Windows.Automation.AutomationProperties.SetName(row, name.Text + ", " + turret.Weapons);
        row.MouseEnter += (_, _) => Light(index);
        row.MouseLeave += (_, _) => Light(-1);
        row.GotKeyboardFocus += (_, _) => Light(index);
        row.LostKeyboardFocus += (_, _) => Light(-1);
        row.MouseLeftButtonUp += (_, _) => Pick(index);
        row.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true;
            Pick(index);
        };
        turretRows.Add(row);
        return row;
    }

    private void Light(int index)
    {
        litTurret = index;
        Redraw();
    }

    private void Pick(int index)
    {
        pickedTurret = index;
        ShowPicked();
        BuildAim();
    }

    /// <summary>The picked turret's row in the selection colour with an accent edge; the row under the mouse in the hover colour.</summary>
    private void ShowPicked()
    {
        for (int i = 0; i < turretRows.Count; i++)
        {
            Border row = turretRows[i], edge = (Border)row.Child;
            bool picked = i == pickedTurret;
            if (picked) row.SetResourceReference(Border.BackgroundProperty, "sel");
            else row.Background = Brushes.Transparent;
            row.BorderThickness = new Thickness(0, 0, 0, i == turretRows.Count - 1 ? 0 : 1);
            edge.BorderThickness = new Thickness(picked ? 3 : 0, 0, 0, 0);
            edge.Padding = new Thickness(picked ? 4 : 7, 5, 7, 5);
        }
    }

    /// <summary>
    /// The picked turret's aiming: a pad within its limits (the open mod's), left and right to turn it, up and down to
    /// raise its gun, which it follows at its speed (<see cref="AimPad"/>); under it where it aims now, then Fire (for a
    /// gun) and Centre. A turret that does not turn on the model gets a line saying so, then Fire.
    /// </summary>
    private void BuildAim()
    {
        aimPad = null;
        aimTurnValue = aimGunValue = null;
        if (aimBox is null || scene is null || scene.Turrets.Count == 0) return;
        ViewTurret turret = scene.Turrets[pickedTurret];
        var panel = new StackPanel();
        panel.Children.Add(Heading(Texts.Get("Units.Model.Aim", ("number", turret.Number))));
        fireButton = turret.Fires ? new Button { Content = Texts.Get("Units.Model.Fire"), ToolTip = Texts.Get("Units.Model.Fire.Tip"), MinWidth = 72 } : null;
        if (fireButton is not null) fireButton.Click += (_, _) => Fire(turret);
        if (!turret.CanAim)
        {
            var note = new TextBlock { Text = Texts.Get(subject?.Flies == true ? "Units.Model.FixedAircraft" : "Units.Model.Fixed"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            panel.Children.Add(note);
            if (fireButton is not null) panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { fireButton } });
            aimBox.Child = panel;
            return;
        }
        bool turns = turret.TurnNode >= 0, raises = turret.RaiseNode >= 0;
        var pad = new AimPad(turret, turns, raises, StartMoving) { Margin = new Thickness(0, 2, 0, 0) };
        panel.Children.Add(pad);
        // how far it turns each way, under the pad's ends
        if (turns)
        {
            string limit = Math.Round(turret.TurnLimit) + "°";
            var ends = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
            TextBlock right = Small(Texts.Get("Units.Model.Pad.Right", ("degrees", limit)));
            DockPanel.SetDock(right, Dock.Right);
            ends.Children.Add(right);
            ends.Children.Add(Small(Texts.Get("Units.Model.Pad.Left", ("degrees", limit))));
            panel.Children.Add(ends);
        }
        // where it aims now, which follows the pad as it turns
        var values = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        TextBlock Value(string name)
        {
            var label = new TextBlock { Text = name, Margin = new Thickness(values.Children.Count == 0 ? 0 : 12, 0, 6, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            var value = new TextBlock { MinWidth = 30, TextAlignment = TextAlignment.Right };
            values.Children.Add(label);
            values.Children.Add(value);
            return value;
        }
        if (turns) aimTurnValue = Value(Texts.Get("Units.Model.Turn"));
        if (raises) aimGunValue = Value(Texts.Get("Units.Model.Gun"));
        var centre = new Button { Content = Texts.Get("Units.Model.Centre"), Margin = new Thickness(6, 0, 0, 0) };
        centre.Click += (_, _) => pad.Centre();
        // Centre at the right of the values; with Fire as well, both on a line of their own, as there is no room for all
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = true };
        if (fireButton is null)
        {
            DockPanel.SetDock(centre, Dock.Right);
            row.Children.Add(centre);
            row.Children.Add(values);
            panel.Children.Add(row);
        }
        else
        {
            row.Children.Add(values);
            panel.Children.Add(row);
            centre.Margin = new Thickness(6, 0, 0, 0);
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { fireButton, centre } });
        }
        aimPad = pad;
        ShowAimNow();
        aimBox.Child = panel;
    }

    /// <summary>A small muted line of text.</summary>
    private static TextBlock Small(string text)
    {
        var small = new TextBlock { Text = text, FontSize = 11 };
        small.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return small;
    }

    private AimPad? aimPad;
    private TextBlock? aimTurnValue, aimGunValue;

    /// <summary>Whether the picked turret was turning at the last frame, so the pad shows where it stopped.</summary>
    private bool aimWasMoving;

    /// <summary>Shows where the picked turret aims now, on its pad and under it: the values in the accent colour while it turns.</summary>
    private void ShowAimNow()
    {
        if (aimPad is null || scene is null || pickedTurret >= scene.Turrets.Count) return;
        ViewTurret turret = scene.Turrets[pickedTurret];
        string colour = turret.IsTurning ? "accent" : "ink";
        if (aimTurnValue is not null)
        {
            aimTurnValue.Text = Signed(turret.Turn);
            aimTurnValue.SetResourceReference(TextBlock.ForegroundProperty, colour);
        }
        if (aimGunValue is not null)
        {
            aimGunValue.Text = Signed(turret.Raise);
            aimGunValue.SetResourceReference(TextBlock.ForegroundProperty, colour);
        }
        aimPad.ShowNow();
    }

    // ---- firing ----

    private Button? fireButton;
    private readonly HashSet<ViewTurret> firing = [];

    /// <summary>
    /// Fires the turret's first weapon: a salvo of the ammunition's shots (the open mod's values), each with the
    /// depiction's sound (a stream of fire sounds once for the burst), its muzzle effects, its gun's recoil and, on a
    /// gatling, its barrels spinning. The shots themselves are not shown flying. A click while the turret's salvo is
    /// still going does nothing; Fire stays clickable, as a button made unclickable would take the keyboard away from
    /// the viewer, and Escape with it.
    /// </summary>
    private void Fire(ViewTurret turret)
    {
        if (scene is null || !turret.Fires || !firing.Add(turret)) return;
        SalvoPattern salvo = turret.Salvo;
        SoundChoice? sound = turret.Weapon?.Look?.Sound;
        bool stream = turret.Weapon?.FiresContinuously == true;
        // a stream of fire plays one recording of a burst: its flashes follow the shots heard in it (as many, as far
        // apart, from the first), not the ammunition's count, nor the recording's length, which runs on with echo
        double heardFrom = 0;
        if (stream && sounds?.Play(sound) is PlayingSound burstSound && SoundShots.Of(burstSound.Sound) is SoundShots.Shots heard)
        {
            salvo = salvo with { Shots = heard.Count * salvo.AtOnce, SecondsBetweenShots = heard.Between / burstSound.Speed };
            heardFrom = heard.First / burstSound.Speed;
        }
        int volleys = Math.Max(1, salvo.Shots / salvo.AtOnce);
        // each shot's time: shots fired together go off a moment apart, as the time between their effects says
        var times = new List<double>();
        for (int volley = 0; volley < volleys; volley++)
            for (int shot = 0; shot < salvo.AtOnce; shot++)
                times.Add(heardFrom + volley * salvo.SecondsBetweenShots + (salvo.AtOnce > 1 ? shot * salvo.SecondsBetweenEffects : 0));
        // the shots that start the weapon's effects, as the game starts them: once a burst, told how many shots it has
        // (the scripts repeat their flashes that many times): a stream of fire's whole salvo is one burst; other
        // weapons start one every TimeBetweenTwoFx, and shots fired together one each
        var starts = new List<int>();
        for (int i = 0; i < times.Count; i++)
            if (starts.Count == 0 || (!stream && (salvo.AtOnce > 1 || times[i] - times[starts[^1]] >= salvo.SecondsBetweenEffects - 1e-6))) starts.Add(i);
        var started = new List<FxEffect>();
        for (int i = 0; i < times.Count; i++)
        {
            int number = i, burst = starts.IndexOf(i);
            int shots = burst < 0 ? 0 : (burst + 1 < starts.Count ? starts[burst + 1] : times.Count) - i;
            scene.Later(times[i], () =>
            {
                if (!stream) sounds?.Play(sound);
                if (turret.RecoilNode >= 0) turret.RecoilTime = 0;
                if (shots > 0) started.AddRange(PlayMuzzleEffects(turret, number, shots, salvo.SecondsBetweenShots));
            });
        }
        if (scene.GatlingNode >= 0 && scene.Mesh.Nodes[scene.GatlingNode].Name.EndsWith(turret.Number.ToString("00", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            scene.SpinGatling(salvo.Seconds + Math.Max(0.3, salvo.SecondsBetweenShots));
        scene.Later(salvo.Seconds + 0.3, () => firing.Remove(turret));
        // the game stops a weapon's effects when it stops firing; the scripts end their own, so this only stops one
        // that would run on
        // a stream of fire's effects stop with its last shot heard, as the game stops a weapon's when it stops firing:
        // no flash after the sound, though smoke already made drifts on; others are left to end by themselves
        scene.Later(stream ? heardFrom + salvo.Seconds + salvo.SecondsBetweenShots : salvo.Seconds + EffectsOutlastSalvo, () =>
        {
            foreach (FxEffect effect in started) effects?.Stop(effect);
        });
        StartMoving();
    }

    /// <summary>How long a weapon's effects may run on after its salvo, in seconds: long enough for a .50 cal's last casings.</summary>
    private const double EffectsOutlastSalvo = 6;

    /// <summary>
    /// A burst's muzzle effects, from the depiction's weapon: one of its firing effects at the muzzle the burst leaves
    /// from, following the gun, told how many shots the burst has and the time between them. Gives the effects started.
    /// </summary>
    private List<FxEffect> PlayMuzzleEffects(ViewTurret turret, int shot, int shots, double secondsBetweenShots)
    {
        var started = new List<FxEffect>();
        if (hideEffects || effects is null || scene is null || turret.Weapon?.Look is not WeaponLook look || look.FiringEffects.Count == 0 || turret.Muzzles.Count == 0) return started;
        ModelScene shown = scene;
        int muzzle = turret.Muzzles[shot % turret.Muzzles.Count];
        var burst = new Dictionary<string, FxValue>
        {
            ["parShotCount"] = FxValue.Of(shots),
            ["parWaitBetweenShot"] = FxValue.Of(secondsBetweenShots),
            ["parInitialShotDelay"] = FxValue.Of(0),
            ["parShotOnGround"] = FxValue.Of(false),
        };
        if (effects.Start(look.FiringEffects[Random.Shared.Next(look.FiringEffects.Count)], new FxFrame(() => shown.NodeMatrix(muzzle)), burst) is FxEffect firingEffect)
            started.Add(firingEffect);
        return started;
    }

    // ---- the engine: driving, rotors, jet engines ----

    private CheckBox? engineSwitch;
    private PlayingSound? running;

    /// <summary>What the engine switch starts: driving for a vehicle with wheels or tracks, a helicopter's rotors, an aircraft's engines; null for none.</summary>
    private string? EngineSwitchText()
    {
        if (scene is null) return null;
        // a propeller plane's switch starts its engines, which turn its propellers
        if (scene.HasRotors && depiction?.Rotors.All(rotor => rotor.IsPropeller) != true) return Texts.Get("Units.Model.Rotors");
        if (scene.HasRotors) return Texts.Get("Units.Model.Engines");
        if (subject?.Flies == true) return Texts.Get("Units.Model.Engines");
        return scene.CanDrive ? Texts.Get("Units.Model.Drive") : null;
    }

    /// <summary>Moving Parts, with its one switch, under the turrets.</summary>
    private void AddEngineSwitch()
    {
        engineSwitch = null;
        if (EngineSwitchText() is not string text) return;
        SidePanel.Children.Add(Heading(Texts.Get("Units.Model.MovingParts")));
        engineSwitch = new CheckBox { Style = (Style)FindResource("Toggle"), Content = text, HorizontalAlignment = HorizontalAlignment.Left };
        engineSwitch.Click += (_, _) => RunEngine(engineSwitch.IsChecked == true);
        SidePanel.Children.Add(engineSwitch);
    }

    /// <summary>
    /// Starts or stops the engine as the game's movement-sound operator plays it: the start sound, then the running
    /// sound looped, rising over its fade-in; on stopping, the running sound fades out over its fade-out and the stop
    /// sound plays. A vehicle drives once its engine has started, a plane flies off; rotors and propellers spin up over
    /// the start sound.
    /// </summary>
    private void RunEngine(bool on)
    {
        if (scene is null) return;
        EngineSounds? engine = depiction?.Engine;
        double startSeconds = SecondsOf(engine?.Start);
        running?.FadeOut(on ? 0.05 : engine?.FadeOut ?? 1);
        running = null;
        if (on)
        {
            sounds?.Play(engine?.Start);
            running = sounds?.Play(engine?.Running, loop: true, fadeIn: engine?.FadeIn ?? 0.5, delay: Math.Max(0, startSeconds - (engine?.FadeIn ?? 0.5)));
            if (scene.HasRotors) scene.TurnRotors(true, startSeconds > 0 ? startSeconds : 5);
            // a plane (jet or propeller) flies off at its speed once its engines run, wheels or not; a helicopter hovers
            bool plane = subject?.Flies == true && depiction?.Rotors.Any(rotor => !rotor.IsPropeller) != true;
            if (plane || (!scene.HasRotors && scene.CanDrive))
            {
                scene.Later(startSeconds * (plane ? 0.5 : 0.7), () =>
                {
                    if (engineSwitch?.IsChecked != true) return;
                    scene?.Drive(true);
                    StartMovementEffects();
                });
            }
            else if (subject?.Flies == true) scene.Later(startSeconds * 0.5, () => { if (engineSwitch?.IsChecked == true) StartMovementEffects(); });
        }
        else
        {
            sounds?.Play(engine?.Stop);
            scene.TurnRotors(false, 8);
            scene.Drive(false);
            StopMovementEffects();
        }
        StartMoving();
    }

    private readonly List<FxEffect> movementEffects = [];
    private readonly Dictionary<int, FxEffect> rotorBlurs = [];

    /// <summary>
    /// The rotors' blur, as the game draws it in place of their blades once they turn fast: each rotor's blur action
    /// (FX_Binder_Rotor at its hub, with its blades' length and count) while the rotors are fast enough, stopped below.
    /// </summary>
    private void ShowRotorBlur()
    {
        if (scene is null || effects is null || hideEffects) return;
        ModelScene shown = scene;
        // each rotor's blur, on while that rotor turns fast enough for it, off as it slows
        for (int i = 0; i < shown.RotorBlurs.Count; i++)
        {
            bool wanted = shown.IsBlurred(i);
            if (wanted == rotorBlurs.ContainsKey(i)) continue;
            if (!wanted)
            {
                effects.Stop(rotorBlurs[i]);
                rotorBlurs.Remove(i);
                continue;
            }
            (int hub, FxAttachment blur) = shown.RotorBlurs[i];
            if (effects.Start(blur.Effect, new FxFrame(() => shown.NodeMatrix(hub)), blur.Given) is FxEffect started) rotorBlurs[i] = started;
        }
    }

    /// <summary>
    /// The effects the depiction plays as the unit moves (its DepictionAction_MovementFX_...): the dust a vehicle's
    /// wheels or tracks raise, a jet's exhaust and flames at its engines, each at its node, until the engine stops.
    /// </summary>
    private void StartMovementEffects()
    {
        if (hideEffects || effects is null || scene is null || files is null || depiction is null || movementEffects.Count > 0) return;
        ModelScene shown = scene;
        foreach (FxAttachment attachment in FxAttachments.Read(files.GameFiles, depiction.MovementEffects))
        {
            int node = shown.Mesh.NodeNamed(attachment.Anchor);
            if (node >= 0 && effects.Start(attachment.Effect, new FxFrame(() => shown.NodeMatrix(node))) is FxEffect started) movementEffects.Add(started);
        }
        StartMoving();
    }

    private void StopMovementEffects()
    {
        foreach (FxEffect effect in movementEffects) effects?.Stop(effect);
        movementEffects.Clear();
    }

    /// <summary>A model's parts' pictures, from the atlas in its pictures' folder; a Steel Division 2 model's from the PNG files its materials name.</summary>
    private static List<MaterialPictures> PicturesOf(OpenFiles reading, ModelMesh model)
    {
        if (reading.Packs.OfType<ModdingAssets>().FirstOrDefault() is ModdingAssets assets) return AssetPictures.Of(assets, model);
        var textures = ModelTextures.For(reading.Archives, model);
        var byMaterial = new Dictionary<ModelMaterial, MaterialPictures>();
        List<MaterialPictures> each = [];
        foreach (ModelPart part in model.Parts)
        {
            if (!byMaterial.TryGetValue(part.Material, out MaterialPictures? maps)) byMaterial[part.Material] = maps = textures.PicturesOf(part.Material);
            each.Add(maps);
        }
        return each;
    }

    /// <summary>Where the weapons hung on the model shown fire from, by weapon number.</summary>
    private IReadOnlyDictionary<int, IReadOnlyList<int>> hungLaunchers = new Dictionary<int, IReadOnlyList<int>>();

    /// <summary>How long a sound's first file lasts, in seconds; 0 for none.</summary>
    private double SecondsOf(SoundChoice? sound)
    {
        if (sound is null || files is null || sound.Files.Count == 0 || files.Sound(sound.Files[0]) is not GameSound unpacked) return 0;
        return unpacked.Frames / (double)unpacked.Rate;
    }

    /// <summary>Unpacks the unit's sounds in the background, so each plays at once the first time.</summary>
    private void PrepareSounds()
    {
        if (sounds is null || depiction is null) return;
        ModelSounds player = sounds;
        UnitDepiction shown = depiction;
        List<SoundChoice?> all = [.. shown.Weapons.Values.Select(weapon => weapon.Look?.Sound), shown.Engine?.Start, shown.Engine?.Running, shown.Engine?.Stop, shown.Turret?.Start, shown.Turret?.Turning, shown.Turret?.Stop];
        _ = Task.Run(() => player.Prepare(all));
    }

    // ---- the turret's turning sound ----

    private PlayingSound? turning;

    /// <summary>
    /// Plays the turret's turning sounds (the depiction's turret-sound operator, for its main turret) while it turns:
    /// the start sound, then the turning sound looped; the stop sound as it stops.
    /// </summary>
    private void TurretSound()
    {
        if (scene is null || depiction?.Turret is not TurretSounds turretSounds) return;
        bool isTurning = scene.Turrets.Any(turret => "tourelle" + turret.Number == turretSounds.TurretTag && Math.Abs(turret.Turn - turret.TurnTo) > 0.01);
        if (isTurning && turning is null)
        {
            sounds?.Play(turretSounds.Start);
            turning = sounds?.Play(turretSounds.Turning, loop: true, fadeIn: 0.1, delay: Math.Max(0, SecondsOf(turretSounds.Start) - 0.15));
            turningWanted = true;
        }
        else if (!isTurning && turningWanted)
        {
            turning?.FadeOut(0.1);
            turning = null;
            turningWanted = false;
            sounds?.Play(turretSounds.Stop);
        }
    }

    private bool turningWanted;

    private void SoundClicked(object sender, RoutedEventArgs e)
    {
        if (sounds is null) return;
        sounds.IsOn = SoundSwitch.IsChecked == true;
        if (!sounds.IsOn)
        {
            sounds.Silence();
            running = null;
            turning = null;
            turningWanted = false;
        }
        else if (engineSwitch?.IsChecked == true) running = sounds.Play(depiction?.Engine?.Running, loop: true, fadeIn: 0.3);
    }

    // ---- the volume ----

    /// <summary>Where the volume is kept for next time, in the app's state.</summary>
    private const string VolumeKey = "modelViewerVolume";

    private bool settingVolume;
    private DispatcherTimer? volumeKeeper;

    /// <summary>
    /// The volume slider moved: the sounds follow at once, and the level is kept for next time a moment after it stops
    /// moving. 100 is the game's own levels; a square curve makes the middle sound about half as loud.
    /// </summary>
    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ShowVolume();
        if (sounds is not null) sounds.Loudness = LoudnessOf(e.NewValue);
        if (settingVolume) return;
        volumeKeeper ??= new DispatcherTimer(TimeSpan.FromSeconds(0.8), DispatcherPriority.Background, (_, _) => KeepVolume(), Dispatcher);
        volumeKeeper.Stop();
        volumeKeeper.Start();
    }

    private void ShowVolume() => VolumeText.Text = Texts.Get("Units.Model.VolumeLevel", ("level", (int)Math.Round(VolumeSlider.Value)));

    private void KeepVolume()
    {
        volumeKeeper?.Stop();
        if ((double?)AppData.Current.State[VolumeKey] == Math.Round(VolumeSlider.Value)) return;
        AppData.Current.State[VolumeKey] = Math.Round(VolumeSlider.Value);
        AppData.Current.SaveState();
    }

    private static float LoudnessOf(double level) => (float)(level / 100 * (level / 100));

    /// <summary>Whether an archive path is a sound the viewer may play: under Assets/Sounds, with or without AllPlatforms/Sound/ in front.</summary>
    private static bool IsSoundFile(string path) =>
        DepictionReader.IsSoundFile(path) || (path.StartsWith("Assets/Sounds/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".ess", StringComparison.OrdinalIgnoreCase));

    /// <summary>A whole number of degrees with its sign: +5°, −10°, 0°.</summary>
    private static string Signed(double degrees)
    {
        long whole = (long)Math.Round(degrees);
        return (whole > 0 ? "+" : whole < 0 ? "−" : "") + Math.Abs(whole) + "°";
    }

    // ---- the marks on the model ----

    /// <summary>A round number in the mark colour, as on the model and in the list.</summary>
    private static Border NumberDisc(int number, double opacity)
    {
        var disc = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x13, 0x0A)),
            Opacity = opacity,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = number.ToString(Texts.Catalog.Culture), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x13, 0x0A)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        disc.SetResourceReference(Border.BackgroundProperty, "caution");
        return disc;
    }

    private void AddMark(int node, bool isFiringPoint, int turret)
    {
        Border disc = NumberDisc(turret, 1);
        disc.RenderTransformOrigin = new Point(0.5, 0.5);
        Marks.Children.Add(disc);
        marks.Add((disc, node, isFiringPoint, turret));
    }

    /// <summary>
    /// Puts each mark where its node is on the picture: shown when the marks are on and of the kind chosen, dimmed when
    /// the model hides it, larger while its turret's row is under the mouse.
    /// </summary>
    private void PlaceMarks(float[]? distances, int distanceWidth, int distanceHeight)
    {
        if (scene is null) return;
        double width = ViewFrame.ActualWidth, height = ViewFrame.ActualHeight;
        int litNumber = litTurret >= 0 && litTurret < scene.Turrets.Count ? scene.Turrets[litTurret].Number : -1;
        foreach ((FrameworkElement mark, int node, bool isFiringPoint, int turret) in marks)
        {
            Vector3 place = scene.PlaceOf(node);
            (double x, double y, double depth) = camera.Project(place);
            bool shown = showMarks && isFiringPoint == showFiringPoints && !double.IsNaN(x);
            mark.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown) continue;
            Canvas.SetLeft(mark, x - 9);
            Canvas.SetTop(mark, y - 9);
            bool hidden = false;
            if (distances is not null)
            {
                int px = (int)(x / width * distanceWidth), py = (int)(y / height * distanceHeight);
                if (px >= 0 && py >= 0 && px < distanceWidth && py < distanceHeight)
                {
                    double modelDistance = distances[py * distanceWidth + px] * camera.Far;
                    hidden = modelDistance < Vector3.Distance(place, camera.Eye) - scene.Radius * 0.06;
                }
                mark.Tag = hidden;
            }
            else hidden = mark.Tag is true;
            bool lit = turret == litNumber;
            mark.Opacity = hidden && !lit ? 0.5 : 1;
            mark.RenderTransform = lit ? new ScaleTransform(1.45, 1.45) : Transform.Identity;
            Panel.SetZIndex(mark, lit ? 2 : 0);
        }
    }

    // ---- drawing ----

    private readonly Stopwatch frameClock = new();
    private bool moving;

    /// <summary>When WPF last said the frame drawn would be shown; zero before the first frame of a move.</summary>
    private TimeSpan lastShown;
    private long lastHiddenCheck;

    /// <summary>Starts drawing a frame each time WPF draws, while something moves.</summary>
    private void StartMoving()
    {
        if (moving) return;
        moving = true;
        frameClock.Restart();
        lastShown = TimeSpan.Zero;
        CompositionTarget.Rendering += DrawNextFrame;
    }

    private void StopMoving()
    {
        if (!moving) return;
        moving = false;
        CompositionTarget.Rendering -= DrawNextFrame;
        Redraw();
    }

    /// <summary>
    /// Moves everything on to when WPF will show the frame, and draws it. WPF can ask more than once for the same frame:
    /// it is drawn once. Things move by the time between the frames as shown, not as drawn, so they move evenly.
    /// </summary>
    private void DrawNextFrame(object? sender, EventArgs e)
    {
        TimeSpan shown = e is RenderingEventArgs rendering ? rendering.RenderingTime : TimeSpan.Zero;
        if (shown != TimeSpan.Zero && shown == lastShown) return;
        double seconds = Math.Min(0.05, lastShown != TimeSpan.Zero && shown > lastShown ? (shown - lastShown).TotalSeconds : frameClock.Elapsed.TotalSeconds);
        lastShown = shown;
        frameClock.Restart();
        bool stillMoving = scene?.Step(seconds) == true || dragging;
        // the aim pad follows the picked turret while it turns, and once more as it stops
        bool aimMoving = scene is not null && pickedTurret < scene.Turrets.Count && scene.Turrets[pickedTurret].IsTurning;
        if (aimMoving || aimWasMoving) ShowAimNow();
        aimWasMoving = aimMoving;
        // the turntable turns the model once round every 20 seconds, while it is not being dragged
        if (turntable && !dragging)
        {
            camera.Yaw += 18 * seconds;
            stillMoving = true;
        }
        stillMoving |= Glide(seconds);
        ShowRotorBlur();
        if (effects is not null)
        {
            effects.Camera = camera.Eye;
            effects.Step(seconds);
            stillMoving |= effects.IsBusy;
        }
        TurretSound();
        Draw(checkHidden: false);
        if (!stillMoving) StopMoving();
    }

    /// <summary>Draws one frame now, and checks which marks the model hides.</summary>
    private void Redraw() => Draw(checkHidden: true);

    private void Draw(bool checkHidden)
    {
        if (renderer is null || scene is null || Visibility != Visibility.Visible) return;
        double width = ViewFrame.ActualWidth, height = ViewFrame.ActualHeight;
        if (width <= 0 || height <= 0) return;
        camera.Aim(scene.Centre + scene.Offset, scene.Radius, width, height);
        Matrix4x4[] pose = scene.Pose();
        var view = new FrameView(camera.ViewProjection, camera.Eye, camera.Far, scene.TrackSlide, scene.Offset);
        FxPlayer? playing = effects;
        EffectRenderer? drawing = effectRenderer;
        renderer.Draw(view, pose, playing is null || drawing is null || playing.Particles.Count == 0 ? null : context => drawing.Draw(context, playing.Particles, camera));
        if (!ReferenceEquals(Picture.Source, renderer.Picture)) Picture.Source = renderer.Picture;
        // which marks the model hides is checked now and then while things move, and always when they stop
        long now = Environment.TickCount64;
        if (marks.Count > 0 && showMarks && (checkHidden || now - lastHiddenCheck > 150))
        {
            lastHiddenCheck = now;
            (float[] distances, int distanceWidth, int distanceHeight) = renderer.Distances(view, pose);
            PlaceMarks(distances, distanceWidth, distanceHeight);
        }
        else PlaceMarks(null, 0, 0);
    }

    /// <summary>What the app held outside .NET as the viewer opened, to tell how much the viewer adds.</summary>
    private long outsideAtOpen;

    /// <summary>What the app holds outside .NET now: Task Manager's Memory column, less what .NET holds.</summary>
    private static long OutsideDotNetNow() => UsageSampler.PrivateWorkingSet() - GC.GetGCMemoryInfo().TotalCommittedBytes;

    /// <summary>
    /// What the viewer holds while it is open, about: in .NET, the game's archive list, the mesh pack's index, the
    /// model and the sounds unpacked (measured on the M1A1, November 2026); outside it, what the app has taken outside .NET since the viewer opened: Direct3D's copies of the model,
    /// its textures and the pictures it draws into (in this program's memory when Windows draws them in software), and
    /// the drawing driver's own. At least the picture WPF shows and those read back through. Nothing when it is closed.
    /// </summary>
    /// <param name="outsideDotNet">What the app holds outside .NET now, as the usage readout read it.</param>
    public (long DotNet, long Outside) MemoryUse(long outsideDotNet)
    {
        const long BytesPerArchiveFile = 324, BytesPerPackedModel = 1200, BytesPerScriptCharacter = 5, BytesPerParticle = 1850;
        long dotNet = 0, outside = 0;
        if (files is OpenFiles open)
        {
            dotNet += open.Archives.Files.Count * BytesPerArchiveFile + open.Packs.Sum(pack => pack.ModelCount) * BytesPerPackedModel + open.Effects.CharactersRead * BytesPerScriptCharacter;
        }
        if (scene?.Mesh is ModelMesh mesh) dotNet += mesh.Parts.Sum(part => part.Vertices.Length * (long)ModelVertex.Size + part.Indexes.Length * 4L) + mesh.Nodes.Count * 120L;
        dotNet += (sounds?.HeldBytes ?? 0) + (effects?.Particles.Count ?? 0) * BytesPerParticle;
        if (renderer is not null)
        {
            long graphics = renderer.GraphicsBytes + (effectRenderer?.GraphicsBytes ?? 0);
            outside += renderer.PictureBytes + (renderer.IsSoftware ? graphics : renderer.ReadBackBytes);
            outside = Math.Max(outside, outsideDotNet - outsideAtOpen);
        }
        return (dotNet, outside);
    }

    /// <summary>
    /// Narrows the view for a model too tall or too long to fit at the start: its bounding box's corners all inside 90%
    /// of the picture, seen as the viewer opens on it, at the view's size now.
    /// </summary>
    private void FitModel()
    {
        if (scene is null || subject is null) return;
        double width = ViewFrame.ActualWidth > 0 ? ViewFrame.ActualWidth : ViewFrame.Width, height = ViewFrame.ActualHeight > 0 ? ViewFrame.ActualHeight : ViewFrame.Height;
        if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0) return;
        var start = new ModelCamera { Yaw = subject.Yaw, Pitch = 18 };
        start.Aim(scene.Centre, scene.Radius, width, height);
        double most = 0;
        for (int corner = 0; corner < 8; corner++)
        {
            var point = new Vector3((corner & 1) != 0 ? scene.Mesh.High.X : scene.Mesh.Low.X, (corner & 2) != 0 ? scene.Mesh.High.Y : scene.Mesh.Low.Y, (corner & 4) != 0 ? scene.Mesh.High.Z : scene.Mesh.Low.Z);
            (double x, double y, _) = start.Project(point);
            if (double.IsNaN(x)) continue;
            most = Math.Max(most, Math.Max(Math.Abs(x - width / 2) / (width / 2), Math.Abs(y - height / 2) / (height / 2)));
        }
        camera.Fit = most > 0 ? Math.Min(1, 0.9 / most) : 1;
    }

    private void ViewResized(object sender, SizeChangedEventArgs e) => ViewResizedNow();

    /// <summary>Makes the picture as many pixels as the view has on this screen, and draws it again.</summary>
    private void ViewResizedNow()
    {
        if (renderer is null) return;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        renderer.Resize((int)Math.Round(ViewFrame.ActualWidth * dpi.DpiScaleX), (int)Math.Round(ViewFrame.ActualHeight * dpi.DpiScaleY));
        Picture.Source = renderer.Picture;
        FitModel();
        Redraw();
    }

    // ---- turning, tilting, moving and zooming ----

    private bool dragging, panning;
    private Point lastPoint;

    /// <summary>
    /// A press on the view gives it the keys and starts a drag: the left button turns and tilts, the right or the
    /// wheel button moves the camera. The turntable pauses while the view is dragged and carries on after.
    /// </summary>
    private void ViewPressed(object sender, MouseButtonEventArgs e)
    {
        if (scene is null || dragging) return;
        bool moves = e.ChangedButton is MouseButton.Right or MouseButton.Middle;
        if (!moves && e.ChangedButton != MouseButton.Left) return;
        ViewFrame.Focus();
        panning = moves;
        dragging = ViewFrame.CaptureMouse();
        lastPoint = e.GetPosition(ViewFrame);
        StopGlide();
        ViewFrame.Cursor = moves ? Cursors.SizeAll : Cursors.Hand;
        e.Handled = true;
        StartMoving();
    }

    /// <summary>
    /// Dragging across turns the model, about half a degree for each pixel, and up and down tilts it; right-dragging
    /// moves the camera so the model follows the mouse.
    /// </summary>
    private void ViewDragged(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        Point point = e.GetPosition(ViewFrame);
        double across = point.X - lastPoint.X, down = point.Y - lastPoint.Y;
        if (panning) MoveCamera(-across, down, camera.UnitsAPixel);
        else
        {
            camera.Yaw += across * 0.555;
            camera.Pitch = Math.Clamp(camera.Pitch + down * 0.2, -10, 85);
        }
        lastPoint = point;
    }

    /// <summary>Moves the camera, and the point it circles, this far right and up on the picture.</summary>
    private void MoveCamera(double right, double up, double unitsEach) =>
        camera.Pan += camera.Right * (float)(right * unitsEach) + camera.Up * (float)(up * unitsEach);

    private void ViewReleased(object sender, MouseButtonEventArgs e)
    {
        bool ends = panning ? e.ChangedButton is MouseButton.Right or MouseButton.Middle : e.ChangedButton == MouseButton.Left;
        if (dragging && ends) ViewFrame.ReleaseMouseCapture();
        if (ends) e.Handled = true;
    }

    private void ViewCaptureLost(object sender, MouseEventArgs e)
    {
        dragging = panning = false;
        ViewFrame.Cursor = Cursors.Hand;
    }

    /// <summary>Scrolling zooms: in to 6 times, out to a third (the camera moving back), to see a smoke screen land.</summary>
    private void ViewScrolled(object sender, MouseWheelEventArgs e)
    {
        Zoom(e.Delta > 0 ? 1.12 : 1 / 1.12);
        e.Handled = true;
    }

    private void Zoom(double by)
    {
        camera.Zoom = Math.Clamp(camera.Zoom * by, ModelCamera.LeastZoom, ModelCamera.MostZoom);
        Redraw();
    }

    private void ResetClicked(object sender, RoutedEventArgs e) => ResetView();

    /// <summary>The camera back where it starts: its angle, no zoom, not moved.</summary>
    private void ResetView()
    {
        if (subject is null) return;
        StopGlide();
        camera.Yaw = subject.Yaw;
        camera.Pitch = 18;
        camera.Zoom = 1;
        camera.Pan = Vector3.Zero;
        Redraw();
    }

    // ---- the straight views ----

    private double? glideYaw, glidePitch;
    private Vector3? glidePan;

    /// <summary>Front, Side, Top or Rear: the camera swings round to look straight at the model from there.</summary>
    private void LookClicked(object sender, RoutedEventArgs e)
    {
        if (scene is null || sender is not FrameworkElement { Tag: string side }) return;
        double ahead = AheadYaw();
        (glideYaw, glidePitch) = side switch
        {
            "front" => (ahead, 4.0),
            "side" => (ahead + 270, 4.0),
            "top" => (ahead + 180, 89.0),
            _ => (ahead + 180, 4.0),
        };
        glidePan = Vector3.Zero;
        StartMoving();
    }

    /// <summary>
    /// The camera's turn that looks at the model's front. Vehicles drive along x; an aircraft faces the way its first
    /// firing point is, as some are built facing back along x.
    /// </summary>
    private double AheadYaw()
    {
        if (scene is null || subject?.Flies != true) return 0;
        int muzzle = scene.Turrets.SelectMany(turret => turret.FiringPoints).DefaultIfEmpty(-1).First();
        return muzzle >= 0 && scene.PlaceOf(muzzle).X < scene.Centre.X ? 180 : 0;
    }

    /// <summary>Moves the camera part of the way to where a view button sends it; whether it is still on its way.</summary>
    private bool Glide(double seconds)
    {
        if (glideYaw is null && glidePitch is null && glidePan is null) return false;
        double share = 1 - Math.Exp(-seconds * 9);
        if (glideYaw is double yaw)
        {
            double left = ((yaw - camera.Yaw) % 360 + 540) % 360 - 180;
            camera.Yaw += Math.Abs(left) > 0.05 ? left * share : left;
            if (Math.Abs(left) <= 0.05) glideYaw = null;
        }
        if (glidePitch is double pitch)
        {
            double left = pitch - camera.Pitch;
            camera.Pitch += Math.Abs(left) > 0.05 ? left * share : left;
            if (Math.Abs(left) <= 0.05) glidePitch = null;
        }
        if (glidePan is Vector3 pan)
        {
            Vector3 left = pan - camera.Pan;
            camera.Pan += left.Length() > 0.5f ? left * (float)share : left;
            if (left.Length() <= 0.5f) glidePan = null;
        }
        return true;
    }

    private void StopGlide()
    {
        glideYaw = glidePitch = null;
        glidePan = null;
    }

    // ---- copying the picture ----

    private DispatcherTimer? saidTimer;

    /// <summary>Copies the view as drawn, without the turret numbers over it, for pasting into another program.</summary>
    private void CopyClicked(object sender, RoutedEventArgs e)
    {
        if (renderer is null) return;
        Redraw();
        if (renderer.Snapshot() is not BitmapSource copy) return;
        try
        {
            Clipboard.SetImage(copy);
            Say(Texts.Get("Units.Model.Copied"));
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // another program is holding the clipboard
            Say(Texts.Get("Units.Model.CopyFailed"));
        }
    }

    /// <summary>A few words beside Copy Picture, gone after three seconds.</summary>
    private void Say(string text)
    {
        CopiedText.Text = text;
        saidTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) =>
        {
            CopiedText.Text = "";
            saidTimer?.Stop();
        }, Dispatcher);
        saidTimer.Stop();
        saidTimer.Start();
    }

    // ---- a squad's soldiers ----

    /// <summary>"‹ Soldier 2 of 6 ›" for a squad with more than one soldier's model.</summary>
    private void ShowSoldierChoice()
    {
        bool squad = modelFiles.Count > 1;
        SoldierChoice.Visibility = squad ? Visibility.Visible : Visibility.Collapsed;
        if (squad) SoldierText.Text = Texts.Get("Units.Model.Soldier", ("number", shownModel + 1), ("count", modelFiles.Count));
    }

    private void PreviousSoldierClicked(object sender, RoutedEventArgs e) => ShowSoldier(-1);

    private void NextSoldierClicked(object sender, RoutedEventArgs e) => ShowSoldier(1);

    /// <summary>The next or previous soldier's model, round again.</summary>
    private void ShowSoldier(int step)
    {
        if (modelFiles.Count < 2) return;
        _ = ShowModel((shownModel + step + modelFiles.Count) % modelFiles.Count, opening);
    }

    // ---- fitting the window, closing ----

    private void BackdropResized(object sender, SizeChangedEventArgs e) => FitToWindow();

    private bool filling;

    /// <summary>Fill the Window, or back to the viewer's own size.</summary>
    private void FillClicked(object sender, RoutedEventArgs e)
    {
        filling = !filling;
        ShowFilling();
    }

    private void ShowFilling()
    {
        FillIcon.Visibility = filling ? Visibility.Collapsed : Visibility.Visible;
        BackIcon.Visibility = filling ? Visibility.Visible : Visibility.Collapsed;
        string tip = Texts.Get(filling ? "Units.Model.Unfill.Tip" : "Units.Model.Fill.Tip");
        FillButton.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(FillButton, tip);
        if (filling) CacheWhatIsUnder();
        else UncacheWhatIsUnder();
        FitToWindow();
    }

    /// <summary>
    /// The view at 880 by 540, or smaller in the same shape when the window is: the whole viewer within 96% of it. When
    /// it fills the window, the viewer reaches within 8 pixels of the window's edges and the view takes all the room
    /// the rest leaves.
    /// </summary>
    private void FitToWindow()
    {
        Box.Margin = new Thickness(filling ? 8 : 0);
        Box.HorizontalAlignment = filling ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        Box.VerticalAlignment = filling ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        ViewColumn.Width = filling ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        if (filling)
        {
            ViewFrame.Width = ViewFrame.Height = double.NaN;
            return;
        }
        double sideRoom = SideScroller.Visibility == Visibility.Visible ? sideWidth + 12 : 0;
        double widthLeft = Backdrop.ActualWidth * 0.96 - sideRoom - 30, heightLeft = Backdrop.ActualHeight * 0.96 - 120;
        if (widthLeft <= 0 || heightLeft <= 0) return;
        double scale = Math.Min(1, Math.Min(widthLeft / ViewWidth, heightLeft / 540));
        ViewFrame.Width = Math.Floor(ViewWidth * scale);
        ViewFrame.Height = Math.Floor(540 * scale);
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Closes the viewer, letting go of the model, the graphics card's copies of it and the game's files,
    /// then giving the memory they took back to Windows.
    /// </summary>
    public void Close()
    {
        if (Visibility != Visibility.Visible) return;
        opening++;
        StopMoving();
        dragging = panning = false;
        turntable = false;
        StopGlide();
        if (volumeKeeper?.IsEnabled == true) KeepVolume();
        if (filling)
        {
            filling = false;
            ShowFilling();
        }
        Visibility = Visibility.Collapsed;
        Picture.Source = null;
        Marks.Children.Clear();
        marks.Clear();
        SidePanel.Children.Clear();
        turretRows.Clear();
        aimBox = null;
        scene = null;
        subject = null;
        CloseFiles();
        renderer?.Dispose();
        renderer = null;
        Memory.GiveBack();
    }

    /// <summary>What is under the viewer while it fills the window (the app, and any window open under the viewer), with how each was cached before.</summary>
    private readonly List<(UIElement Element, CacheMode? Cache)> cachedUnder = [];

    /// <summary>
    /// Keeps what is under the viewer as pictures while it fills the window. Tuned for speed: WPF draws everything under
    /// a picture that changes, hidden or not, so each frame of the model redrew the whole app under it (3 to 4.5 ms a
    /// frame on the VM, at 2560 by 1440); it now draws one picture of it (under 1 ms). Only while filling: a picture draws
    /// some of the app's text without ClearType, which would show around the viewer at its own size.
    /// </summary>
    private void CacheWhatIsUnder()
    {
        if (cachedUnder.Count > 0 || Parent is not Panel parent) return;
        foreach (UIElement under in parent.Children)
        {
            if (ReferenceEquals(under, this)) break;
            if (under.Visibility != Visibility.Visible) continue;
            cachedUnder.Add((under, under.CacheMode));
            // at the screen's scale, as WPF draws it there: its own is 96 dots an inch
            under.CacheMode = new BitmapCache { SnapsToDevicePixels = true, RenderAtScale = VisualTreeHelper.GetDpi(this).DpiScaleX };
        }
    }

    /// <summary>Draws what is under the viewer as before it filled the window.</summary>
    private void UncacheWhatIsUnder()
    {
        foreach ((UIElement element, CacheMode? cache) in cachedUnder) element.CacheMode = cache;
        cachedUnder.Clear();
    }

    /// <summary>Caches what is under the viewer again at the new screen's scale, when the window moves to it.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (cachedUnder.Count == 0) return;
        UncacheWhatIsUnder();
        CacheWhatIsUnder();
    }

    /// <summary>Stops the sounds and lets go of the game's files and what was read from them.</summary>
    private void CloseFiles()
    {
        sounds?.Silence();
        sounds?.Dispose();
        sounds = null;
        running = turning = null;
        turningWanted = false;
        firing.Clear();
        fireButton = null;
        engineSwitch = null;
        depiction = null;
        movementEffects.Clear();
        rotorBlurs.Clear();
        effects?.Clear();
        effects = null;
        effectRenderer?.Dispose();
        effectRenderer = null;
        files?.Dispose();
        files = null;
        modelFiles = [];
    }

    /// <summary>
    /// Escape closes the viewer. Arrows turn and tilt the model, Shift and arrows move the camera, + and - zoom, F fires
    /// the picked turret, Home resets the view; the aim pad keeps its own arrows and Home, and a button its Space and Enter.
    /// </summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        if (scene is null || e.Handled || e.OriginalSource is System.Windows.Controls.Primitives.RangeBase or TextBox) return;
        if (e.OriginalSource is ButtonBase && e.Key is Key.Space or Key.Enter) return;
        int across = e.Key switch { Key.Left => -1, Key.Right => 1, _ => 0 }, up = e.Key switch { Key.Up => 1, Key.Down => -1, _ => 0 };
        if ((across != 0 || up != 0) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) MoveCamera(-across, -up, camera.Distance * 0.04);
        else if (across != 0) camera.Yaw += across * 5;
        else if (up != 0) camera.Pitch = Math.Clamp(camera.Pitch - up * 4, -10, 85);
        else if (e.Key is Key.OemPlus or Key.Add) Zoom(1.12);
        else if (e.Key is Key.OemMinus or Key.Subtract) Zoom(1 / 1.12);
        else if (e.Key == Key.Home) ResetView();
        else if (e.Key == Key.F && scene.Turrets.Count > 0) Fire(scene.Turrets[pickedTurret]);
        else return;
        e.Handled = true;
        StopGlide();
        Redraw();
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
