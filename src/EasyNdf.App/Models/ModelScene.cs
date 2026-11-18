using System.Numerics;
using EasyNdf.Data.Effects;
using EasyNdf.Data.Models;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Models;

/// <summary>One of the unit's turrets as the viewer is given it: from its loadout, with its weapons' names and how its first weapon fires.</summary>
/// <param name="Turret">The loadout's turret.</param>
/// <param name="Weapons">Its weapons' names, as the Armory shows them.</param>
/// <param name="Salvo">How its first weapon fires a salvo.</param>
/// <param name="Fires">Whether its first weapon is a gun the viewer fires: not a missile or rocket.</param>
internal sealed record TurretGiven(LoadoutTurret Turret, string Weapons, SalvoPattern Salvo, bool Fires);

/// <summary>How a vehicle drives, in the model's units: its top speed, and how fast it speeds up and slows down (each a second).</summary>
internal sealed record DriveMotion(double Speed, double SpeedingUp, double SlowingDown)
{
    /// <summary>15 km/h at the model's own scale, for a model whose unit gives no speed.</summary>
    public static readonly DriveMotion Slow = new(15 / 3.6 * 215, 450, 650);
}

/// <summary>
/// A model as the viewer moves it: its turrets, wheels and tracks, rotors and gatling barrels, and where each node has
/// moved to. A node's move is worked out the way the game turns its parts: undo where the node sits, turn about its own
/// axes, put it back, then follow its parent.
/// </summary>
internal sealed class ModelScene
{
    private readonly ModelMesh mesh;
    private readonly DriveMotion drive;
    private readonly HashSet<int> nodesWithVertices = [];
    private readonly Matrix4x4[] pose;
    private readonly Matrix4x4[] inverses;
    private readonly List<(int Node, float Radius)> wheels = [];
    private readonly List<Rotor> rotors = [];
    private readonly List<(int Hub, FxAttachment Blur)> blurs = [];
    private readonly List<(double At, Action Run)> timers = [];
    private readonly double gatlingSpeed, gatlingAcceleration;
    private double clock, rotorSecondsToChange = 5, gatlingTurning, gatlingAngle, gatlingSpinsUntil;

    /// <summary>Gets a model ready to move.</summary>
    /// <param name="mesh">The model.</param>
    /// <param name="turrets">The unit's turrets; none for a model with no loadout.</param>
    /// <param name="flies">Whether the unit flies: an aircraft's turret angles are the arcs its weapons fire in, not parts that move.</param>
    /// <param name="depiction">How the game shows the unit; null when it was not found.</param>
    /// <param name="drive">How the vehicle drives; null for <see cref="DriveMotion.Slow"/>.</param>
    /// <param name="launchers">Where the weapons hung on the model fire from, by weapon number; null for none.</param>
    public ModelScene(ModelMesh mesh, IReadOnlyList<TurretGiven> turrets, bool flies, UnitDepiction? depiction, DriveMotion? drive = null,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? launchers = null)
    {
        this.mesh = mesh;
        this.launchers = launchers ?? new Dictionary<int, IReadOnlyList<int>>();
        this.drive = drive ?? DriveMotion.Slow;
        pose = new Matrix4x4[Math.Max(ModelRenderer.MostNodes, mesh.Nodes.Count)];
        foreach (ModelPart part in mesh.Parts)
            foreach (ModelVertex vertex in part.Vertices)
            {
                nodesWithVertices.Add(part.NodeBase + vertex.Node);
                // a soldier's skin follows up to three more bones
                for (int more = 0; more < 3; more++)
                    if ((vertex.Weights >> (8 * (more + 1)) & 0xFF) != 0) nodesWithVertices.Add(part.NodeBase + (int)(vertex.MoreNodes >> (8 * more) & 0xFF));
            }
        inverses = [.. mesh.Nodes.Select(node => Matrix4x4.Invert(node.World, out Matrix4x4 back) ? back : Matrix4x4.Identity)];
        Array.Fill(pose, Matrix4x4.Identity);
        Turrets = [.. turrets.Select(each => TurretOf(each, flies, depiction)).OfType<ViewTurret>().OrderBy(turret => turret.Number)];
        Centre = (mesh.Low + mesh.High) / 2;
        Radius = (mesh.High - mesh.Low).Length() / 2;
        FindWheels();
        TrackUnitsAUnitOfTexture = TrackLength();
        foreach (UnitRotor rotor in depiction?.Rotors ?? [])
        {
            if (mesh.NodeNamed(rotor.Node) is not (int node and >= 0)) continue;
            int bladesAt = rotor.Blades is string bladesNode ? mesh.NodeNamed(bladesNode) : -1;
            int blurAt = -1;
            // the blades are hidden while the blur drawn in their place shows
            if (rotor.Blur is FxAttachment blur && mesh.NodeNamed(blur.Anchor) is int hub and >= 0)
            {
                blurAt = blurs.Count;
                blurs.Add((hub, blur));
            }
            rotors.Add(new Rotor(node, rotor.Axis, rotor.Speed, rotor.Clockwise ? -1 : 1, rotor.BladeCount, bladesAt, blurAt));
        }
        if (depiction?.Gatling is GatlingBarrels gatling && mesh.NodeNamed(gatling.Node) is int barrels and >= 0)
        {
            GatlingNode = barrels;
            gatlingSpeed = gatling.Speed;
            gatlingAcceleration = gatling.Acceleration;
        }
    }

