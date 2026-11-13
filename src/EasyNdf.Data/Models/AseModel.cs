using System.Numerics;

namespace EasyNdf.Data.Models;

/// <summary>
/// A Steel Division 2 model as its Modding Assets keep it: a 3ds Max scene exported as a binary NDF file (.Ase2NdfBin).
/// The scene (TMemASE) lists its materials, its meshes (TMemASEGeomObject, each a node with a mesh of its own) and its
/// helpers (points with no mesh: firing points, wheel mounts). A mesh lists its vertices, its faces (three vertices
/// each, A, B and C, 0 when left out), each face's three vertex normals, and its texture places with each face's three.
/// A material names its texture files ("…/Assets/3D/Units/GER/Char/Tiger_E_GER/Tiger_E.png"). Node names are read in
/// lower case, as the viewer looks them up ("tourelle_01", "fx_tourelle1_tir_01").
/// 3ds Max's space is right-handed (x forward, y to the left, z up), the viewer's left-handed as WARNO's pack is, so y
/// is turned around (and each face's corners with it): a Tiger's hull machine gun stays on its right.
/// </summary>
public static class AseModel
{
    /// <summary>Reads a model; <paramref name="path"/> is its path in the game, such as "Assets/3D/Units/GER/Char/Tiger_E_GER/Tiger_E_GER.Ase2NdfBin".</summary>
    public static ModelMesh Read(byte[] file, string path)
    {
        var ndf = BinaryNdf.Read(file);
        NdfObject scene = ndf.Objects.FirstOrDefault(each => each.Class == "TMemASE") ?? throw new InvalidDataException("no scene");
        Dictionary<int, MaterialSet> materials = MaterialsOf(ndf, scene);

        // the nodes: the meshes', then the helpers'; each names its parent by name
        var geometry = scene.List("GeomObjectList").Select(ndf.At).OfType<NdfObject>().ToList();
        var helpers = scene.List("HelperObjectList").Select(ndf.At).OfType<NdfObject>().ToList();
        var places = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var named = new List<(string Name, string? Parent, Matrix4x4 World)>();
        foreach (NdfObject node in geometry.Concat(helpers))
        {
            string name = node.Get<string?>("NodeName", null) ?? "";
            places.TryAdd(name, named.Count);
            named.Add((name.ToLowerInvariant(), node.Get<string?>("NodeParent", null), WorldOf(ndf.At(node.Fields.GetValueOrDefault("NodeTM")))));
        }
        var nodes = named.Select(each => new ModelNode(each.Name, each.Parent is string parent && places.TryGetValue(parent, out int at) ? at : -1, each.World)).ToList();

        var parts = new List<ModelPart>();
        var low = new Vector3(float.MaxValue);
        var high = new Vector3(float.MinValue);
        for (int number = 0; number < geometry.Count; number++)
        {
            NdfObject node = geometry[number];
            // a soldier's skeleton is drawn as boxes in 3ds Max (Bip01, Bip01 Spine…), not in the game
            if (nodes[number].Name.StartsWith("bip", StringComparison.Ordinal)) continue;
            if (ndf.At(node.Fields.GetValueOrDefault("Mesh")) is not NdfObject mesh) continue;
            MaterialSet set = materials.GetValueOrDefault(node.Get("MaterialRef", 0)) ?? MaterialSet.None;
            bool track = set.Track || nodes[number].Name.StartsWith("chenille", StringComparison.Ordinal);
            foreach (ModelPart part in PartsOf(ndf, mesh, set, track, number))
            {
                foreach (ModelVertex vertex in part.Vertices)
                {
                    low = Vector3.Min(low, vertex.Position);
                    high = Vector3.Max(high, vertex.Position);
                }
                parts.Add(part);
            }
        }
        if (parts.Count == 0) low = high = Vector3.Zero;
        return new ModelMesh(path, parts, nodes, low, high);
    }

    /// <summary>
    /// A mesh's material: its textures, or its sub-materials when it has some (a face's MESH_MTLID picks one), and
    /// whether it paints a track.
    /// </summary>
    private sealed record MaterialSet(ModelMaterial Main, IReadOnlyList<ModelMaterial> Subs, bool Track)
    {
        public static readonly MaterialSet None = new(new ModelMaterial(null, null), [], false);

        /// <summary>The material a face of this sub-material number is painted with.</summary>
        public ModelMaterial For(int sub) => Subs.Count == 0 ? Main : Subs[((sub % Subs.Count) + Subs.Count) % Subs.Count];
    }

    /// <summary>
    /// The materials, by number (MaterialID, 0 when left out): each one's colour and normal textures as paths from
    /// "Assets/3D/", its sub-materials' (SubMaterialList), and whether it paints a track (its shader's chenilledroite
    /// or chenillegauche is on).
    /// </summary>
    private static Dictionary<int, MaterialSet> MaterialsOf(BinaryNdf ndf, NdfObject scene)
    {
        var materials = new Dictionary<int, MaterialSet>();
        foreach (NdfObject material in scene.List("MaterialList").Select(ndf.At).OfType<NdfObject>())
        {
            (ModelMaterial main, bool track) = Look(ndf, material);
            var subs = material.List("SubMaterialList").Select(ndf.At).OfType<NdfObject>().Select(sub => Look(ndf, sub).Material).ToList();
            materials.TryAdd(material.Get("MaterialID", 0), new MaterialSet(main, subs, track));
        }
        return materials;
    }

