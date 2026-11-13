using System.Numerics;
using EasyNdf.Data.Models;

namespace EasyNdf.App.Models;

/// <summary>One munition hung on the model, on a node of its own so it can leave when fired.</summary>
/// <param name="WeaponIndex">The weapon that fires it (its number in the unit).</param>
/// <param name="Node">Its node in the assembled model.</param>
/// <param name="Model">Its model's path, which flies when it is fired.</param>
internal sealed record HungMunition(int WeaponIndex, int Node, string Model);

/// <summary>
/// A unit's model with what its depiction hangs on it, made into one model that is drawn and moved as one: its
/// launchers' turrets (sub-models, keeping their own nodes, so they turn and fire as the unit's turrets do), the pylons
/// and pods at its mounts (fixed to the mount's node), and the missiles and bombs on them, each on a node of its own.
/// </summary>
internal static class ModelAssembly
{
    /// <summary>The model assembled, each part with its pictures, and its munitions in the order they are fired.</summary>
    public sealed record Assembled(ModelMesh Mesh, List<MaterialPictures> Pictures, List<HungMunition> Munitions)
    {
        /// <summary>
        /// Where each hung weapon's shots leave from, by the weapon's number, in the order they are fired: its munitions'
        /// nodes, or a pod's tubes (its missile_1, missile_2... places, which hold no model).
        /// </summary>
        public IReadOnlyDictionary<int, IReadOnlyList<int>> Launchers { get; init; } = new Dictionary<int, IReadOnlyList<int>>();
    }