    /// <summary>The turrets the model shows, in number order: those of the loadout whose nodes the model has.</summary>
    public IReadOnlyList<ViewTurret> Turrets { get; }

    /// <summary>The middle of the model's bounding box.</summary>
    public Vector3 Centre { get; }

    /// <summary>Half the length of the bounding box's diagonal: the camera stands three times that away.</summary>
    public float Radius { get; }

    /// <summary>The model.</summary>
    public ModelMesh Mesh => mesh;

    // ---- turrets ----

    private readonly IReadOnlyDictionary<int, IReadOnlyList<int>> launchers;

    /// <summary>
    /// A loadout turret as shown, or null when the model has no node of its number. Its tag's number picks the nodes:
    /// tourelle_0N, axe_canon_0N, and the firing points fx_tourelleN_tir_...; a weapon hung on pylons, which has none,
    /// fires from its munitions or its pods' tubes. It is offered to aim only when parts of the model follow those nodes,
    /// and its limits allow more than a sliver of movement. Its first weapon fires with the depiction's weapon of the
    /// same number (weapon_effet_tagN), and its gun recoils as the depiction's recoil says.
    /// </summary>
    private ViewTurret? TurretOf(TurretGiven given, bool flies, UnitDepiction? depiction)
    {
        LoadoutTurret turret = given.Turret;
        string digits = new([.. turret.Tag.Where(char.IsAsciiDigit)]);
        if (!int.TryParse(digits, out int number)) return null;
        string padded = number.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        int centre = mesh.NodeNamed("tourelle_" + padded);
        List<int> firingPoints = [.. Enumerable.Range(0, mesh.Nodes.Count).Where(n => mesh.Nodes[n].Name.StartsWith($"fx_tourelle{number}_tir_", StringComparison.Ordinal))];
        if (centre < 0 && firingPoints.Count == 0 && turret.Mounts.Count > 0 && launchers.TryGetValue(turret.Mounts[0].WeaponNumber, out IReadOnlyList<int>? hung))
            firingPoints = [.. hung];
        if (centre < 0 && firingPoints.Count == 0) return null;
        TurretMotion? motion = turret.Motion;
        double turnLimit = Math.Min(180, Degrees(motion?.TurnLimit) ?? 180);
        double lowest = Degrees(motion?.LowestPitch) ?? 0, highest = Degrees(motion?.HighestPitch) ?? 0;
        int raiseNode = mesh.NodeNamed("axe_canon_" + padded);
        bool moves = !flies && turret.Type != "TTurretUnitDescriptor";
        DepictedWeapon? weapon = null;
        if (turret.Mounts.Count > 0 && depiction is not null) depiction.Weapons.TryGetValue(turret.Mounts[0].WeaponNumber, out weapon);
        List<int> muzzles = [.. (weapon?.Anchors ?? []).Select(mesh.NodeNamed).Where(node => node >= 0)];
        GunRecoil? recoil = depiction?.Recoils.GetValueOrDefault(number);
        int recoilNode = recoil is null ? -1 : mesh.NodeNamed(recoil.Node);
        return new ViewTurret
        {
            Number = number,
            Weapons = given.Weapons,
            FiringPoints = firingPoints,
            Centre = centre,
            // a few degrees of turn (a coaxial gun that follows the main gun) is not worth a slider
            TurnNode = moves && centre >= 0 && turnLimit >= 5 && HasPartsUnder(centre) ? centre : -1,
            RaiseNode = moves && raiseNode >= 0 && highest - lowest >= 2 && HasPartsUnder(raiseNode) ? raiseNode : -1,
            TurnLimit = turnLimit,
            LowestRaise = lowest,
            HighestRaise = highest,
            DegreesASecond = Degrees(motion?.Speed) ?? 60,
            Fires = given.Fires,
            Weapon = weapon,
            Salvo = given.Salvo,
            Muzzles = muzzles.Count > 0 ? muzzles : firingPoints,
            RecoilNode = recoilNode,
            RecoilBack = recoilNode >= 0 && recoil is not null ? RecoilDirection(recoilNode, firingPoints) * (float)recoil.Distance : Vector3.Zero,
            RecoilSpeed = recoil?.Speed ?? 4,
            RecoilPeak = recoil?.Peak ?? 0.5,
        };
    }

