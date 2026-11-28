using System.Numerics;
using EasyNdf.Data.Models;
using EasyNdf.Data.Models.Fbx;

namespace EasyNdf.Data.Tests;

/// <summary>
/// A damaged .fbx file: whatever is wrong with it (cut short, bytes changed), reading it either works or says so with
/// an <see cref="InvalidDataException"/>, which the app shows; it never stops with another error, and never hangs.
/// </summary>
public class FbxDamageTests
{
    /// <summary>A small model with an array long enough to be stored compressed.</summary>
    private static byte[] SmallFbx()
    {
        var file = new FbxFile { Version = 7400 };
        file.Nodes.Add(new FbxNode("GlobalSettings", [], [new FbxNode("Properties70", [], [new FbxNode("P", "UnitScaleFactor", "", "", "", 100.0)])]));
        double[] points = [.. Enumerable.Range(0, 90).Select(i => (double)(i % 7))];
        int[] faces = [.. Enumerable.Range(0, 30).Select(i => i % 3 == 2 ? ~i : i)];
        file.Nodes.Add(new FbxNode("Objects", [], [
            new FbxNode("Model", [10L, "Hull\0\u0001Model", "Mesh"], [new FbxNode("Properties70", [], [new FbxNode("P", "Lcl Translation", "", "", "", 1.0, 2.0, 3.0)])]),
            new FbxNode("Geometry", [11L, "Hull\0\u0001Geometry", "Mesh"], [new FbxNode("Vertices", points), new FbxNode("PolygonVertexIndex", faces)]),
            new FbxNode("Material", [40L, "Paint\0\u0001Material", ""], []),
        ]));
        file.Nodes.Add(new FbxNode("Connections", [], [new FbxNode("C", "OO", 10L, 0L), new FbxNode("C", "OO", 11L, 10L), new FbxNode("C", "OO", 40L, 10L)]));
        return file.Write();
    }

    /// <summary>Reads a file and does everything the app does with a model read; only "the file is damaged" may stop it.</summary>
    private static void ReadAndUse(byte[] bytes)
    {
        try
        {
            FbxScene scene = FbxScene.Read(bytes);
            scene.ToMesh("model.fbx", material => new ModelMaterial(material.Name, null));
            _ = scene.HasLargerFaces;
            foreach (FbxPart part in scene.Parts) scene.FarEndOf(part.Id);
            scene.Scale(2);
            scene.Triangulate();
            if (scene.Parts.Count > 0)
            {
                scene.Rename(scene.Parts[0].Id, "chassis");
                scene.AddPoint("fx_tir", scene.Parts[0].Id, Vector3.One);
            }
            scene.Save();
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void WithinTime(Action work, string what)
    {
        Task task = Task.Run(work);
        Assert.True(task.Wait(TimeSpan.FromSeconds(20)), "reading hung on " + what);
    }

    [Fact]
    public void A_file_cut_short_anywhere_is_read_or_said_to_be_damaged()
    {
        byte[] whole = SmallFbx();
        WithinTime(() =>
        {
            for (int length = 0; length < whole.Length; length++) ReadAndUse(whole[..length]);
        }, "a file cut short");
    }

    [Fact]
    public void A_file_with_bytes_changed_is_read_or_said_to_be_damaged()
    {
        byte[] whole = SmallFbx();
        var random = new Random(20261122);
        for (int round = 0; round < 4000; round++)
        {
            byte[] damaged = (byte[])whole.Clone();
            int changes = 1 + random.Next(3);
            for (int i = 0; i < changes; i++) damaged[21 + random.Next(damaged.Length - 21)] = (byte)random.Next(256);
            int at = round;
            WithinTime(() => ReadAndUse(damaged), "round " + at);
        }
    }

    [Fact]
    public void Each_single_byte_set_to_its_extremes_is_read_or_said_to_be_damaged()
    {
        byte[] whole = SmallFbx();
        for (int at = 21; at < whole.Length; at++)
            foreach (byte value in new byte[] { 0, 1, 0x7f, 0x80, 0xff })
            {
                byte[] damaged = (byte[])whole.Clone();
                damaged[at] = value;
                int where = at;
                WithinTime(() => ReadAndUse(damaged), "byte " + where + " = " + value);
            }
    }
}