    /// <summary>A material's textures, from its Material1's shader settings, and whether it paints a track.</summary>
    private static (ModelMaterial Material, bool Track) Look(BinaryNdf ndf, NdfObject material)
    {
        if (ndf.At(material.Fields.GetValueOrDefault("Material1")) is not NdfObject look) return (new ModelMaterial(null, null), false);
        var shader = look.Get<Dictionary<string, object?>?>("DX9ShaderParams", null) ?? [];
        string? Texture(string key) => ndf.At(shader.GetValueOrDefault(key))?.Get<string?>("Value", null) is string file ? FromAssets(file) : null;
        bool On(string key) => ndf.At(shader.GetValueOrDefault(key))?.Get("Value", false) == true;
        return (new ModelMaterial(Texture("DiffuseTexture"), Texture("NormalTexture")), On("chenilledroite") || On("chenillegauche"));
    }

    /// <summary>A file's path from "Assets/3D/" on ("D:/…/Data/Assets/3D/Units/…png" becomes "Assets/3D/Units/…png"); as written when it has no such folder.</summary>
    public static string FromAssets(string file)
    {
        string forward = file.Replace('\\', '/');
        int at = forward.IndexOf("Assets/3D/", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? forward : forward[at..];
    }

    /// <summary>
    /// Where a node sits, from its NodeTM: its three axes (TM_ROW0..2) and its place (TM_POS), with y turned around
    /// (<see cref="Mirror"/>, and its y axis with it); where it stands when it has none.
    /// </summary>
    private static Matrix4x4 WorldOf(NdfObject? tm)
    {
        if (tm is null) return Matrix4x4.Identity;
        Vector3 x = Mirror(tm.Get("TM_ROW0", Vector3.UnitX)), y = -Mirror(tm.Get("TM_ROW1", Vector3.UnitY)), z = Mirror(tm.Get("TM_ROW2", Vector3.UnitZ));
        Vector3 place = Mirror(tm.Get("TM_POS", tm.Get("TM_ROW3", Vector3.Zero)));
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, place.X, place.Y, place.Z, 1);
    }

    /// <summary>A point or direction of 3ds Max's space in the viewer's: y turned around.</summary>
    private static Vector3 Mirror(Vector3 value) => new(value.X, -value.Y, value.Z);

    /// <summary>
    /// A mesh as a part: each face's three corners become vertices (corners alike in place, texture place and normal
    /// are one vertex), all following the mesh's own node: one part for each material its faces use (a sub-material
    /// by the face's MESH_MTLID); none for a mesh with no faces.
    /// </summary>
    private static List<ModelPart> PartsOf(BinaryNdf ndf, NdfObject mesh, MaterialSet set, bool track, int node)
    {
        Vector3[] places = [.. mesh.List("VertexList").Select(each => Mirror(ndf.At(each)?.Get("Vector", Vector3.Zero) ?? Vector3.Zero))];
        IReadOnlyList<object?> faces = mesh.List("FaceList");
        if (places.Length == 0 || faces.Count == 0) return [];
        IReadOnlyList<object?> normals = mesh.List("NormalsList");
        // the first texture channel: its places, and each face's three
        IReadOnlyList<object?> textureFaces = FirstChannel(mesh, "TFaceMap");
        Vector2[] texturePlaces = [.. FirstChannel(mesh, "TVertexMap").Select(each => ndf.At(each)?.Get("Vector", Vector3.Zero) ?? Vector3.Zero)
            .Select(uvw => new Vector2(uvw.X, 1 - uvw.Y))];

        var builders = new Dictionary<ModelMaterial, PartBuilder>();
        for (int f = 0; f < faces.Count; f++)
        {
            if (ndf.At(faces[f]) is not NdfObject face) continue;
            // A, C, B: turning y around turns each face over, so its corners go the other way round
            int[] corners = [face.Get("A", 0), face.Get("C", 0), face.Get("B", 0)];
            if (corners.Any(corner => corner < 0 || corner >= places.Length)) continue;
            int[] given = ndf.At(f < textureFaces.Count ? textureFaces[f] : null)?.Get<int[]?>("TripleInteger", null) ?? [0, 0, 0];
            int[] textureCorners = given.Length == 3 ? [given[0], given[2], given[1]] : [0, 0, 0];
            IReadOnlyList<object?> faceNormals = ndf.At(f < normals.Count ? normals[f] : null)?.List("NormalList") ?? [];
            IReadOnlyList<object?> cornerNormals = faceNormals.Count == 3 ? [faceNormals[0], faceNormals[2], faceNormals[1]] : faceNormals;
            ModelMaterial material = set.For(face.Get("MESH_MTLID", 0));
            if (!builders.TryGetValue(material, out PartBuilder? builder)) builders[material] = builder = new PartBuilder(track);
            Vector3 faceNormal = Vector3.Normalize(Vector3.Cross(places[corners[1]] - places[corners[0]], places[corners[2]] - places[corners[0]]));
            int[] made3 = new int[3];
            for (int c = 0; c < 3; c++)
            {
                int texture = textureCorners.Length > c && textureCorners[c] >= 0 && textureCorners[c] < texturePlaces.Length ? textureCorners[c] : -1;
                Vector3 normal = c < cornerNormals.Count && ndf.At(cornerNormals[c])?.Get("Vector", Vector3.Zero) is Vector3 written && written != Vector3.Zero
                    ? Vector3.Normalize(Mirror(written)) : faceNormal;
                if (float.IsNaN(normal.X)) normal = Vector3.UnitZ;
                made3[c] = builder.Vertex(corners[c], places[corners[c]], texture, texture < 0 ? Vector2.Zero : texturePlaces[texture], normal);
            }
            builder.Face(made3);
        }
        return [.. builders.Where(pair => pair.Value.HasFaces).Select(pair => pair.Value.Finish(pair.Key, node))];
    }