    /// <summary>
    /// Which way a gun recoils, in its recoiling node's own space: back from its muzzle (the turret's first firing point)
    /// towards the node.
    /// </summary>
    private Vector3 RecoilDirection(int node, IReadOnlyList<int> firingPoints)
    {
        if (firingPoints.Count == 0) return -Vector3.UnitX;
        Vector3 muzzle = Vector3.Transform(mesh.Nodes[firingPoints[0]].Place, inverses[node]);
        return muzzle.LengthSquared() > 0.001f ? -Vector3.Normalize(muzzle) : -Vector3.UnitX;
    }

    private static double? Degrees(double? radians) => radians is double value ? Math.Round(value * 180 / Math.PI, 1) : null;

    /// <summary>Whether any vertex follows this node or a node under it, so turning it moves something.</summary>
    private bool HasPartsUnder(int node) => nodesWithVertices.Any(vertexNode => IsUnder(vertexNode, node));

    private bool IsUnder(int node, int ancestor)
    {
        for (int at = node; at >= 0 && at < mesh.Nodes.Count; at = mesh.Nodes[at].Parent)
            if (at == ancestor) return true;
        return false;
    }

    // ---- wheels and tracks ----

    /// <summary>
    /// The wheels that roll: nodes named roue_ but not roue_elev_ (a wheel's suspension), with parts on them; each one's
    /// size is the farthest its vertices reach from its axle, which runs along its own y axis.
    /// </summary>
    private void FindWheels()
    {
        var reach = new Dictionary<int, float>();
        foreach (ModelPart part in mesh.Parts)
            foreach (ModelVertex vertex in part.Vertices)
            {
                int node = part.NodeBase + vertex.Node;
                if (node >= mesh.Nodes.Count) continue;
                string name = mesh.Nodes[node].Name;
                if (!name.StartsWith("roue_", StringComparison.Ordinal) || name.StartsWith("roue_elev", StringComparison.Ordinal)) continue;
                Vector3 local = Vector3.Transform(vertex.Position, inverses[node]);
                reach[node] = Math.Max(reach.GetValueOrDefault(node), MathF.Sqrt(local.X * local.X + local.Z * local.Z));
            }
        foreach ((int node, float radius) in reach)
            if (radius > 1) wheels.Add((node, radius));
    }

