using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using EasyNdf.Data.Effects;
using EasyNdf.Data.Game;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace EasyNdf.App.Models;

/// <summary>
/// Draws the game's effects' particles over the model: each a picture facing the camera, stretched along its direction
/// when the script stretches it, showing the frame of its texture's animation it has reached; a rotor's blur as a
/// picture of a blade's blur for each blade, lying in the rotor's turn. Far particles are drawn first so near ones
/// cover them. The game's own particle shaders are compiled into the game and cannot be read, so how
/// a particle's colour, the light it gives off and its opacity combine is this viewer's own (see ModelShaders.hlsl);
/// everything else about it is the script's.
/// </summary>
internal sealed class EffectRenderer : IDisposable
{
    /// <summary>A texture's frames on the graphics card.</summary>
    private sealed record Frames(ID3D11Texture2D Texture, ID3D11ShaderResourceView View, int Count, int Named, long Bytes);

    /// <summary>How bright the light a particle gives off is drawn, for each 1 of its EmissivePower: chosen by eye, as the game's shaders cannot be read.</summary>
    private const float GlowScale = 4;

    /// <summary>How brightly lit a particle's own colour is drawn: about as the model's lit sides.</summary>
    private const float LitScale = 1.5f;

    /// <summary>A vertex's numbers: place (3), texture place (2), colour and how much shows (4), lit, glow and opacity (3), frame (1).</summary>
    private const int VertexFloats = 13;

    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly FxTextures textures;
    private readonly ID3D11VertexShader vertexShader;
    private readonly ID3D11PixelShader pixelShader;
    private readonly ID3D11InputLayout layout;
    private readonly ID3D11BlendState blend;
    private readonly ID3D11DepthStencilState depthRead;
    private readonly ID3D11SamplerState clamped, alongWrapped;
    private readonly Dictionary<string, Frames?> framesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Frames Frames, int First, int Count, bool Wraps)> runs = [];
    private Frames? spot;
    private ID3D11Buffer? vertices, indexes;
    private int quadsHeld;
    private float[] built = [];

    /// <summary>Gets the effects' shaders and states ready on the model renderer's device.</summary>
    public EffectRenderer(ModelRenderer renderer, FxTextures textures)
    {
        device = renderer.Device;
        context = renderer.Context;
        this.textures = textures;
        ReadOnlyMemory<byte> vertexCode = ModelRenderer.Compile(renderer.Source, "ParticleVertex", "vs_5_0");
        vertexShader = device.CreateVertexShader(vertexCode.Span);
        pixelShader = device.CreatePixelShader(ModelRenderer.Compile(renderer.Source, "ParticlePixelShader", "ps_5_0").Span);
        layout = device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 20, 0),
            new InputElementDescription("TEXCOORD", 1, Format.R32G32B32_Float, 36, 0),
            new InputElementDescription("TEXCOORD", 2, Format.R32_Float, 48, 0),
        ], vertexCode.Span);
        // light already scaled by how much shows, over what is behind it kept by how little the particle hides it
        var blending = new BlendDescription();
        blending.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.Zero,
            DestinationBlendAlpha = Blend.One,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        blend = device.CreateBlendState(blending);
        // hidden behind the model, but never hiding each other by depth: they are sorted instead
        depthRead = device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual));
        clamped = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp));
        // a ribbon's texture repeats along it
        alongWrapped = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Wrap, TextureAddressMode.Clamp, TextureAddressMode.Clamp));
    }

    /// <summary>How many of the effects' textures are on the graphics card.</summary>
    public int TexturesHeld => framesByName.Values.Count(frames => frames is not null);

    /// <summary>What the effects hold on the graphics card, in bytes: their textures and buffers.</summary>
    public long GraphicsBytes => framesByName.Values.Sum(frames => frames?.Bytes ?? 0) + (spot?.Bytes ?? 0) + (long)quadsHeld * (4 * VertexFloats * 4 + 6 * 4);

    /// <summary>Draws the particles alive, with the frame's camera already set on the device.</summary>
    /// <param name="context">The device's context, drawing into the frame.</param>
    /// <param name="particles">The particles.</param>
    /// <param name="camera">The camera, which the pictures face.</param>
    public void Draw(ID3D11DeviceContext context, IReadOnlyList<FxParticle> particles, ModelCamera camera)
    {
        var shown = new List<(FxParticle Particle, Frames Frames, float Distance, int Quads)>(particles.Count);
        int quads = 0;
        // a ribbon is drawn a piece at a time, between each of its particles and the next one its trail dropped
        var ribbons = new Dictionary<(FxParticle? Trail, FxParticleKind Kind), List<FxParticle>>();
        foreach (FxParticle particle in particles)
            if (particle.Kind.Drawing == FxDrawing.Ribbon)
            {
                if (!ribbons.TryGetValue((particle.Parent, particle.Kind), out List<FxParticle>? strip)) ribbons[(particle.Parent, particle.Kind)] = strip = [];
                strip.Add(particle);
            }
        var nextOnRibbon = new Dictionary<FxParticle, FxParticle>(ReferenceEqualityComparer.Instance);
        int along = FxFields.Of("CurvilinearAbscissa");
        foreach (List<FxParticle> strip in ribbons.Values)
        {
            strip.Sort((a, b) => a.Fields[along].Number.CompareTo(b.Fields[along].Number));
            for (int i = 0; i + 1 < strip.Count; i++) nextOnRibbon[strip[i]] = strip[i + 1];
        }
        foreach (FxParticle particle in particles)
        {
            int count = particle.Kind.Drawing switch
            {
                FxDrawing.Picture => 1,
                FxDrawing.Rotor => Math.Clamp((int)MathF.Round(particle.Fields[FxFields.Of("Count")].Number), 1, MostBlades),
                FxDrawing.Ribbon => nextOnRibbon.ContainsKey(particle) ? 1 : 0,
                _ => 0,
            };
            if (count == 0 || FramesOf(particle.Kind) is not Frames frames) continue;
            shown.Add((particle, frames, Vector3.Dot(particle.Position - camera.Eye, camera.Forward), count));
            quads += count;
        }
        if (shown.Count == 0) return;
        // far ones first, so near ones are drawn over them
        shown.Sort((a, b) => b.Distance.CompareTo(a.Distance));
        Hold(quads);
        if (built.Length < quads * 4 * VertexFloats) built = new float[quads * 4 * VertexFloats];
        runs.Clear();
        int quad = 0;
        foreach ((FxParticle particle, Frames frames, _, int count) in shown)
        {
            bool wraps = particle.Kind.Drawing == FxDrawing.Ribbon;
            if (particle.Kind.Drawing == FxDrawing.Rotor) BuildRotor(particle, quad, count);
            else if (wraps) BuildRibbonPiece(particle, nextOnRibbon[particle], frames, camera, quad * 4 * VertexFloats);
            else Build(particle, frames, camera, quad * 4 * VertexFloats);
            if (runs.Count > 0 && runs[^1].Frames == frames && runs[^1].Wraps == wraps) runs[^1] = runs[^1] with { Count = runs[^1].Count + count };
            else runs.Add((frames, quad, count, wraps));
            quad += count;
        }
        MappedSubresource mapped = context.Map(vertices!, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            Marshal.Copy(built, 0, mapped.DataPointer, quads * 4 * VertexFloats);
        }
        finally
        {
            context.Unmap(vertices!, 0);
        }
        context.OMSetBlendState(blend);
        context.OMSetDepthStencilState(depthRead);
        context.IASetInputLayout(layout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.IASetVertexBuffer(0, vertices!, (uint)(VertexFloats * 4), 0);
        context.IASetIndexBuffer(indexes!, Format.R32_UInt, 0);
        context.VSSetShader(vertexShader);
        context.PSSetShader(pixelShader);
        context.PSSetSampler(0, clamped);
        foreach ((Frames frames, int first, int count, bool wraps) in runs)
        {
            context.PSSetSampler(0, wraps ? alongWrapped : clamped);
            context.PSSetShaderResource(0, frames.View);
            context.DrawIndexed((uint)(count * 6), (uint)(first * 6), 0);
        }
        context.PSSetShaderResource(0, null!);
        context.OMSetBlendState(null);
    }

    /// <summary>
    /// A particle's four corners: a square of its Size facing the camera, turned by its Rotation; or, stretched
    /// (Stretching above 0), as long as its Size times its stretching along its Direction as the camera sees it, moved
    /// along it by OffsetAlongDirection, its texture's width running along it, the right edge in front: the game's
    /// streaks (tracers, sparks) are long pictures with their bright head on the right.
    /// </summary>
    private void Build(FxParticle particle, Frames frames, ModelCamera camera, int at)
    {
        FxValue[] fields = particle.Fields;
        Vector3 centre = particle.Position;
        float half = MathF.Abs(fields[FxFields.Size].Number) / 2;
        Vector3 across, along;
        Vector3 direction = fields[FxFields.Direction].Vector;
        float stretching = fields[FxFields.Stretching].Number;
        Vector3 onScreen = direction - camera.Forward * Vector3.Dot(direction, camera.Forward);
        bool stretched = stretching > 0 && onScreen.LengthSquared() > 1e-6f;
        if (stretched)
        {
            Vector3 way = Vector3.Normalize(onScreen);
            along = way * half * stretching;
            across = Vector3.Normalize(Vector3.Cross(camera.Forward, way)) * half;
            centre += Vector3.Normalize(direction) * fields[FxFields.OffsetAlongDirection].Number;
        }
        else
        {
            float turn = fields[FxFields.Rotation].Number, cos = MathF.Cos(turn), sin = MathF.Sin(turn);
            across = (camera.Right * cos + camera.Up * sin) * half;
            along = (camera.Up * cos - camera.Right * sin) * half;
        }
        Vector3 colour = Vector3.Max(Vector3.Zero, fields[FxFields.ColorRGB].Vector * fields[FxFields.ColorModifier].Vector * fields[FxFields.ColorMultiplier].Vector);
        float shows = Math.Clamp(fields[FxFields.Alpha].Number, 0, 1);
        // only particles drawn as giving off light (EmissiveParticle) do; smoke's EmissivePower does not light it
        float glow = particle.Kind.Emissive ? Math.Max(0, fields[FxFields.EmissivePower].Number) * GlowScale : 0;
        float given = fields[FxFields.Opacity].Number;
        float opacity = particle.Kind.Opaque ? 1 : given == FxFields.NotGiven ? (particle.Kind.Emissive ? 0 : 1) : Math.Clamp(given, 0, 1);
        float frame = FrameNumber(particle, frames);
        (float U, float V)[] places = stretched ? StretchedPlaces : SquarePlaces;
        Corner(at, centre - across + along, places[0].U, places[0].V, colour, shows, glow, opacity, frame);
        Corner(at + VertexFloats, centre + across + along, places[1].U, places[1].V, colour, shows, glow, opacity, frame);
        Corner(at + 2 * VertexFloats, centre + across - along, places[2].U, places[2].V, colour, shows, glow, opacity, frame);
        Corner(at + 3 * VertexFloats, centre - across - along, places[3].U, places[3].V, colour, shows, glow, opacity, frame);
    }

    /// <summary>The texture's place at each corner of a square particle, across then down.</summary>
    private static readonly (float U, float V)[] SquarePlaces = [(0, 0), (1, 0), (1, 1), (0, 1)];

    /// <summary>The texture's place at each corner of a stretched one: its width along it, its right edge in front.</summary>
    private static readonly (float U, float V)[] StretchedPlaces = [(1, 0), (1, 1), (0, 1), (0, 0)];

    /// <summary>
    /// A piece of a ribbon: from a particle to the next its trail dropped, as wide as each one's Size, facing the camera,
    /// the texture's width running along it by their TexCoordU and its height across it.
    /// </summary>
    private void BuildRibbonPiece(FxParticle start, FxParticle end, Frames frames, ModelCamera camera, int at)
    {
        Vector3 from = start.Position, to = end.Position, along = to - from;
        if (along.LengthSquared() < 1e-6f) along = camera.Right;
        Vector3 Across(FxParticle particle)
        {
            Vector3 side = Vector3.Cross(Vector3.Normalize(along), Vector3.Normalize(particle.Position - camera.Eye));
            return (side.LengthSquared() > 1e-8f ? Vector3.Normalize(side) : camera.Up) * MathF.Abs(particle.Fields[FxFields.Size].Number) / 2;
        }
        Vector3 acrossFrom = Across(start), acrossTo = Across(end);
        int u = FxFields.Of("TexCoordU");
        float frame = FrameNumber(start, frames);
        // corners in the order a quad is drawn: start's two, then end's two the other way round, the texture's height
        // running from one edge (0) to the other (1) the same way at both ends
        void Corners(FxParticle particle, Vector3 centre, Vector3 across, int first, bool backwards)
        {
            FxValue[] fields = particle.Fields;
            Vector3 colour = Vector3.Max(Vector3.Zero, fields[FxFields.ColorRGB].Vector * fields[FxFields.ColorModifier].Vector * fields[FxFields.ColorMultiplier].Vector);
            float shows = Math.Clamp(fields[FxFields.Alpha].Number, 0, 1);
            float glow = particle.Kind.Emissive ? Math.Max(0, fields[FxFields.EmissivePower].Number) * GlowScale : 0;
            float given = fields[FxFields.Opacity].Number;
            float opacity = particle.Kind.Opaque ? 1 : given == FxFields.NotGiven ? (particle.Kind.Emissive ? 0 : 1) : Math.Clamp(given, 0, 1);
            float place = fields[u].Number;
            Corner(first, backwards ? centre + across : centre - across, place, backwards ? 1 : 0, colour, shows, glow, opacity, frame);
            Corner(first + VertexFloats, backwards ? centre - across : centre + across, place, backwards ? 0 : 1, colour, shows, glow, opacity, frame);
        }
        Corners(start, from, acrossFrom, at, false);
        Corners(end, to, acrossTo, at + 2 * VertexFloats, true);
    }

    /// <summary>The most blades a rotor's blur is drawn with.</summary>
    private const int MostBlades = 16;

    /// <summary>
    /// A rotor's blur (RotorRenderer): for each of its Count blades, spread evenly round its UpVector from its Direction,
    /// the picture of a blade's blur lying in the rotor's turn, from the hub (the picture's left edge) out to its Size,
    /// and as wide as that across (the picture is square). It covers what is behind it as much as it shows, with its
    /// colour unlit (black for the game's rotors). The game's rotor shader cannot be read: the picture's size and how it
    /// lies are this viewer's reading of the script.
    /// </summary>
    private void BuildRotor(FxParticle particle, int firstQuad, int blades)
    {
        FxValue[] fields = particle.Fields;
        Vector3 hub = particle.Position, up = fields[FxFields.Of("UpVector")].Vector, first = fields[FxFields.Direction].Vector;
        if (up.LengthSquared() < 1e-8f) up = Vector3.UnitZ;
        up = Vector3.Normalize(up);
        first -= up * Vector3.Dot(first, up);
        first = first.LengthSquared() > 1e-8f ? Vector3.Normalize(first) : Vector3.Normalize(Vector3.Cross(up, Math.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
        float length = MathF.Abs(fields[FxFields.Size].Number);
        Vector3 colour = Vector3.Max(Vector3.Zero, fields[FxFields.ColorRGB].Vector * fields[FxFields.ColorModifier].Vector * fields[FxFields.ColorMultiplier].Vector);
        float shows = Math.Clamp(fields[FxFields.Alpha].Number, 0, 1);
        for (int blade = 0; blade < blades; blade++)
        {
            Vector3 along = Vector3.Transform(first, Quaternion.CreateFromAxisAngle(up, blade * 2 * MathF.PI / blades));
            Vector3 across = Vector3.Normalize(Vector3.Cross(up, along)) * length / 2, tip = hub + along * length;
            int at = (firstQuad + blade) * 4 * VertexFloats;
            Corner(at, hub - across, 0, 0, colour, shows, 0, 1, 0);
            Corner(at + VertexFloats, tip - across, 1, 0, colour, shows, 0, 1, 0);
            Corner(at + 2 * VertexFloats, tip + across, 1, 1, colour, shows, 0, 1, 0);
            Corner(at + 3 * VertexFloats, hub + across, 0, 1, colour, shows, 0, 1, 0);
        }
    }

    /// <summary>One corner of a quad: its place, its place on the texture, its colour and how it is drawn.</summary>
    private void Corner(int start, Vector3 place, float u, float v, Vector3 colour, float shows, float glow, float opacity, float frame)
    {
        float[] b = built;
        b[start] = place.X; b[start + 1] = place.Y; b[start + 2] = place.Z;
        b[start + 3] = u; b[start + 4] = v;
        b[start + 5] = colour.X; b[start + 6] = colour.Y; b[start + 7] = colour.Z; b[start + 8] = shows;
        b[start + 9] = LitScale; b[start + 10] = glow; b[start + 11] = opacity;
        b[start + 12] = frame;
    }

    /// <summary>
    /// Which frame a particle shows: as far through the animation as its Animation value (0 to 1) when it animates;
    /// the picture its TextureSmallIndex picks when it has one; else the one its script names.
    /// </summary>
    private static float FrameNumber(FxParticle particle, Frames frames)
    {
        if (frames.Count == 1) return 0;
        if (particle.Kind.Animates) return Math.Clamp((int)(Math.Clamp(particle.Fields[FxFields.Animation].Number, 0, 1) * frames.Count), 0, frames.Count - 1);
        float index = particle.Fields[FxFields.TextureSmallIndex].Number;
        return index > 0 ? (int)index % frames.Count : frames.Named;
    }

    // ---- textures ----

    /// <summary>A kind's texture on the card, read the first time it is drawn; a soft round spot when the archives do not have it.</summary>
    private Frames? FramesOf(FxParticleKind kind)
    {
        if (kind.Drawn is Frames known) return known;
        Frames? frames = null;
        if (kind.Texture is string name)
        {
            string key = name + (kind.Animates ? "#animated" : "");
            if (!framesByName.TryGetValue(key, out frames))
            {
                FxTextureSet? set = null;
                try
                {
                    set = textures.Read(name, kind.Animates);
                }
                catch (Exception failure) when (failure is InvalidDataException or IOException or NotSupportedException or IndexOutOfRangeException)
                {
                    set = null;
                }
                framesByName[key] = frames = set is null ? null : Upload(set);
            }
        }
        frames ??= spot ??= SoftSpot();
        kind.Drawn = frames;
        return frames;
    }

    /// <summary>A texture's frames as one texture of slices, kept in its block-compressed form as the game keeps it.</summary>
    private Frames Upload(FxTextureSet set)
    {
        Texture first = set.Frames[0];
        Format format = first.Format.StartsWith("BC3_SRGB", StringComparison.Ordinal) ? Format.BC3_UNorm_SRgb : Format.BC3_UNorm;
        var description = new Texture2DDescription(format, (uint)first.Width, (uint)first.Height, (uint)set.Frames.Count, 1, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        ID3D11Texture2D texture = device.CreateTexture2D(description);
        uint rowBytes = (uint)Math.Max(1, (first.Width + 3) / 4) * 16;
        long bytes = 0;
        for (int slice = 0; slice < set.Frames.Count; slice++)
        {
            byte[] blocks = set.Frames[slice].Pixels;
            context.UpdateSubresource(blocks, texture, (uint)slice, rowBytes, 0, null);
            bytes += blocks.Length;
        }
        return new Frames(texture, device.CreateShaderResourceView(texture, null), set.Frames.Count, set.Named, bytes);
    }

    /// <summary>A soft white spot, for a texture the archives do not have (the scripts' default, spot.png, is one).</summary>
    private Frames SoftSpot()
    {
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                float fade = Math.Clamp(1 - MathF.Sqrt(dx * dx + dy * dy), 0, 1);
                int at = (y * size + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = 255;
                pixels[at + 3] = (byte)(fade * fade * 255);
            }
        var description = new Texture2DDescription(Format.R8G8B8A8_UNorm_SRgb, size, size, 1, 1, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        ID3D11Texture2D texture = device.CreateTexture2D(description);
        context.UpdateSubresource(pixels, texture, 0, size * 4, 0, null);
        return new Frames(texture, device.CreateShaderResourceView(texture, null), 1, 0, pixels.Length);
    }

    // ---- buffers ----

    /// <summary>Makes the buffers hold at least so many particles, growing by half again so they are seldom remade.</summary>
    private void Hold(int quads)
    {
        if (quads <= quadsHeld && vertices is not null) return;
        quadsHeld = Math.Max(256, quads + quads / 2);
        vertices?.Dispose();
        indexes?.Dispose();
        vertices = device.CreateBuffer((uint)(quadsHeld * 4 * VertexFloats * 4), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
        var corners = new uint[quadsHeld * 6];
        for (uint quad = 0; quad < quadsHeld; quad++)
        {
            uint first = quad * 4, at = quad * 6;
            corners[at] = first;
            corners[at + 1] = first + 1;
            corners[at + 2] = first + 2;
            corners[at + 3] = first;
            corners[at + 4] = first + 2;
            corners[at + 5] = first + 3;
        }
        indexes = device.CreateBuffer(corners, BindFlags.IndexBuffer, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
    }

    /// <summary>Lets go of the effects' textures and buffers on the graphics card.</summary>
    public void Dispose()
    {
        foreach (Frames? frames in framesByName.Values)
        {
            frames?.View.Dispose();
            frames?.Texture.Dispose();
        }
        framesByName.Clear();
        spot?.View.Dispose();
        spot?.Texture.Dispose();
        spot = null;
        vertices?.Dispose();
        indexes?.Dispose();
        vertices = indexes = null;
        quadsHeld = 0;
        built = [];
        foreach (IDisposable resource in new IDisposable[] { vertexShader, pixelShader, layout, blend, depthRead, clamped, alongWrapped }) resource.Dispose();
    }
}