    /// <summary>
    /// Hangs the depiction's sub-models and munitions on a model. A sub-model hangs at its anchor; a weapon's munitions
    /// at the mounts its kind names (as_1_1, as_1_2... or aa_1_1...), in a sub-model's when the weapon is a launcher's: the
    /// pylon (or pod) of the pylon set that holds as many as each mount carries, and on its places (missile_1,
    /// missile_2...) the munitions, until all are hung. Something whose model cannot be read, or that would take the
    /// model past the nodes it can move, is left off.
    /// </summary>
    /// <param name="main">The unit's model.</param>
    /// <param name="mainPictures">Its parts' pictures.</param>
    /// <param name="depiction">What its depiction hangs; null for nothing.</param>
    /// <param name="meshPath">A model descriptor's path.</param>
    /// <param name="readModel">Reads a model by its path; null when the packs do not have it.</param>
    /// <param name="picturesOf">A model's parts' pictures.</param>
    public static Assembled Assemble(ModelMesh main, List<MaterialPictures> mainPictures, UnitDepiction? depiction,
        Func<string, string?> meshPath, Func<string, ModelMesh?> readModel, Func<ModelMesh, List<MaterialPictures>> picturesOf)
    {
        var nodes = new List<ModelNode>(main.Nodes);
        var parts = new List<ModelPart>(main.Parts);
        var pictures = new List<MaterialPictures>(mainPictures);
        var munitions = new List<(HungMunition Munition, int Order)>();
        var launches = new List<(int Weapon, int Node, int Order)>();
        var read = new Dictionary<string, ModelMesh?>(StringComparer.OrdinalIgnoreCase);
        ModelMesh? Read(string? descriptor)
        {
            if (descriptor is null || meshPath(descriptor) is not string path) return null;
            if (!read.TryGetValue(path, out ModelMesh? mesh)) read[path] = mesh = readModel(path);
            return mesh;
        }
        // the nodes each sub-model brought, by its descriptor, for the mounts a launcher's weapon uses
        var subNodes = new Dictionary<string, (int First, int Count)>(StringComparer.Ordinal);
        foreach (SubModel sub in depiction?.SubModels ?? [])
        {
            int anchor = IndexOf(nodes, sub.Anchor, 0, nodes.Count);
            if (anchor < 0 || Read(sub.Model) is not ModelMesh model || nodes.Count + model.Nodes.Count > ModelRenderer.MostNodes) continue;
            subNodes.TryAdd(sub.Model, (nodes.Count, model.Nodes.Count));
            AppendWithNodes(model, anchor, nodes, parts);
            pictures.AddRange(picturesOf(model));
        }
        foreach (HungWeapon weapon in depiction?.Hung ?? [])
        {
            (int first, int count) = weapon.InModel is string inModel && subNodes.TryGetValue(inModel, out var range) ? range : (0, main.Nodes.Count);
            List<int> mounts = MountsOf(nodes, weapon.Mounts, first, count);
            if (mounts.Count == 0 || weapon.Count <= 0) continue;
            int each = (weapon.Count + mounts.Count - 1) / mounts.Count;
            if (PylonFor(weapon.Pylons, each, Read) is not ModelMesh pylon) continue;
            List<int> places = PlacesOf(pylon);
            ModelMesh? munition = weapon.Munition is null ? null : Read(weapon.Munition);
            string? munitionPath = weapon.Munition is null ? null : meshPath(weapon.Munition);
            int used = munition is null ? mounts.Count : Math.Min(mounts.Count, (weapon.Count + Math.Max(1, Math.Min(each, places.Count)) - 1) / Math.Max(1, Math.Min(each, places.Count)));
            List<MaterialPictures> pylonPictures = picturesOf(pylon), munitionPictures = munition is null ? [] : picturesOf(munition);
            int left = weapon.Count;
            for (int m = 0; m < used; m++)
            {
                int mount = mounts[m];
                Matrix4x4 atMount = nodes[mount].World;
                if (AppendFixed(pylon, mount, atMount, parts)) pictures.AddRange(pylonPictures);
                for (int slot = 0; slot < places.Count && slot < each && left > 0 && nodes.Count < ModelRenderer.MostNodes; slot++, left--)
                {
                    Matrix4x4 at = pylon.Nodes[places[slot]].World * atMount;
                    int node = nodes.Count;
                    // fired a place at a time across the mounts: the first of each pylon, then the second...
                    launches.Add((weapon.WeaponIndex, node, slot * used + m));
                    // a pod's rockets are not shown in it: they leave from its tubes
                    if (munition is null || munitionPath is null)
                    {
                        nodes.Add(new ModelNode($"launch_{weapon.WeaponIndex}_{m + 1}_{slot + 1}", mount, at));
                        continue;
                    }
                    nodes.Add(new ModelNode($"munition_{weapon.WeaponIndex}_{m + 1}_{slot + 1}", mount, at));
                    if (AppendFixed(munition, node, at, parts)) pictures.AddRange(munitionPictures);
                    munitions.Add((new HungMunition(weapon.WeaponIndex, node, munitionPath), slot * used + m));
                }
            }
        }
        if (nodes.Count == main.Nodes.Count && parts.Count == main.Parts.Count) return new Assembled(main, mainPictures, []);
        (Vector3 low, Vector3 high) = Bounds(parts, main);
        var mesh = new ModelMesh(main.Path, parts, nodes, low, high);
        return new Assembled(mesh, pictures, [.. munitions.OrderBy(each => each.Munition.WeaponIndex).ThenBy(each => each.Order).Select(each => each.Munition)])
        {
            Launchers = launches.GroupBy(each => each.Weapon).ToDictionary(group => group.Key, group => (IReadOnlyList<int>)[.. group.OrderBy(each => each.Order).Select(each => each.Node)]),
        };
    }