    /// <summary>
    /// How far a track strip's texture runs for each repeat of it, in the model's units: the middle of the strip's
    /// triangles' values, each from how fast the texture's height changes across the triangle. 0 for no tracks.
    /// </summary>
    private float TrackLength()
    {
        var lengths = new List<float>();
        foreach (ModelPart part in mesh.Parts.Where(part => part.IsTrackStrip))
            for (int t = 0; t + 2 < part.Indexes.Length; t += 3)
            {
                ModelVertex a = part.Vertices[part.Indexes[t]], b = part.Vertices[part.Indexes[t + 1]], c = part.Vertices[part.Indexes[t + 2]];
                Vector3 edge1 = b.Position - a.Position, edge2 = c.Position - a.Position;
                float change1 = b.TexturePlace.Y - a.TexturePlace.Y, change2 = c.TexturePlace.Y - a.TexturePlace.Y;
                float aa = Vector3.Dot(edge1, edge1), ab = Vector3.Dot(edge1, edge2), bb = Vector3.Dot(edge2, edge2), determinant = aa * bb - ab * ab;
                if (determinant < 1e-6f) continue;
                float x = (change1 * bb - change2 * ab) / determinant, y = (change2 * aa - change1 * ab) / determinant;
                float gradient = (edge1 * x + edge2 * y).Length();
                if (gradient > 1e-7f) lengths.Add(1 / gradient);
            }
        if (lengths.Count == 0) return 0;
        lengths.Sort();
        return lengths[lengths.Count / 2];
    }

    /// <summary>Whether the model has wheels that roll, or tracks.</summary>
    public bool CanDrive => wheels.Count > 0 || TrackUnitsAUnitOfTexture > 0;

    /// <summary>How far the tracks' texture repeats, in the model's units; 0 for no tracks.</summary>
    public float TrackUnitsAUnitOfTexture { get; }

    /// <summary>How fast the vehicle is driving, in the model's units a second.</summary>
    public double Speed { get; private set; }

    /// <summary>How fast it is getting to.</summary>
    public double SpeedTo { get; private set; }

    /// <summary>How far it has driven, in the model's units: the wheels have rolled this far, and the tracks slid.</summary>
    public double Driven { get; private set; }

    /// <summary>How far the tracks' texture has slid: back as the vehicle drives forward.</summary>
    public float TrackSlide => TrackUnitsAUnitOfTexture > 0 ? (float)(-Driven / TrackUnitsAUnitOfTexture) : 0;

    /// <summary>Starts or stops driving, at the vehicle's top speed.</summary>
    public void Drive(bool on) => SpeedTo = on ? drive.Speed : 0;

    /// <summary>
    /// How far the vehicle has driven forward, as a move of the whole model: the camera follows it, so it stays in view,
    /// while the dust and smoke it leaves stay where they were made and fall behind.
    /// </summary>
    public Vector3 Offset => new((float)Driven, 0, 0);

    // ---- rotors and gatling barrels ----

    /// <summary>Whether the model has rotors that turn.</summary>
    public bool HasRotors => rotors.Count > 0;

    /// <summary>How fast the rotors turn, from 0 (still) to 1 (their RotationSpeed).</summary>
    public double RotorLevel { get; private set; }

    /// <summary>The level the rotors are getting to.</summary>
    public double RotorLevelTo { get; private set; }

    /// <summary>The blur each rotor shows in place of its blades when they turn fast: the node it is drawn at, and the action that draws it.</summary>
    public IReadOnlyList<(int Hub, FxAttachment Blur)> RotorBlurs => blurs;

