using System.Numerics;

namespace EasyNdf.Data.Models.Fbx;

/// <summary>A part of an FBX model: a mesh, a point (an empty, for a firing point or an effect) or a bone.</summary>
/// <param name="Id">Its id in the file.</param>
/// <param name="Name">Its name as written, such as "Tourelle_01".</param>
/// <param name="Kind">"Mesh", "Null" (a point), "LimbNode" (a bone)...</param>
/// <param name="Parent">Its parent's id; 0 for a part at the top.</param>
/// <param name="Local">Where it sits in its parent, as a matrix (a point times it).</param>
public sealed record FbxPart(long Id, string Name, string Kind, long Parent, Matrix4x4 Local)
{
    /// <summary>Where it sits in the model, set once the parts are all read.</summary>
    public Matrix4x4 World { get; internal set; } = Matrix4x4.Identity;
}

/// <summary>A material of an FBX model: its name and the picture files its textures name, by kind.</summary>
/// <param name="Id">Its id in the file.</param>
/// <param name="Name">Its name, such as "M1_Abrams"; the game's pictures for it are named after it.</param>
/// <param name="Files">The files its textures name, by the slot they fill ("DiffuseColor", "NormalMap"...).</param>
public sealed record FbxMaterial(long Id, string Name, IReadOnlyDictionary<string, string> Files);

/// <summary>
/// An FBX model as the app needs it: its parts with where they sit, its meshes' triangles and its materials, all in
/// metres with z up, as the game's own models are made (Blender's FBX: x forward, z up). Read from <see cref="FbxFile"/>,
/// whose records it keeps, so the changes the app makes to a copy (renaming a part, adding a point, scaling the model)
/// are made in those records and saved with <see cref="Save"/>.
/// </summary>
public sealed class FbxScene
{
    /// <summary>The game's units in a metre, as its own models are stored.</summary>
    public const float GameUnitsAMetre = 215;

    private readonly FbxFile file;
    private readonly Dictionary<long, FbxNode> objects = [];
    private readonly List<(string Kind, long Child, long Parent, string Slot)> links = [];

    /// <summary>Its parts, in the file's order.</summary>
    public List<FbxPart> Parts { get; } = [];

    /// <summary>Its materials, in the file's order.</summary>
    public List<FbxMaterial> Materials { get; } = [];

    /// <summary>How many metres one of the file's units is (UnitScaleFactor, in centimetres, over 100).</summary>
    public double MetresAUnit { get; private set; } = 0.01;

    /// <summary>Whether the file has y up (Maya's, 3ds Max's exports), turned here to z up.</summary>
    public bool YUp { get; private set; }

    /// <summary>Whether any mesh bends with bones (a skin), as a tank's tracks do.</summary>
    public bool HasSkin { get; private set; }

    private FbxScene(FbxFile file)
    {
        this.file = file;
        Load();
    }

    /// <summary>Reads a binary FBX file's model.</summary>
    /// <exception cref="InvalidDataException">It is not a binary FBX file, or has no objects.</exception>
    public static FbxScene Read(byte[] data) => new(FbxFile.Read(data));