    /// <summary>A node's place by its name, among so many from the first; -1 when none has it.</summary>
    private static int IndexOf(List<ModelNode> nodes, string name, int first, int count)
    {
        for (int i = first; i < first + count && i < nodes.Count; i++)
            if (string.Equals(nodes[i].Name, name, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>The mounts whose names start so and end in a number (as_1_1, as_1_2...), in number order.</summary>
    private static List<int> MountsOf(List<ModelNode> nodes, string start, int first, int count) =>
        [.. Enumerable.Range(first, Math.Min(count, nodes.Count - first))
            .Where(i => nodes[i].Name.StartsWith(start, StringComparison.Ordinal) && int.TryParse(nodes[i].Name.AsSpan(start.Length), out _))
            .OrderBy(i => int.Parse(nodes[i].Name.AsSpan(start.Length), System.Globalization.CultureInfo.InvariantCulture))];

    /// <summary>A pylon's places for munitions (missile_1, missile_2...), in number order.</summary>
    private static List<int> PlacesOf(ModelMesh pylon) =>
        [.. Enumerable.Range(0, pylon.Nodes.Count)
            .Where(i => pylon.Nodes[i].Name.StartsWith("missile_", StringComparison.Ordinal) && int.TryParse(pylon.Nodes[i].Name.AsSpan("missile_".Length), out _))
            .OrderBy(i => int.Parse(pylon.Nodes[i].Name.AsSpan("missile_".Length), System.Globalization.CultureInfo.InvariantCulture))];

    /// <summary>
    /// The pylon of the set that holds as many munitions as each mount carries (Pylone_2 for two, the pod of twenty
    /// rockets for twenty), else the smallest that holds more, else the largest.
    /// </summary>
    private static ModelMesh? PylonFor(IReadOnlyList<string> pylons, int each, Func<string?, ModelMesh?> read)
    {
        List<(ModelMesh Mesh, int Places)> known = [.. pylons.Select(read).OfType<ModelMesh>().Select(mesh => (mesh, PlacesOf(mesh).Count))];
        return known.Where(pylon => pylon.Places == each).Select(pylon => pylon.Mesh).FirstOrDefault()
            ?? known.Where(pylon => pylon.Places > each).OrderBy(pylon => pylon.Places).Select(pylon => pylon.Mesh).FirstOrDefault()
            ?? known.OrderByDescending(pylon => pylon.Places).Select(pylon => pylon.Mesh).FirstOrDefault();
    }

    /// <summary>A sub-model hung at a node, keeping its own nodes (its top ones under the node), placed where the node is.</summary>
    private static void AppendWithNodes(ModelMesh model, int anchor, List<ModelNode> nodes, List<ModelPart> parts)
    {
        int offset = nodes.Count;
        Matrix4x4 placed = nodes[anchor].World;
        foreach (ModelNode node in model.Nodes)
            nodes.Add(new ModelNode(node.Name, node.Parent >= 0 ? node.Parent + offset : anchor, node.World * placed));
        // its vertices keep their nodes, counted from its first
        foreach (ModelPart part in model.Parts)
            parts.Add(part with { Vertices = Moved(part.Vertices, placed, vertex => vertex.Node), NodeBase = offset + part.NodeBase });
    }

    /// <summary>A model fixed to a node: all of it follows that one node, placed so; false when it has nothing to draw.</summary>
    private static bool AppendFixed(ModelMesh model, int node, Matrix4x4 placed, List<ModelPart> parts)
    {
        // a vertex names its node in a byte: a node further on is counted from itself
        (int from, byte own) = node < ModelRenderer.MostNodes ? (0, (byte)node) : (node, (byte)0);
        foreach (ModelPart part in model.Parts)
            parts.Add(part with { Vertices = Moved(part.Vertices, placed, _ => own, followsOne: true), NodeBase = from, IsTrackStrip = false });
        return model.Parts.Count > 0;
    }

    /// <summary>
    /// Vertices placed in the model: moved and turned so, each following the node given (and the others it bends
    /// with), or that node only.
    /// </summary>
    private static ModelVertex[] Moved(ModelVertex[] vertices, Matrix4x4 placed, Func<ModelVertex, byte> node, bool followsOne = false)
    {
        var moved = new ModelVertex[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            ModelVertex vertex = vertices[i];
            vertex.Position = Vector3.Transform(vertex.Position, placed);
            vertex.Normal = Turned(vertex.Normal, placed);
            vertex.Tangent = Turned(vertex.Tangent, placed);
            vertex.Binormal = Turned(vertex.Binormal, placed);
            vertex.Node = node(vertices[i]);
            if (followsOne) vertex.MoreNodes = vertex.Weights = 0;
            vertex.IsTrack = 0;
            moved[i] = vertex;
        }
        return moved;
    }

    /// <summary>A packed direction turned as the placing turns it.</summary>
    private static uint Turned(uint packed, Matrix4x4 placed)
    {
        static float Unpack(uint value, int shift) => (value >> shift & 0xFF) / 255f * 2 - 1;
        var direction = new Vector3(Unpack(packed, 0), Unpack(packed, 8), Unpack(packed, 16));
        Vector3 turned = Vector3.TransformNormal(direction, placed);
        return ModelVertex.Pack(turned.LengthSquared() > 1e-8f ? Vector3.Normalize(turned) : direction) | (packed & 0xFF000000);
    }

    /// <summary>The corners of the box around every part's vertices; the model's own when it has none.</summary>
    private static (Vector3 Low, Vector3 High) Bounds(List<ModelPart> parts, ModelMesh main)
    {
        Vector3 low = main.Low, high = main.High;
        foreach (ModelPart part in parts)
            foreach (ModelVertex vertex in part.Vertices)
            {
                low = Vector3.Min(low, vertex.Position);
                high = Vector3.Max(high, vertex.Position);
            }
        return (low, high);
    }
}