    /// <summary>One part being made: its vertices (a corner alike in place, texture place and normal is one), its triangles, and each vertex's texture directions so far.</summary>
    private sealed class PartBuilder(bool track)
    {
        private readonly List<ModelVertex> vertices = [];
        private readonly List<Vector3> normals = [];
        private readonly List<Vector3> tangents = [];
        private readonly List<Vector3> binormals = [];
        private readonly List<int> indexes = [];
        private readonly Dictionary<(int, int, uint), int> made = [];

        public bool HasFaces => indexes.Count > 0;

        /// <summary>The vertex for a corner: one made before alike, else a new one.</summary>
        public int Vertex(int place, Vector3 position, int texture, Vector2 texturePlace, Vector3 normal)
        {
            uint packed = ModelVertex.Pack(normal);
            if (made.TryGetValue((place, texture, packed), out int at)) return at;
            at = vertices.Count;
            made[(place, texture, packed)] = at;
            vertices.Add(new ModelVertex { Position = position, TexturePlace = texturePlace, Normal = packed, IsTrack = track ? (byte)1 : (byte)0 });
            normals.Add(normal);
            tangents.Add(Vector3.Zero);
            binormals.Add(Vector3.Zero);
            return at;
        }

        /// <summary>Adds a triangle, and its directions along its texture's width and height to its three vertices'.</summary>
        public void Face(int[] corners)
        {
            indexes.AddRange(corners);
            ModelVertex a = vertices[corners[0]], b = vertices[corners[1]], c = vertices[corners[2]];
            Vector3 edge1 = b.Position - a.Position, edge2 = c.Position - a.Position;
            Vector2 step1 = b.TexturePlace - a.TexturePlace, step2 = c.TexturePlace - a.TexturePlace;
            float area = step1.X * step2.Y - step2.X * step1.Y;
            if (MathF.Abs(area) < 1e-12f) return;
            Vector3 tangent = (edge1 * step2.Y - edge2 * step1.Y) / area;
            Vector3 binormal = (edge2 * step1.X - edge1 * step2.X) / area;
            foreach (int corner in corners)
            {
                tangents[corner] += tangent;
                binormals[corner] += binormal;
            }
        }

        /// <summary>The part, its vertices' texture directions made square to their normals, following node <paramref name="node"/>.</summary>
        public ModelPart Finish(ModelMaterial material, int node)
        {
            ModelVertex[] finished = [.. vertices];
            for (int i = 0; i < finished.Length; i++)
            {
                Vector3 n = normals[i];
                Vector3 t = tangents[i] - n * Vector3.Dot(n, tangents[i]);
                t = t.LengthSquared() > 1e-12f ? Vector3.Normalize(t) : Perpendicular(n);
                Vector3 b = Vector3.Cross(n, t) * (Vector3.Dot(Vector3.Cross(n, t), binormals[i]) < 0 ? -1 : 1);
                finished[i].Tangent = ModelVertex.Pack(t);
                finished[i].Binormal = ModelVertex.Pack(b);
            }
            return new ModelPart(finished, [.. indexes], material, track) { NodeBase = node };
        }
    }

    /// <summary>A mesh's first texture channel in a map of channels (TFaceMap, TVertexMap): channel 1, else the first; none when it has none.</summary>
    private static IReadOnlyList<object?> FirstChannel(NdfObject mesh, string name)
    {
        if (mesh.Get<Dictionary<string, object?>?>(name, null) is not { Count: > 0 } channels) return [];
        object? chosen = channels.TryGetValue("1", out object? first) ? first : channels.Values.First();
        return chosen as List<object?> ?? [];
    }

    /// <summary>Any direction square to <paramref name="normal"/>.</summary>
    private static Vector3 Perpendicular(Vector3 normal) =>
        Vector3.Normalize(MathF.Abs(normal.X) < 0.9f ? Vector3.Cross(normal, Vector3.UnitX) : Vector3.Cross(normal, Vector3.UnitY));
}
