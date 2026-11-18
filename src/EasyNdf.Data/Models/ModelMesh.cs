using System.Numerics;
using System.Runtime.InteropServices;

namespace EasyNdf.Data.Models;

/// <summary>
/// One 3D model as the game's mesh pack stores it: its parts, each with its own textures, and its nodes, the named points
/// parts turn about (a turret, a wheel, a rotor) or effects come from (a firing point).
/// </summary>
/// <param name="Path">Its path in the game, such as "Assets/3D/Units/US/Char/M1A1_Abrams/M1A1_Abrams.fbx".</param>
/// <param name="Parts">Its parts.</param>
/// <param name="Nodes">Its nodes, in the pack's order: a vertex names its node by its place in this list.</param>
/// <param name="Low">The corner of its bounding box with the lowest x, y and z.</param>
/// <param name="High">The corner with the highest.</param>
public sealed record ModelMesh(string Path, IReadOnlyList<ModelPart> Parts, IReadOnlyList<ModelNode> Nodes, Vector3 Low, Vector3 High)
{
    /// <summary>
    /// The folder its pictures come from, when its descriptor names one of its own (Textures, such as
    /// "Assets/3D/Units/COL/Avion/SilverStar_Mk3AT": one model painted for another army); null for its own folder.
    /// </summary>
    public string? Textures { get; init; }

    /// <summary>How many triangles it has, as the viewer's foot shows.</summary>
    public int TriangleCount => Parts.Sum(part => part.Indexes.Length / 3);

    /// <summary>The place of the node of this name; -1 when it has none.</summary>
    public int NodeNamed(string name)
    {
        for (int i = 0; i < Nodes.Count; i++)
            if (string.Equals(Nodes[i].Name, name, StringComparison.Ordinal)) return i;
        return -1;
    }
}

/// <summary>A model as a descriptor names it: its path, and the folder its pictures come from.</summary>
/// <param name="Path">Its path in the game, such as "Assets/3D/Units/CAN/Avion/SilverStar_Mk3AT/SilverStar_Mk3AT.fbx".</param>
/// <param name="Textures">The folder of its pictures when the descriptor names one (Textures); null for the model's own.</param>
public sealed record ModelFile(string Path, string? Textures = null);

/// <summary>A part of a model: its vertices, its triangles (three vertex numbers each) and its material.</summary>
/// <param name="Vertices">Its vertices: each names its nodes counted from <see cref="NodeBase"/>.</param>
/// <param name="Indexes">Its triangles: each three numbers are three of <paramref name="Vertices"/>.</param>
/// <param name="Material">Its material: which textures it is painted with.</param>
/// <param name="IsTrackStrip">
/// Whether it is a tank's track, as a strip of its own: every vertex follows a wheel's suspension (roue_elev_) or a track
/// node (chenille_). A moving track is shown by sliding its texture along it.
/// </param>
public sealed record ModelPart(ModelVertex[] Vertices, int[] Indexes, ModelMaterial Material, bool IsTrackStrip)
{
    /// <summary>
    /// The node its vertices' nodes count from: a vertex names a node in a byte, so a model of more than 256 nodes (a
    /// squad, each soldier with bones of his own) has parts that count from further on. 0 for a model as read.
    /// </summary>
    public int NodeBase { get; init; }
}

/// <summary>A material's textures, as the material names them in its TextureDico; null where it has none.</summary>
/// <param name="Colour">Its colour texture (DiffuseTextureNoAlpha), such as "GameData:/Assets/3D/Units/US/Char/M1A1_Abrams/M1A1_Abrams.png".</param>
/// <param name="Normal">Its normal map (NormalTexture), which gives the flat parts their small bumps and dents.</param>
public sealed record ModelMaterial(string? Colour, string? Normal);

/// <summary>A node of a model: its name, its parent and where it sits.</summary>
/// <param name="Name">Its name, such as "tourelle_01", "roue_d1" or "fx_tourelle1_tir_01".</param>
/// <param name="Parent">The place of its parent node; -1 for a node at the top.</param>
/// <param name="World">
/// Where it sits and which way it faces, in the model's own space, laid out as <see cref="Matrix4x4"/> expects (a point
/// times the matrix): turning the node about its own axes is undoing this, turning, then doing it again.
/// </param>
public sealed record ModelNode(string Name, int Parent, Matrix4x4 World)
{
    /// <summary>Where the node sits, in the model's own space.</summary>
    public Vector3 Place => World.Translation;
}

/// <summary>
/// One vertex as the viewer draws it: 44 bytes, laid out for the graphics card. Directions are packed one byte each, as
/// the pack stores them (0 is -1, 255 is +1). A vehicle's vertex follows one node; a soldier's skin bends between up to
/// four (his bones), each so much.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ModelVertex
{
    /// <summary>Where it is, in the model's own space.</summary>
    public Vector3 Position;

    /// <summary>Where it is on its textures; a texture repeats past 0 to 1.</summary>
    public Vector2 TexturePlace;

    /// <summary>Which way its surface faces: x, y and z, a byte each, then a spare byte.</summary>
    public uint Normal;

    /// <summary>Its surface's direction along the texture's width, packed as <see cref="Normal"/>.</summary>
    public uint Tangent;

    /// <summary>Its surface's direction along the texture's height, packed as <see cref="Normal"/>.</summary>
    public uint Binormal;

    /// <summary>The node it follows most (its place in <see cref="ModelMesh.Nodes"/>, counted from its part's <see cref="ModelPart.NodeBase"/>).</summary>
    public byte Node;

    /// <summary>1 when it is part of a track strip (<see cref="ModelPart.IsTrackStrip"/>), whose texture slides as the tank drives.</summary>
    public byte IsTrack;

    /// <summary>Unused, to keep the nodes on four bytes.</summary>
    public ushort Spare;

    /// <summary>The other nodes it follows, a byte each, counted as <see cref="Node"/> is: the second, third and fourth.</summary>
    public uint MoreNodes;

    /// <summary>
    /// How much it follows each of its nodes, a byte each out of 255, <see cref="Node"/>'s first (the pack's BlW); all 0
    /// for a vertex that follows <see cref="Node"/> only.
    /// </summary>
    public uint Weights;

    /// <summary>How many bytes a vertex takes.</summary>
    public const int Size = 44;

    /// <summary>A direction packed as <see cref="Normal"/> is: each of x, y and z from -1..1 into a byte.</summary>
    public static uint Pack(Vector3 direction) =>
        (uint)ToByte(direction.X) | (uint)ToByte(direction.Y) << 8 | (uint)ToByte(direction.Z) << 16;

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round((value + 1) / 2 * 255), 0, 255);
}