    /// <summary>
    /// Whether a rotor's blur (by its place in <see cref="RotorBlurs"/>) shows in place of its blades: once the rotor
    /// turns more than a quarter turn divided by its blade count from one frame to the next.
    /// </summary>
    public bool IsBlurred(int blur) => ShowsBlur && rotors.Any(rotor => rotor.Blur == blur && rotor.IsBlurred);

    /// <summary>Whether a fast rotor's blur shows in place of its blades; off, the blades always show (Hide Effects).</summary>
    public bool ShowsBlur { get; set; } = true;

    /// <summary>A rotor: its node and axis, its full speed (turns a minute) and way round, its blades, and where it has got to.</summary>
    private sealed class Rotor(int node, int axis, double speed, double direction, int bladeCount, int blades, int blur)
    {
        public int Node { get; } = node;
        public int Axis { get; } = axis;
        public double Speed { get; } = speed;
        public double Direction { get; } = direction;
        public int BladeCount { get; } = bladeCount;
        public int Blades { get; } = blades;
        public int Blur { get; } = blur;
        public double Angle { get; set; }
        public bool IsBlurred { get; set; }

        /// <summary>
        /// Turns on for a frame, at a share of its full speed: its speed (turns a minute) times the share cubed, a 24th of
        /// it for each millisecond, so it creeps round as it starts and is soon too fast to follow; its blades blur once a
        /// frame's turn times its blade count is more than a quarter turn.
        /// </summary>
        public void Step(double level, double seconds)
        {
            double step = Speed / 60 * 2 * Math.PI * level * level * level * seconds * 1000 / 24;
            Angle = (Angle + step) % (2 * Math.PI);
            IsBlurred = Blur >= 0 && seconds > 0 && step * BladeCount > Math.PI / 2;
        }
    }

    /// <summary>Starts the rotors, reaching full speed in this many seconds (the engine's start sound), or stops them.</summary>
    public void TurnRotors(bool on, double seconds)
    {
        RotorLevelTo = on ? 1 : 0;
        rotorSecondsToChange = Math.Max(0.5, seconds);
    }

    /// <summary>The gatling's barrels' node; -1 for none.</summary>
    public int GatlingNode { get; } = -1;

    /// <summary>Spins the gatling's barrels for this many seconds, as it fires.</summary>
    public void SpinGatling(double seconds) => gatlingSpinsUntil = Math.Max(gatlingSpinsUntil, clock + seconds);

    // ---- time ----

    /// <summary>Runs something this many seconds from now, as the scene's time goes (a shot of a salvo, a vehicle setting off once its engine runs).</summary>
    public void Later(double seconds, Action run) => timers.Add((clock + seconds, run));

    /// <summary>Moves what is moving for this many seconds, and runs what is due; whether anything still moves or is due.</summary>
    public bool Step(double seconds)
    {
        clock += seconds;
        for (int i = 0; i < timers.Count; i++)
        {
            if (timers[i].At > clock) continue;
            Action run = timers[i].Run;
            timers.RemoveAt(i--);
            run();
        }
        bool moving = timers.Count > 0;
        foreach (ViewTurret turret in Turrets)
        {
            turret.Step(seconds);
            moving |= turret.IsTurning || turret.RecoilTime >= 0;
        }
        Speed += Math.Clamp(SpeedTo - Speed, -drive.SlowingDown * seconds, drive.SpeedingUp * seconds);
        Driven += Speed * seconds;
        // the rotors spin up over the engine's start, and wind down more slowly
        RotorLevel += Math.Clamp(RotorLevelTo - RotorLevel, -seconds / Math.Max(8, rotorSecondsToChange), seconds / rotorSecondsToChange);
        foreach (Rotor rotor in rotors) rotor.Step(RotorLevel, seconds);
        // gatling barrels reach their speed in about a second (RotationAcceleration 1 as the share of it gained each second)
        double gatlingTo = clock < gatlingSpinsUntil ? gatlingSpeed : 0, change = gatlingSpeed * Math.Max(0.2, gatlingAcceleration) * seconds;
        gatlingTurning += Math.Clamp(gatlingTo - gatlingTurning, -change, change);
        gatlingAngle += gatlingTurning * seconds;
        return moving || Speed != 0 || SpeedTo != 0 || RotorLevel != 0 || RotorLevelTo != 0 || gatlingTurning != 0;
    }