    private void Load()
    {
        objects.Clear();
        links.Clear();
        Parts.Clear();
        Materials.Clear();
        FbxNode settings = file.Node("GlobalSettings")?.Child("Properties70") ?? new FbxNode("Properties70");
        double unitCm = Property(settings, "UnitScaleFactor")?.Number(4) ?? 1;
        MetresAUnit = (unitCm > 0 ? unitCm : 1) / 100;
        YUp = (Property(settings, "UpAxis")?.Number(4) ?? 2) == 1;
        FbxNode objectsNode = file.Node("Objects") ?? throw new InvalidDataException("the FBX file has no objects");
        foreach (FbxNode node in objectsNode.Children)
            if (node.Values.Count > 0) objects[node.Long(0)] = node;
        foreach (FbxNode link in file.Node("Connections")?.All("C") ?? [])
            links.Add((link.Text(0), link.Long(1), link.Long(2), link.Values.Count > 3 ? link.Text(3) : ""));
        HasSkin = objectsNode.Children.Any(node => node.Name == "Deformer" && node.Text(2) == "Skin");

        foreach (FbxNode node in objectsNode.All("Model"))
        {
            long id = node.Long(0);
            long parent = links.FirstOrDefault(link => link.Child == id && link.Kind == "OO" && objects.TryGetValue(link.Parent, out FbxNode? p) && p.Name == "Model").Parent;
            Parts.Add(new FbxPart(id, NameOf(node), node.Text(2), parent, LocalOf(node)));
        }
        // two parts of one id (a damaged file): the first is the one links find
        var byId = new Dictionary<long, FbxPart>();
        foreach (FbxPart part in Parts) byId.TryAdd(part.Id, part);
        foreach (FbxPart part in Parts) part.World = WorldOf(part, byId, 0);

        foreach (FbxNode node in objectsNode.All("Material"))
        {
            long id = node.Long(0);
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var link in links.Where(link => link.Parent == id && link.Kind == "OP"))
                if (objects.TryGetValue(link.Child, out FbxNode? texture) && texture.Name == "Texture")
                {
                    string name = (texture.Child("RelativeFilename") ?? texture.Child("FileName"))?.Text(0) ?? "";
                    if (name != "") files.TryAdd(link.Slot, name);
                }
            Materials.Add(new FbxMaterial(id, NameOf(node), files));
        }
    }

    private static Matrix4x4 WorldOf(FbxPart part, Dictionary<long, FbxPart> byId, int depth)
    {
        if (depth > 64 || part.Parent == 0 || !byId.TryGetValue(part.Parent, out FbxPart? parent)) return part.Local;
        return part.Local * WorldOf(parent, byId, depth + 1);
    }

    /// <summary>An object's name: the part of its name record before "\0\x01Model".</summary>
    private static string NameOf(FbxNode node)
    {
        string name = node.Text(1);
        int cut = name.IndexOf('\0');
        return cut < 0 ? name : name[..cut];
    }

    /// <summary>A Properties70 record's property of a name; null when it has none.</summary>
    private static FbxNode? Property(FbxNode? properties, string name) =>
        properties?.All("P").FirstOrDefault(p => p.Text(0) == name);

    private static Vector3 Vector(FbxNode? properties, string name, Vector3 otherwise)
    {
        FbxNode? p = Property(properties, name);
        return p is null || p.Values.Count < 7 ? otherwise : new Vector3((float)p.Number(4), (float)p.Number(5), (float)p.Number(6));
    }

    /// <summary>
    /// Where a part sits in its parent: its translation, pre-rotation, rotation (in its rotation order) and scaling, as
    /// the FBX SDK puts them together (pivots and offsets left out: Blender writes none).
    /// </summary>
    private static Matrix4x4 LocalOf(FbxNode model)
    {
        FbxNode? props = model.Child("Properties70");
        Vector3 t = Vector(props, "Lcl Translation", Vector3.Zero);
        Vector3 r = Vector(props, "Lcl Rotation", Vector3.Zero);
        Vector3 s = Vector(props, "Lcl Scaling", Vector3.One);
        Vector3 pre = Vector(props, "PreRotation", Vector3.Zero);
        Vector3 post = Vector(props, "PostRotation", Vector3.Zero);
        int order = (int)(Property(props, "RotationOrder")?.Number(4) ?? 0);
        Matrix4x4 postInverse = Matrix4x4.Invert(Euler(post, 0), out Matrix4x4 inverse) ? inverse : Matrix4x4.Identity;
        return Matrix4x4.CreateScale(s) * postInverse * Euler(r, order) * Euler(pre, 0) * Matrix4x4.CreateTranslation(t);
    }

    /// <summary>A rotation from angles in degrees about x, y and z, applied in an FBX rotation order (0 is x, then y, then z).</summary>
    private static Matrix4x4 Euler(Vector3 degrees, int order)
    {
        const float ToRadians = MathF.PI / 180;
        Matrix4x4 x = Matrix4x4.CreateRotationX(degrees.X * ToRadians), y = Matrix4x4.CreateRotationY(degrees.Y * ToRadians), z = Matrix4x4.CreateRotationZ(degrees.Z * ToRadians);
        // a point times the matrix: the first rotation applied comes first
        return order switch
        {
            1 => x * z * y,
            2 => y * z * x,
            3 => y * x * z,
            4 => z * x * y,
            5 => z * y * x,
            _ => x * y * z,
        };
    }

    // ---- the model as the viewer draws it ----

    /// <summary>
    /// The file's space turned into the game's, a point times it: y up turned to z up, then y turned around (the game
    /// stores models mirrored from Blender's along y), then metres turned into the game's units.
    /// </summary>
    private Matrix4x4 Conversion
    {
        get
        {
            Matrix4x4 up = YUp ? new Matrix4x4(1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1) : Matrix4x4.Identity;
            float scale = (float)(MetresAUnit * GameUnitsAMetre);
            return up * Matrix4x4.CreateScale(scale, -scale, scale);
        }
    }

    /// <summary>A point of the file's space in the game's (see <see cref="Conversion"/>).</summary>
    public Vector3 ToGame(Vector3 point) => Vector3.Transform(point, Conversion);

    private Vector3 DirectionToGame(Vector3 direction)
    {
        Vector3 turned = Vector3.TransformNormal(direction, Conversion);
        return turned.LengthSquared() > 0 ? Vector3.Normalize(turned) : Vector3.UnitZ;
    }

    /// <summary>
    /// The model as the viewer draws it: a node for each part (its name in lower case, as the game looks names up), and a
    /// drawn part for each of its meshes' materials, each triangle's corners turned the other way round as y is.
    /// </summary>
    /// <param name="path">The path the model is shown under.</param>
    /// <param name="colourOf">The colour and normal pictures' paths for a material, as the viewer reads them.</param>
    public ModelMesh ToMesh(string path, Func<FbxMaterial, ModelMaterial> colourOf)
    {
        var nodes = new List<ModelNode>();
        var nodeOf = new Dictionary<long, int>();
        foreach (FbxPart part in Parts)
        {
            nodeOf[part.Id] = nodes.Count;
            nodes.Add(null!);
        }
        foreach (FbxPart part in Parts)
        {
            Matrix4x4 world = GameMatrix(part.World);
            nodes[nodeOf[part.Id]] = new ModelNode(part.Name.ToLowerInvariant(), part.Parent != 0 && nodeOf.TryGetValue(part.Parent, out int parent) ? parent : -1, world);
        }

        var byMaterial = new Dictionary<long, (List<ModelVertex> Vertices, List<int> Indexes)>();
        var low = new Vector3(float.MaxValue);
        var high = new Vector3(float.MinValue);
        foreach (FbxPart part in Parts.Where(part => part.Kind == "Mesh"))
        {
            FbxNode? geometry = links.Where(link => link.Parent == part.Id && link.Kind == "OO").Select(link => objects.GetValueOrDefault(link.Child)).FirstOrDefault(node => node?.Name == "Geometry");
            if (geometry is null) continue;
            long[] materialIds = [.. links.Where(link => link.Parent == part.Id && link.Kind == "OO" && objects.TryGetValue(link.Child, out FbxNode? m) && m.Name == "Material").Select(link => link.Child)];
            Matrix4x4 place = GeometricOf(part) * part.World;
            AddGeometry(geometry, place, nodeOf[part.Id], materialIds, byMaterial, ref low, ref high);
        }
        if (low.X > high.X) low = high = Vector3.Zero;
        var parts = new List<ModelPart>();
        foreach ((long materialId, var built) in byMaterial)
        {
            FbxMaterial material = Materials.FirstOrDefault(m => m.Id == materialId) ?? new FbxMaterial(0, "", new Dictionary<string, string>());
            parts.Add(new ModelPart([.. built.Vertices], [.. built.Indexes], colourOf(material), IsTrackStrip: false));
        }
        return new ModelMesh(path, parts, nodes, low, high);
    }

    /// <summary>A part's geometric offset (moves its mesh without moving its children), as a matrix.</summary>
    private Matrix4x4 GeometricOf(FbxPart part)
    {
        FbxNode? props = objects.GetValueOrDefault(part.Id)?.Child("Properties70");
        Vector3 t = Vector(props, "GeometricTranslation", Vector3.Zero);
        Vector3 r = Vector(props, "GeometricRotation", Vector3.Zero);
        Vector3 s = Vector(props, "GeometricScaling", Vector3.One);
        return Matrix4x4.CreateScale(s) * Euler(r, 0) * Matrix4x4.CreateTranslation(t);
    }

    /// <summary>A part's place of the file's space as one of the game's: from the game's space into the file's, there, then back.</summary>
    private Matrix4x4 GameMatrix(Matrix4x4 world)
    {
        Matrix4x4 conversion = Conversion;
        return Matrix4x4.Invert(conversion, out Matrix4x4 back) ? back * world * conversion : world;
    }

    private void AddGeometry(FbxNode geometry, Matrix4x4 place, int node, long[] materialIds,
        Dictionary<long, (List<ModelVertex> Vertices, List<int> Indexes)> byMaterial, ref Vector3 low, ref Vector3 high)
    {
        double[] points = geometry.Child("Vertices")?.Doubles() ?? [];
        int[] polygons = geometry.Child("PolygonVertexIndex")?.Ints() ?? [];
        if (points.Length < 9 || polygons.Length < 3) return;
        FbxNode? normalLayer = geometry.Child("LayerElementNormal");
        double[] normals = normalLayer?.Child("Normals")?.Doubles() ?? [];
        int[] normalIndex = normalLayer?.Child("NormalsIndex")?.Ints() ?? [];
        string normalMapping = normalLayer?.Child("MappingInformationType")?.Text(0) ?? "";
        FbxNode? uvLayer = geometry.Child("LayerElementUV");
        double[] uvs = uvLayer?.Child("UV")?.Doubles() ?? [];
        int[] uvIndex = uvLayer?.Child("UVIndex")?.Ints() ?? [];
        string uvMapping = uvLayer?.Child("MappingInformationType")?.Text(0) ?? "";
        FbxNode? materialLayer = geometry.Child("LayerElementMaterial");
        int[] polygonMaterials = materialLayer?.Child("Materials")?.Ints() ?? [];
        bool allSame = (materialLayer?.Child("MappingInformationType")?.Text(0) ?? "AllSame") == "AllSame";
        Matrix4x4 turn = place with { M41 = 0, M42 = 0, M43 = 0 };

        int polygon = 0, start = 0;
        for (int i = 0; i < polygons.Length; i++)
        {
            if (polygons[i] >= 0) continue;
            // a polygon's corners run from start to i; the last is written as -(index + 1)
            int count = i - start + 1;
            int material = allSame ? (polygonMaterials.Length > 0 ? polygonMaterials[0] : 0) : polygon < polygonMaterials.Length ? polygonMaterials[polygon] : 0;
            long materialId = material >= 0 && material < materialIds.Length ? materialIds[material] : materialIds.FirstOrDefault();
            if (!byMaterial.TryGetValue(materialId, out var built)) byMaterial[materialId] = built = ([], []);
            int first = built.Vertices.Count;
            for (int c = 0; c < count; c++)
            {
                int corner = start + c;
                int point = polygons[corner] < 0 ? ~polygons[corner] : polygons[corner];
                if (point < 0 || point >= points.Length / 3) continue;
                Vector3 position = ToGame(Vector3.Transform(new Vector3((float)points[point * 3], (float)points[point * 3 + 1], (float)points[point * 3 + 2]), place));
                Vector3 normal = Vector3.UnitZ;
                int n = Layered(normalMapping, normalIndex, corner, point, polygon);
                if (n >= 0 && n < normals.Length / 3)
                    normal = DirectionToGame(Vector3.TransformNormal(new Vector3((float)normals[n * 3], (float)normals[n * 3 + 1], (float)normals[n * 3 + 2]), turn));
                Vector2 uv = Vector2.Zero;
                int u = Layered(uvMapping, uvIndex, corner, point, polygon);
                if (u >= 0 && u < uvs.Length / 2) uv = new Vector2((float)uvs[u * 2], 1 - (float)uvs[u * 2 + 1]);
                low = Vector3.Min(low, position);
                high = Vector3.Max(high, position);
                Vector3 tangent = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX, normal));
                built.Vertices.Add(new ModelVertex
                {
                    Position = position, TexturePlace = uv, Normal = ModelVertex.Pack(normal), Tangent = ModelVertex.Pack(tangent),
                    Binormal = ModelVertex.Pack(Vector3.Cross(normal, tangent)), Node = (byte)Math.Min(node, 255),
                });
            }
            // a fan of triangles over the polygon, turned the other way round since y is turned around
            int made = built.Vertices.Count - first;
            for (int c = 1; c + 1 < made; c++)
            {
                built.Indexes.Add(first);
                built.Indexes.Add(first + c + 1);
                built.Indexes.Add(first + c);
            }
            polygon++;
            start = i + 1;
        }
    }

    /// <summary>Which of a layer's values a corner takes, by the layer's mapping; -1 for none.</summary>
    private static int Layered(string mapping, int[] index, int corner, int point, int polygon)
    {
        int at = mapping switch
        {
            "ByPolygonVertex" => corner,
            "ByVertice" or "ByVertex" or "ByControlPoint" => point,
            "ByPolygon" => polygon,
            "AllSame" => 0,
            _ => -1,
        };
        if (at < 0) return -1;
        if (index.Length > 0) return at < index.Length ? index[at] : -1;
        return at;
    }

    /// <summary>
    /// The far end of a part's mesh along its own x (a gun's muzzle, as the game's guns point along x), in the part's own
    /// space and the file's units: the furthest x, at the middle of the mesh's height and width. The part's origin when
    /// it has no mesh.
    /// </summary>
    public Vector3 FarEndOf(long id)
    {
        FbxNode? geometry = links.Where(link => link.Parent == id && link.Kind == "OO").Select(link => objects.GetValueOrDefault(link.Child)).FirstOrDefault(node => node?.Name == "Geometry");
        double[] points = geometry?.Child("Vertices")?.Doubles() ?? [];
        if (points.Length < 3) return Vector3.Zero;
        FbxPart? part = Parts.FirstOrDefault(each => each.Id == id);
        Matrix4x4 geometric = part is null ? Matrix4x4.Identity : GeometricOf(part);
        var low = new Vector3(float.MaxValue);
        var high = new Vector3(float.MinValue);
        for (int i = 0; i + 2 < points.Length; i += 3)
        {
            Vector3 point = Vector3.Transform(new Vector3((float)points[i], (float)points[i + 1], (float)points[i + 2]), geometric);
            low = Vector3.Min(low, point);
            high = Vector3.Max(high, point);
        }
        return new Vector3(high.X, (low.Y + high.Y) / 2, (low.Z + high.Z) / 2);
    }

    // ---- changes to a copy ----

    /// <summary>
    /// Whether any mesh has a face of more than three corners: the game's mod tools stop on one ("Mesh not exported
    /// triangulating faces"), so the copy the mod carries is split into triangles (<see cref="Triangulate"/>).
    /// </summary>
    public bool HasLargerFaces => objects.Values.Where(node => node.Name == "Geometry").Any(geometry =>
    {
        int corners = 0;
        foreach (int index in geometry.Child("PolygonVertexIndex")?.Ints() ?? [])
        {
            corners++;
            if (index >= 0) continue;
            if (corners != 3) return true;
            corners = 0;
        }
        return false;
    });

    /// <summary>
    /// Splits every face of more than three corners into triangles, as Blender's "Triangulate Faces" export does: a fan
    /// from its first corner. What each corner has (its normal, texture place, colour) and what each face has (its
    /// material, smoothing) go with the triangles made from it; the list of edges, no longer right, is left out.
    /// </summary>
    public void Triangulate()
    {
        foreach (FbxNode geometry in objects.Values.Where(node => node.Name == "Geometry"))
        {
            if (geometry.Child("PolygonVertexIndex") is not FbxNode polygonNode) continue;
            int[] polygons = polygonNode.Ints();
            var corners = new List<int>();      // for each new corner, the old corner it is
            var faces = new List<int>();        // for each new triangle, the old face it is from
            int start = 0, face = 0;
            for (int i = 0; i < polygons.Length; i++)
            {
                if (polygons[i] >= 0) continue;
                for (int c = 1; c + 1 <= i - start; c++)
                {
                    corners.AddRange([start, start + c, start + c + 1]);
                    faces.Add(face);
                }
                face++;
                start = i + 1;
            }
            if (corners.Count == polygons.Length) continue;
            var triangles = new int[corners.Count];
            for (int i = 0; i < corners.Count; i++)
            {
                int point = polygons[corners[i]] < 0 ? ~polygons[corners[i]] : polygons[corners[i]];
                triangles[i] = i % 3 == 2 ? ~point : point;
            }
            polygonNode.Values[0] = triangles;
            geometry.Children.RemoveAll(child => child.Name == "Edges");
            foreach (FbxNode layer in geometry.Children.Where(child => child.Name.StartsWith("LayerElement", StringComparison.Ordinal)))
            {
                string mapping = layer.Child("MappingInformationType")?.Text(0) ?? "";
                List<int> from = mapping switch { "ByPolygonVertex" => corners, "ByPolygon" => faces, _ => [] };
                if (from.Count == 0) continue;
                int oldCount = mapping == "ByPolygonVertex" ? polygons.Length : face;
                FbxNode? index = layer.Children.FirstOrDefault(child => child.Name.EndsWith("Index", StringComparison.Ordinal) && child.Values.Count > 0 && child.Values[0] is int[]);
                if (index is not null && (layer.Child("ReferenceInformationType")?.Text(0) ?? "") != "Direct")
                {
                    int[] old = index.Ints();
                    index.Values[0] = from.Select(at => at < old.Length ? old[at] : 0).ToArray();
                    continue;
                }
                // the values themselves, so many for each corner or face
                foreach (FbxNode values in layer.Children.Where(child => child.Values.Count > 0 && child.Values[0] is Array))
                {
                    var array = (Array)values.Values[0];
                    if (oldCount == 0 || array.Length % oldCount != 0) continue;
                    int each = array.Length / oldCount;
                    Array made = Array.CreateInstance(array.GetType().GetElementType()!, from.Count * each);
                    for (int i = 0; i < from.Count; i++) Array.Copy(array, from[i] * each, made, i * each, each);
                    values.Values[0] = made;
                }
            }
        }
        Load();
    }

    /// <summary>Renames a part (its name keeps the "\0\x01Model" the file writes after it).</summary>
    public void Rename(long id, string name)
    {
        if (!objects.TryGetValue(id, out FbxNode? node) || node.Values.Count < 2) return;
        string written = node.Text(1);
        int cut = written.IndexOf('\0');
        node.Values[1] = name + (cut < 0 ? "\0\u0001Model" : written[cut..]);
        Load();
    }

    /// <summary>
    /// Adds a point (an empty, as Blender writes one) under a part, at a place in that part's own space, in the file's
    /// units.
    /// </summary>
    public void AddPoint(string name, long parent, Vector3 place)
    {
        FbxNode objectsNode = file.Node("Objects")!;
        long id = objects.Keys.DefaultIfEmpty(0).Max() + 1;
        long attributeId = id + 1;
        objectsNode.Children.Add(new FbxNode("NodeAttribute", [attributeId, "\0\u0001NodeAttribute", "Null"],
            [new FbxNode("TypeFlags", "Null"), new FbxNode("Properties70")]));
        var props = new FbxNode("Properties70", [], [
            new FbxNode("P", "Lcl Translation", "Lcl Translation", "", "A", (double)place.X, (double)place.Y, (double)place.Z),
            new FbxNode("P", "DefaultAttributeIndex", "int", "Integer", "", 0),
        ]);
        objectsNode.Children.Add(new FbxNode("Model", [id, name + "\0\u0001Model", "Null"], [new FbxNode("Version", 232), props, new FbxNode("Shading", true), new FbxNode("Culling", "CullingOff")]));
        foreach (FbxNode type in file.Node("Definitions")?.All("ObjectType") ?? [])
            if ((type.Text(0) is "Model" or "NodeAttribute") && type.Child("Count") is FbxNode count) count.Values[0] = (int)count.Long(0) + 1;
        FbxNode? connections = file.Node("Connections");
        if (connections is null) file.Nodes.Add(connections = new FbxNode("Connections"));
        connections.Children.Add(new FbxNode("C", "OO", attributeId, id));
        connections.Children.Add(new FbxNode("C", "OO", id, parent));
        Load();
    }

    /// <summary>
    /// Scales the whole model: every mesh's points and every part's place, so the file's numbers themselves change and
    /// any tool reading it sees the new size. Skins' bind matrices move their places too.
    /// </summary>
    public void Scale(double by)
    {
        foreach (FbxNode node in objects.Values)
        {
            if (node.Name == "Geometry" && node.Child("Vertices") is FbxNode vertices)
                vertices.Values[0] = vertices.Doubles().Select(v => v * by).ToArray();
            else if (node.Name == "Model" && Property(node.Child("Properties70"), "Lcl Translation") is { Values.Count: >= 7 } t)
            {
                for (int i = 4; i < 7; i++) t.Values[i] = t.Number(i) * by;
            }
            else if (node.Name == "Deformer")
            {
                ScaleMatrix(node.Child("Transform"), by);
                ScaleMatrix(node.Child("TransformLink"), by);
            }
            else if (node.Name == "Pose")
            {
                foreach (FbxNode poseNode in node.All("PoseNode")) ScaleMatrix(poseNode.Child("Matrix"), by);
            }
        }
        Load();
    }

    /// <summary>Moves a matrix record's place (its last row) by a scale.</summary>
    private static void ScaleMatrix(FbxNode? matrix, double by)
    {
        if (matrix?.Doubles() is not { Length: 16 } values) return;
        for (int i = 12; i < 15; i++) values[i] *= by;
        matrix.Values[0] = values;
    }

    /// <summary>The file's bytes, with the changes made.</summary>
    public byte[] Save() => file.Write();
}