    // ---- the pose ----

    /// <summary>
    /// Where each node has moved to, as the shaders take it: a matrix for each node, which turns a point that follows the
    /// node from where the model has it to where it is now.
    /// </summary>
    public Matrix4x4[] Pose()
    {
        var turns = new Dictionary<int, Matrix4x4>();
        void Add(int node, Matrix4x4 turn)
        {
            if (node < 0) return;
            turns[node] = turns.TryGetValue(node, out Matrix4x4 before) ? before * turn : turn;
        }
        foreach (ViewTurret turret in Turrets)
        {
            // a turret turns about its own up axis; its gun rises when turned back about its own side axis, and recoils back along its barrel
            Add(turret.TurnNode, Matrix4x4.CreateRotationZ((float)(turret.Turn * Math.PI / 180)));
            Add(turret.RaiseNode, Matrix4x4.CreateRotationY((float)(-turret.Raise * Math.PI / 180)));
            if (turret.RecoilNode >= 0 && turret.RecoilAmount > 0) Add(turret.RecoilNode, Matrix4x4.CreateTranslation(turret.RecoilBack * (float)turret.RecoilAmount));
        }
        // a wheel rolls forward about its own y axis, by the distance driven over its size
        foreach ((int node, float radius) in wheels) Add(node, Matrix4x4.CreateRotationY((float)(Driven / radius)));
        foreach (Rotor rotor in rotors)
        {
            float angle = (float)(rotor.Angle * rotor.Direction);
            Add(rotor.Node, rotor.Axis == 0 ? Matrix4x4.CreateRotationX(angle) : rotor.Axis == 1 ? Matrix4x4.CreateRotationY(angle) : Matrix4x4.CreateRotationZ(angle));
        }
        if (GatlingNode >= 0) Add(GatlingNode, Matrix4x4.CreateRotationX((float)gatlingAngle));
        // blades shrunk to nothing where they hang, while their blur shows
        foreach (Rotor rotor in rotors)
            if (ShowsBlur && rotor.IsBlurred && rotor.Blades >= 0) Add(rotor.Blades, Matrix4x4.CreateScale(0));

        var moved = new Matrix4x4?[mesh.Nodes.Count];
        Matrix4x4 Moved(int node)
        {
            if (moved[node] is Matrix4x4 known) return known;
            int parent = mesh.Nodes[node].Parent;
            Matrix4x4 result = parent >= 0 && parent < mesh.Nodes.Count && parent != node ? Moved(parent) : Matrix4x4.Identity;
            if (turns.TryGetValue(node, out Matrix4x4 turn)) result = inverses[node] * turn * mesh.Nodes[node].World * result;
            moved[node] = result;
            return result;
        }
        Matrix4x4 driven = Matrix4x4.CreateTranslation(Offset);
        for (int node = 0; node < pose.Length; node++) pose[node] = (node < mesh.Nodes.Count ? Moved(node) : Matrix4x4.Identity) * driven;
        return pose;
    }

    /// <summary>A node's place and axes now, after <see cref="Pose"/>: where the effects it plays are drawn.</summary>
    public Matrix4x4 NodeMatrix(int node) => node < pose.Length ? mesh.Nodes[node].World * pose[node] : mesh.Nodes[node].World;

    /// <summary>Where a node is now, after <see cref="Pose"/>.</summary>
    public Vector3 PlaceOf(int node) =>
        node < pose.Length ? Vector3.Transform(mesh.Nodes[node].Place, pose[node]) : mesh.Nodes[node].Place;
}
