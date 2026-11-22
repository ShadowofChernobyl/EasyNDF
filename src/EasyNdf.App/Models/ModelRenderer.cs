using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasyNdf.Data.Game;
using EasyNdf.Data.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using D9 = Vortice.Direct3D9;

namespace EasyNdf.App.Models;

/// <summary>What one frame is drawn from: the camera, and the few things that change from frame to frame.</summary>
/// <param name="ViewProjection">The camera: a point in the model's space times this gives where it is on the picture.</param>
/// <param name="Eye">Where the camera is.</param>
/// <param name="Far">The farthest distance drawn.</param>
/// <param name="TrackSlide">How far the tracks' textures have slid.</param>
/// <param name="Moved">How far the whole model has moved (driven forward), so the floor's light and its shadow follow it.</param>
internal readonly record struct FrameView(Matrix4x4 ViewProjection, Vector3 Eye, float Far, float TrackSlide, Vector3 Moved);

/// <summary>
/// Draws a model with Direct3D 11 into a picture WPF shows. WPF's own 3D has no custom lighting or normal maps, so the
/// model is drawn off screen on the graphics card and handed to WPF there, as a <see cref="D3DImage"/> that shares the
/// drawn picture's memory through Direct3D 9; where that cannot be (Windows' software renderer, WARP, draws it, or WPF
/// itself draws in software), each frame is copied into a <see cref="WriteableBitmap"/>. Frames are drawn only while
/// something moves.
/// </summary>
internal sealed class ModelRenderer : IDisposable
{
    /// <summary>One part as drawn: where its triangles are in the shared buffers, its textures, and the node its vertices' nodes count from.</summary>
    private sealed record DrawnPart(uint FirstIndex, uint IndexCount, ID3D11ShaderResourceView?[] Maps, int NodeBase = 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 ViewProjection;
        public Vector3 Eye;
        public float TrackSlide;
        public float Far;
        public Vector2 Size;
        public float WireLift;
        public float Ground;
        public Vector3 Unused;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FloorConstants
    {
        public Vector4 Place;
        public Vector4 Shadow;
        public float Lamp;
        public Vector3 Grid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MaterialConstants
    {
        public int Has;
        public Vector3 Unused;
    }

    /// <summary>The most nodes a pose can move: a vertex names its node in one byte.</summary>
    public const int MostNodes = 256;

    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly ID3D11VertexShader modelVertex, backdropVertex, floorVertex, gridVertex, wireVertex;
    private readonly ID3D11PixelShader modelPixel, distancePixel, backdropPixel, screenPixel, floorPixel, shadowPixel, gridPixel, wirePixel;
    private readonly ID3D11InputLayout layout, gridLayout;
    private readonly ID3D11Buffer frameBuffer, materialBuffer, poseBuffer, floorBuffer;
    private readonly ID3D11RasterizerState bothSides, wireframe;
    private readonly ID3D11SamplerState smooth, clamped;
    private readonly ID3D11DepthStencilState depthOn, depthOff, depthRead;
    private readonly ID3D11BlendState lowestKept, seeThrough;
    private readonly ID3D11Texture2D shadow;
    private readonly ID3D11RenderTargetView shadowTarget;
    private readonly ID3D11ShaderResourceView shadowView;
    private readonly uint samples;
    private readonly List<IDisposable> modelResources = [];
    private readonly List<DrawnPart> parts = [];
    private ID3D11Buffer? vertices, indexes, gridLines;
    private uint gridPoints;
    private float gridReach;
    private Vector3 low, high;

    private int width, height;
    private ID3D11Texture2D? colour, depth, resolved, screen, staging;

    /// <summary>
    /// Direct3D 9 on the graphics card, through which WPF is handed the picture drawn (<see cref="screen"/>, shared);
    /// null where it cannot be.
    /// </summary>
    private readonly D9.IDirect3D9Ex? direct3D9;
    private readonly D9.IDirect3DDevice9Ex? device9;
    private D9.IDirect3DTexture9? sharedTexture;
    private D9.IDirect3DSurface9? sharedSurface;

    /// <summary>The picture WPF shows: the shared one on the graphics card, or else a copy of each frame.</summary>
    private D3DImage? image;
    private WriteableBitmap? bitmap;
    private ID3D11RenderTargetView? colourView, screenView;
    private ID3D11ShaderResourceView? resolvedView;
    private ID3D11DepthStencilView? depthView;
    private long modelBytes;
    private ID3D11Texture2D? distance, distanceDepth, distanceStaging;
    private ID3D11RenderTargetView? distanceView;
    private ID3D11DepthStencilView? distanceDepthView;

    private ModelRenderer(ID3D11Device device, bool isSoftware)
    {
        this.device = device;
        context = device.ImmediateContext;
        IsSoftware = isSoftware;
        string source = ShaderSource();
        ReadOnlyMemory<byte> vertexCode = Compile(source, "ModelVertex", "vs_5_0");
        modelVertex = device.CreateVertexShader(vertexCode.Span);
        backdropVertex = device.CreateVertexShader(Compile(source, "BackdropVertex", "vs_5_0").Span);
        modelPixel = device.CreatePixelShader(Compile(source, "ModelPixel", "ps_5_0").Span);
        distancePixel = device.CreatePixelShader(Compile(source, "DistancePixel", "ps_5_0").Span);
        backdropPixel = device.CreatePixelShader(Compile(source, "BackdropPixel", "ps_5_0").Span);
        screenPixel = device.CreatePixelShader(Compile(source, "ScreenPixel", "ps_5_0").Span);
        floorVertex = device.CreateVertexShader(Compile(source, "FloorVertex", "vs_5_0").Span);
        floorPixel = device.CreatePixelShader(Compile(source, "FloorPixel", "ps_5_0").Span);
        shadowPixel = device.CreatePixelShader(Compile(source, "ShadowPixel", "ps_5_0").Span);
        ReadOnlyMemory<byte> gridCode = Compile(source, "GridVertex", "vs_5_0");
        gridVertex = device.CreateVertexShader(gridCode.Span);
        gridPixel = device.CreatePixelShader(Compile(source, "GridPixel", "ps_5_0").Span);
        wireVertex = device.CreateVertexShader(Compile(source, "WireVertex", "vs_5_0").Span);
        wirePixel = device.CreatePixelShader(Compile(source, "WirePixel", "ps_5_0").Span);
        gridLayout = device.CreateInputLayout([new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0)], gridCode.Span);
        Source = source;
        layout = device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
            new InputElementDescription("NORMAL", 0, Format.R8G8B8A8_UNorm, 20, 0),
            new InputElementDescription("TANGENT", 0, Format.R8G8B8A8_UNorm, 24, 0),
            new InputElementDescription("BINORMAL", 0, Format.R8G8B8A8_UNorm, 28, 0),
            new InputElementDescription("BLENDINDICES", 0, Format.R8G8_UInt, 32, 0),
            new InputElementDescription("BLENDINDICES", 1, Format.R8G8B8A8_UInt, 36, 0),
            new InputElementDescription("BLENDWEIGHT", 0, Format.R8G8B8A8_UNorm, 40, 0),
        ], vertexCode.Span);
        frameBuffer = device.CreateBuffer((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
        materialBuffer = device.CreateBuffer((uint)Marshal.SizeOf<MaterialConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
        poseBuffer = device.CreateBuffer((uint)(MostNodes * 64), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
        floorBuffer = device.CreateBuffer((uint)Marshal.SizeOf<FloorConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
        // the game's models are not closed shapes, so both sides of every triangle are drawn
        bothSides = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { MultisampleEnable = true, DepthClipEnable = true });
        wireframe = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Wireframe) { MultisampleEnable = true, DepthClipEnable = true });
        smooth = device.CreateSamplerState(new SamplerDescription(Filter.Anisotropic, TextureAddressMode.Wrap, TextureAddressMode.Wrap, TextureAddressMode.Wrap) { MaxAnisotropy = 8, MaxLOD = float.MaxValue });
        depthOn = device.CreateDepthStencilState(DepthStencilDescription.Default);
        depthOff = device.CreateDepthStencilState(DepthStencilDescription.None);
        // the grid and the triangles' lines: hidden behind the model, hiding nothing themselves
        depthRead = device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual));
        var seeThroughBlend = new BlendDescription();
        seeThroughBlend.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.Zero,
            DestinationBlendAlpha = Blend.One,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        seeThrough = device.CreateBlendState(seeThroughBlend);
        // the model seen from straight above, for its shadow: the lowest part over each point is kept, and its smaller
        // copies blur it
        clamped = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp) { MaxLOD = float.MaxValue });
        var lowest = new BlendDescription();
        lowest.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.One,
            BlendOperation = BlendOperation.Min,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.One,
            BlendOperationAlpha = BlendOperation.Min,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        lowestKept = device.CreateBlendState(lowest);
        shadow = device.CreateTexture2D(new Texture2DDescription(Format.R16G16_Float, ShadowSize, ShadowSize, 1, 0, BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips));
        shadowTarget = device.CreateRenderTargetView(shadow, null);
        shadowView = device.CreateShaderResourceView(shadow, null);
        // four samples a pixel smooth the model's edges
        samples = device.CheckMultisampleQualityLevels(LightFormat, 4) > 0 ? 4u : 1u;
        // the picture is shared with WPF only when both draw on the graphics card
        if (!isSoftware && RenderCapability.Tier >> 16 >= 2)
        {
            D9.IDirect3D9Ex? nine = null;
            try
            {
                nine = D9.D3D9.Direct3DCreate9Ex();
                var present = new D9.PresentParameters { Windowed = true, SwapEffect = D9.SwapEffect.Discard, DeviceWindowHandle = GetDesktopWindow(), BackBufferWidth = 1, BackBufferHeight = 1 };
                device9 = nine.CreateDeviceEx(0, D9.DeviceType.Hardware, IntPtr.Zero, D9.CreateFlags.HardwareVertexProcessing | D9.CreateFlags.Multithreaded | D9.CreateFlags.FpuPreserve, present);
                direct3D9 = nine;
            }
            catch (Exception failure) when (failure is SharpGen.Runtime.SharpGenException or DllNotFoundException)
            {
                nine?.Dispose();
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    /// <summary>What the model is drawn into: light, which can be brighter than white, turned into the screen's colours last.</summary>
    private const Format LightFormat = Format.R16G16B16A16_Float;

    /// <summary>How many pixels across the picture of the model from above is, which its shadow is made from (as ModelShaders.hlsl has it).</summary>
    private const uint ShadowSize = 512;

    /// <summary>Whether the model stands on a floor lit by a lamp above, with its shadow; a plain dark backdrop when not.</summary>
    public bool ShowsFloor { get; set; } = true;

    /// <summary>Whether lines 6 metres apart are drawn on the ground under the model.</summary>
    public bool ShowsGrid { get; set; } = true;

    /// <summary>Whether the model's triangles are drawn over it.</summary>
    public bool ShowsWireframe { get; set; }

    /// <summary>The shaders' source, for the effects' own shaders.</summary>
    public string Source { get; }

    /// <summary>The device the effects are drawn with, for the effects' own buffers and shaders.</summary>
    public ID3D11Device Device => device;

    /// <summary>The device's context, for the effects' own textures.</summary>
    public ID3D11DeviceContext Context => context;

    /// <summary>
    /// What the renderer holds on the graphics card (or, drawn by Windows' software renderer, in this program's
    /// memory), in bytes: the model's buffers and textures, and the pictures it draws into.
    /// </summary>
    public long GraphicsBytes => modelBytes + TargetBytes;

    /// <summary>The picture WPF shows, in bytes, when it is a copy kept outside the graphics card; 0 when it is shared there.</summary>
    public long PictureBytes => bitmap is null ? 0 : (long)width * height * 4;

    /// <summary>The pictures read back through (each frame's, or Copy Picture's, and the marks'), in bytes: kept in this program's memory even when the graphics card draws.</summary>
    public long ReadBackBytes => (long)width * height * 4 + (long)Math.Max(8, width / 2) * Math.Max(8, height / 2) * 4;

    private long TargetBytes => (long)width * height * (8 * samples + 4 * samples + 8 + 4 + 4) + (long)Math.Max(8, width / 2) * Math.Max(8, height / 2) * 12
        + (long)ShadowSize * ShadowSize * 4 * 4 / 3;

    /// <summary>Whether the model is drawn by Windows' software renderer, which is slower: this computer's graphics card could not be used.</summary>
    public bool IsSoftware { get; }

    /// <summary>The picture the frames are drawn into; null before the first <see cref="Resize"/>.</summary>
    public ImageSource? Picture => (ImageSource?)image ?? bitmap;

    /// <summary>Raised when WPF can show the shared picture again after losing it (the screen locked, the card reset): draw it again.</summary>
    public event Action? PictureBack;

    /// <summary>
    /// A renderer on the graphics card, or on Windows' software renderer when the card cannot be used; null, with what
    /// went wrong, when neither can be.
    /// </summary>
    public static ModelRenderer? Create(out string? problem)
    {
        problem = null;
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
        foreach (DriverType driver in new[] { DriverType.Hardware, DriverType.Warp })
        {
            try
            {
                if (D3D11.D3D11CreateDevice(null, driver, DeviceCreationFlags.BgraSupport, levels, out ID3D11Device? device).Failure || device is null) continue;
                return new ModelRenderer(device, driver == DriverType.Warp);
            }
            catch (Exception failure) when (failure is SharpGen.Runtime.SharpGenException or DllNotFoundException or InvalidOperationException)
            {
                problem = failure.Message;
            }
        }
        problem ??= "Direct3D 11 is not available";
        return null;
    }

    private static string ShaderSource()
    {
        using Stream stream = typeof(ModelRenderer).Assembly.GetManifestResourceStream("ModelShaders.hlsl")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Compiles one of the shaders.</summary>
    public static ReadOnlyMemory<byte> Compile(string source, string entry, string profile) =>
        Compiler.Compile(source, entry, "ModelShaders.hlsl", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    // ---- the model ----

    /// <summary>
    /// Puts a model and its pictures on the graphics card, in place of the one before. Every part's vertices go in one
    /// buffer and its triangles in another; each picture becomes a texture with smaller copies for far away.
    /// </summary>
    /// <param name="mesh">The model.</param>
    /// <param name="pictures">Each part's pictures, in the order of its parts.</param>
    public void Load(ModelMesh mesh, IReadOnlyList<MaterialPictures> pictures)
    {
        UnloadModel();
        low = mesh.Low;
        high = mesh.High;
        var allVertices = new List<ModelVertex>(mesh.Parts.Sum(part => part.Vertices.Length));
        var allIndexes = new List<uint>(mesh.Parts.Sum(part => part.Indexes.Length));
        var textureOf = new Dictionary<Picture, ID3D11ShaderResourceView>(ReferenceEqualityComparer.Instance);
        for (int p = 0; p < mesh.Parts.Count; p++)
        {
            ModelPart part = mesh.Parts[p];
            uint first = (uint)allIndexes.Count, baseVertex = (uint)allVertices.Count;
            allVertices.AddRange(part.Vertices);
            foreach (int index in part.Indexes) allIndexes.Add(baseVertex + (uint)index);
            MaterialPictures maps = p < pictures.Count ? pictures[p] : new MaterialPictures(null, null, null);
            ID3D11ShaderResourceView? Texture(Picture? picture)
            {
                if (picture is null) return null;
                if (!textureOf.TryGetValue(picture, out ID3D11ShaderResourceView? view)) textureOf[picture] = view = Upload(picture);
                return view;
            }
            parts.Add(new DrawnPart(first, (uint)part.Indexes.Length, [Texture(maps.Colour), Texture(maps.Normal), Texture(maps.Surface)], part.NodeBase));
        }
        if (allVertices.Count == 0) return;
        vertices = device.CreateBuffer(allVertices.ToArray(), BindFlags.VertexBuffer, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
        indexes = device.CreateBuffer(allIndexes.ToArray(), BindFlags.IndexBuffer, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
        modelBytes += (long)allVertices.Count * ModelVertex.Size + allIndexes.Count * 4L;
        modelResources.Add(vertices);
        modelResources.Add(indexes);
        MakeGrid();
    }

    /// <summary>How far apart the ground grid's lines are, in metres.</summary>
    private const int GridSquare = 6;

    /// <summary>
    /// The ground grid's lines: <see cref="GridSquare"/> metres apart, out to two and a half times the model's size from
    /// its middle (16 metres at the least), each given in metres from the grid's middle with 1 for every fifth line.
    /// </summary>
    private void MakeGrid()
    {
        int reach = Math.Max(16, (int)Math.Ceiling((high - low).Length() / 2 * 2.5 / Metre));
        reach = (reach + GridSquare - 1) / GridSquare * GridSquare;
        var ends = new List<float>((reach + 1) * 12);
        for (int line = -reach; line <= reach; line += GridSquare)
        {
            float strong = line % (5 * GridSquare) == 0 ? 1 : 0;
            ends.AddRange([line, -reach, strong, line, reach, strong, -reach, line, strong, reach, line, strong]);
        }
        gridLines = device.CreateBuffer(ends.ToArray(), BindFlags.VertexBuffer, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
        gridPoints = (uint)(ends.Count / 3);
        gridReach = reach * Metre;
        modelBytes += ends.Count * 4L;
        modelResources.Add(gridLines);
    }

    /// <summary>The game's units in a metre (GameUnitsPerLBU, as ModelShaders.hlsl has it).</summary>
    private const float Metre = 215;

    /// <summary>A picture as a texture of the unit's model, with its smaller copies made on the card.</summary>
    private ID3D11ShaderResourceView Upload(Picture picture) => Upload(picture, modelResources, ref modelBytes);

    /// <summary>A picture as a texture, with its smaller copies made on the card, kept with these resources.</summary>
    private ID3D11ShaderResourceView Upload(Picture picture, List<IDisposable> kept, ref long bytes)
    {
        var description = new Texture2DDescription(Format.R8G8B8A8_UNorm, (uint)picture.Width, (uint)picture.Height, 1, 0,
            BindFlags.ShaderResource | BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips);
        ID3D11Texture2D texture = device.CreateTexture2D(description);
        context.UpdateSubresource(picture.Rgba, texture, 0, (uint)picture.Width * 4, 0, null);
        ID3D11ShaderResourceView view = device.CreateShaderResourceView(texture, null);
        context.GenerateMips(view);
        kept.Add(texture);
        kept.Add(view);
        // a texture with its smaller copies takes a third more
        bytes += (long)picture.Width * picture.Height * 4 * 4 / 3;
        return view;
    }

    private void UnloadModel()
    {
        foreach (IDisposable resource in modelResources) resource.Dispose();
        modelResources.Clear();
        parts.Clear();
        vertices = indexes = gridLines = null;
        gridPoints = 0;
        modelBytes = 0;
    }

    // ---- the picture ----

    /// <summary>Makes the picture this many pixels; does nothing when it already is.</summary>
    public void Resize(int pixelWidth, int pixelHeight)
    {
        pixelWidth = Math.Max(16, pixelWidth);
        pixelHeight = Math.Max(16, pixelHeight);
        if (pixelWidth == width && pixelHeight == height && Picture is not null) return;
        width = pixelWidth;
        height = pixelHeight;
        DisposeTargets();
        colour = device.CreateTexture2D(new Texture2DDescription(LightFormat, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, samples, 0, ResourceOptionFlags.None));
        depth = device.CreateTexture2D(new Texture2DDescription(Format.D32_Float, (uint)width, (uint)height, 1, 1, BindFlags.DepthStencil, ResourceUsage.Default, CpuAccessFlags.None, samples, 0, ResourceOptionFlags.None));
        resolved = device.CreateTexture2D(new Texture2DDescription(LightFormat, (uint)width, (uint)height, 1, 1, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        // shared with Direct3D 9, it must also be readable by shaders
        screen = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, device9 is null ? BindFlags.RenderTarget : BindFlags.RenderTarget | BindFlags.ShaderResource,
            ResourceUsage.Default, CpuAccessFlags.None, 1, 0, device9 is null ? ResourceOptionFlags.None : ResourceOptionFlags.Shared));
        staging = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None));
        colourView = device.CreateRenderTargetView(colour, null);
        resolvedView = device.CreateShaderResourceView(resolved, null);
        screenView = device.CreateRenderTargetView(screen, null);
        depthView = device.CreateDepthStencilView(depth, null);
        int halfWidth = Math.Max(8, width / 2), halfHeight = Math.Max(8, height / 2);
        distance = device.CreateTexture2D(new Texture2DDescription(Format.R32_Float, (uint)halfWidth, (uint)halfHeight, 1, 1, BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        distanceDepth = device.CreateTexture2D(new Texture2DDescription(Format.D32_Float, (uint)halfWidth, (uint)halfHeight, 1, 1, BindFlags.DepthStencil, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        distanceStaging = device.CreateTexture2D(new Texture2DDescription(Format.R32_Float, (uint)halfWidth, (uint)halfHeight, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None));
        distanceView = device.CreateRenderTargetView(distance, null);
        distanceDepthView = device.CreateDepthStencilView(distanceDepth, null);
        bitmap = ShareWithWpf() ? null : new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    }

    /// <summary>
    /// Hands WPF the picture drawn, as a Direct3D 9 surface of the same memory on the graphics card; false (and nothing
    /// shared) when it cannot be.
    /// </summary>
    private bool ShareWithWpf()
    {
        if (device9 is null || screen is null) return false;
        try
        {
            using IDXGIResource resource = screen.QueryInterface<IDXGIResource>();
            IntPtr handle = resource.SharedHandle;
            sharedTexture = device9.CreateTexture((uint)width, (uint)height, 1, D9.Usage.RenderTarget, D9.Format.A8R8G8B8, D9.Pool.Default, ref handle);
            sharedSurface = sharedTexture.GetSurfaceLevel(0);
            if (image is null)
            {
                image = new D3DImage();
                image.IsFrontBufferAvailableChanged += FrontBufferChanged;
            }
            ShowSharedSurface();
            return true;
        }
        catch (Exception failure) when (failure is SharpGen.Runtime.SharpGenException or InvalidOperationException or ArgumentException)
        {
            ReleaseShared();
            image = null;
            return false;
        }
    }

    /// <summary>Gives WPF the shared surface to show (WPF copies it in software when it cannot draw on the card).</summary>
    private void ShowSharedSurface()
    {
        if (image is null || sharedSurface is null) return;
        image.Lock();
        image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, sharedSurface.NativePointer, true);
        image.Unlock();
    }

    private void FrontBufferChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (image?.IsFrontBufferAvailable != true) return;
        ShowSharedSurface();
        PictureBack?.Invoke();
    }

    /// <summary>Lets go of the shared surface, WPF first.</summary>
    private void ReleaseShared()
    {
        if (image is not null)
        {
            image.Lock();
            image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
            image.Unlock();
        }
        sharedSurface?.Dispose();
        sharedTexture?.Dispose();
        sharedSurface = null;
        sharedTexture = null;
    }

    /// <summary>The picture as last drawn, read back from the graphics card, to keep (Copy Picture); null before the first frame.</summary>
    public BitmapSource? Snapshot()
    {
        if (screen is null || staging is null) return null;
        context.CopyResource(staging, screen);
        MappedSubresource mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            BitmapSource copy = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, mapped.DataPointer, (int)mapped.RowPitch * height, (int)mapped.RowPitch);
            copy.Freeze();
            return copy;
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }

    /// <summary>
    /// Draws a frame into <see cref="Picture"/>: the backdrop, the floor (after the picture of the model
    /// from above that its shadow is made from), then the model, each part with its own textures, its triangles' lines
    /// and the ground grid, then the effects, all as light; then that light turned into the screen's colours.
    /// </summary>
    /// <param name="view">The camera and what changes from frame to frame.</param>
    /// <param name="pose">Where each node has moved to (up to <see cref="MostNodes"/>).</param>
    /// <param name="drawEffects">Draws the effects (driving dust) over the model, before the frame is shown; null for none.</param>
    public void Draw(in FrameView view, Matrix4x4[] pose, Action<ID3D11DeviceContext>? drawEffects = null)
    {
        if (Picture is null || colourView is null || depthView is null) return;
        // the floor is seen only from above: from under it, it would hide the model
        bool floor = ShowsFloor && vertices is not null && view.Eye.Z > low.Z + view.Moved.Z;
        FloorConstants ground = FloorOf(view.Moved);
        context.UpdateSubresource(ref ground, floorBuffer, 0, 0, 0, null);
        if (floor) DrawShadowPicture(view, pose, ground);
        SetUp(view, pose, width, height);
        context.OMSetRenderTargets(colourView, depthView);
        context.ClearDepthStencilView(depthView, DepthStencilClearFlags.Depth, 1, 0);
        DrawBackdrop();
        if (floor) DrawFloor();
        DrawModel(modelPixel);
        if (ShowsWireframe) DrawWireframe();
        if (ShowsGrid) DrawGrid();
        drawEffects?.Invoke(context);
        context.ResolveSubresource(resolved!, 0, colour!, 0, LightFormat);
        context.OMSetRenderTargets(screenView!, null);
        context.OMSetDepthStencilState(depthOff);
        context.OMSetBlendState(null);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(backdropVertex);
        context.PSSetShader(screenPixel);
        context.PSSetShaderResource(0, resolvedView!);
        context.Draw(3, 0);
        context.PSSetShaderResource(0, null!);
        // Tuned for speed: WPF shows the shared picture straight from the graphics card, so nothing is copied back; the
        // lock waits only for WPF to finish showing the last frame (3.4 ms on the VM's card)
        if (image is not null)
        {
            context.Flush();
            image.Lock();
            image.AddDirtyRect(new Int32Rect(0, 0, width, height));
            image.Unlock();
            return;
        }
        context.CopyResource(staging!, screen!);
        MappedSubresource mapped = context.Map(staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            bitmap!.WritePixels(new Int32Rect(0, 0, width, height), mapped.DataPointer, (int)mapped.RowPitch * height, (int)mapped.RowPitch);
        }
        finally
        {
            context.Unmap(staging!, 0);
        }
    }

    /// <summary>
    /// The model's distance from the eye at every pixel of a picture half the size, as a share of
    /// <see cref="FrameView.Far"/> (1 where there is no model), to tell which marks it hides.
    /// </summary>
    public (float[] Distances, int Width, int Height) Distances(in FrameView view, Matrix4x4[] pose)
    {
        int halfWidth = Math.Max(8, width / 2), halfHeight = Math.Max(8, height / 2);
        var result = new float[halfWidth * halfHeight];
        if (distanceView is null || distanceDepthView is null) return (result, halfWidth, halfHeight);
        SetUp(view, pose, halfWidth, halfHeight);
        context.OMSetRenderTargets(distanceView, distanceDepthView);
        context.ClearRenderTargetView(distanceView, new Color4(1, 1, 1, 1));
        context.ClearDepthStencilView(distanceDepthView, DepthStencilClearFlags.Depth, 1, 0);
        DrawModel(distancePixel);
        context.CopyResource(distanceStaging!, distance!);
        MappedSubresource mapped = context.Map(distanceStaging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            for (int row = 0; row < halfHeight; row++)
                Marshal.Copy(mapped.DataPointer + row * (int)mapped.RowPitch, result, row * halfWidth, halfWidth);
        }
        finally
        {
            context.Unmap(distanceStaging!, 0);
        }
        return (result, halfWidth, halfHeight);
    }

    private void SetUp(in FrameView view, Matrix4x4[] pose, int pixelWidth, int pixelHeight)
    {
        var frame = new FrameConstants
        {
            ViewProjection = view.ViewProjection,
            Eye = view.Eye,
            TrackSlide = view.TrackSlide,
            Far = view.Far,
            Size = new Vector2(pixelWidth, pixelHeight),
            // a sliver of the model's size: enough to lift the lines off its faces, too little to show through it
            WireLift = (high - low).Length() / 2 * 0.004f,
            Ground = low.Z + view.Moved.Z,
        };
        context.UpdateSubresource(ref frame, frameBuffer, 0, 0, 0, null);
        posed = pose;
        PoseFrom(0);
        context.RSSetViewport(0, 0, pixelWidth, pixelHeight, 0, 1);
        context.RSSetState(bothSides);
        context.VSSetConstantBuffer(0, frameBuffer);
        context.VSSetConstantBuffer(2, poseBuffer);
        context.PSSetConstantBuffer(0, frameBuffer);
        context.PSSetConstantBuffer(1, materialBuffer);
        context.PSSetSampler(0, smooth);
    }

    /// <summary>The pose being drawn: where each node of the model has moved to.</summary>
    private Matrix4x4[] posed = [];

    /// <summary>The node the shaders' pose starts at now, as a part's vertices count their nodes from it.</summary>
    private int poseFrom;

    /// <summary>A whole pose buffer's worth of nodes, for a pose with fewer nodes than that left from where it starts.</summary>
    private readonly Matrix4x4[] poseScratch = new Matrix4x4[MostNodes];

    /// <summary>
    /// Gives the shaders the pose from this node on: as many nodes as they hold. The copy always fills the whole buffer
    /// (<see cref="MostNodes"/> nodes), so fewer nodes than that are copied through a full-sized array first: copied
    /// straight from a shorter one, it read past the array's end and closed the app (a 3D Model step's Bradley, turned).
    /// </summary>
    private void PoseFrom(int node)
    {
        poseFrom = node;
        if (node >= posed.Length) return;
        int count = Math.Min(posed.Length - node, MostNodes);
        if (count == MostNodes)
        {
            context.UpdateSubresource(posed.AsSpan(node, MostNodes), poseBuffer, 0, 0, 0, null);
            return;
        }
        posed.AsSpan(node, count).CopyTo(poseScratch);
        poseScratch.AsSpan(count).Fill(Matrix4x4.Identity);
        context.UpdateSubresource(poseScratch.AsSpan(), poseBuffer, 0, 0, 0, null);
    }

    private void DrawBackdrop()
    {
        context.OMSetDepthStencilState(depthOff);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(backdropVertex);
        context.PSSetShader(backdropPixel);
        context.Draw(3, 0);
    }

    /// <summary>
    /// The floor under the model and the picture of it from above that its shadow is made from: the model's middle
    /// (moved as it drives), its lowest point, how far the floor reaches (past the farthest distance drawn), the
    /// picture's middle and half width (the model's widest half, with room for the shadow's fall and blur), the model's
    /// height, the lamp's height (a little over the model's size, so its pool of light is a little larger than the
    /// model: chosen by eye), and the ground grid's middle (on the line nearest the model's) and reach.
    /// </summary>
    private FloorConstants FloorOf(Vector3 moved)
    {
        Vector3 middle = (low + high) / 2 + moved, size = high - low;
        float radius = size.Length() / 2;
        // the shadow falls away from the key light: the picture is moved that way by half the model's height's fall
        Vector2 fall = ShadowFall * size.Z;
        float half = Math.Max(size.X, size.Y) / 2 + size.Z * 0.5f + Math.Max(Math.Abs(fall.X), Math.Abs(fall.Y)) / 2 + radius * 0.05f;
        return new FloorConstants
        {
            Place = new Vector4(middle.X, middle.Y, low.Z + moved.Z, radius * 12),
            Shadow = new Vector4(middle.X + fall.X / 2, middle.Y + fall.Y / 2, half, Math.Max(1, size.Z)),
            Lamp = radius * 1.1f,
            Grid = new Vector3(MathF.Round(middle.X / (GridSquare * Metre)) * GridSquare * Metre, MathF.Round(middle.Y / (GridSquare * Metre)) * GridSquare * Metre, gridReach),
        };
    }

    /// <summary>
    /// How far along the floor a point's shadow falls for each unit of its height: away from the model's key light
    /// (keyLight in ModelShaders.hlsl, which points towards the light).
    /// </summary>
    private static readonly Vector2 ShadowFall = new(0.35f / 0.8f, 0.5f / 0.8f);

    /// <summary>
    /// Draws the model seen from straight above into the shadow's picture, keeping the lowest part over each point,
    /// then makes its smaller copies.
    /// </summary>
    private void DrawShadowPicture(in FrameView view, Matrix4x4[] pose, in FloorConstants floor)
    {
        // each point is moved along the floor as far as its shadow falls, for its height above the floor
        float half = floor.Shadow.Z;
        var above = new Matrix4x4(
            1 / half, 0, 0, 0,
            0, 1 / half, 0, 0,
            ShadowFall.X / half, ShadowFall.Y / half, 0, 0,
            (-floor.Shadow.X - floor.Place.Z * ShadowFall.X) / half, (-floor.Shadow.Y - floor.Place.Z * ShadowFall.Y) / half, 0.5f, 1);
        SetUp(view with { ViewProjection = above }, pose, (int)ShadowSize, (int)ShadowSize);
        context.PSSetConstantBuffer(3, floorBuffer);
        context.OMSetRenderTargets(shadowTarget, null);
        context.ClearRenderTargetView(shadowTarget, new Color4(1, 1, 1, 1));
        context.OMSetBlendState(lowestKept);
        DrawModel(shadowPixel);
        context.OMSetBlendState(null);
        context.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        context.GenerateMips(shadowView);
    }

    /// <summary>
    /// Draws the floor under everything, keeping no distance: the model is above it anyway, and smoke at ground level
    /// would be cut off along a hard line where it dips below the floor.
    /// </summary>
    private void DrawFloor()
    {
        context.OMSetDepthStencilState(depthOff);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(floorVertex);
        context.VSSetConstantBuffer(3, floorBuffer);
        context.PSSetShader(floorPixel);
        context.PSSetConstantBuffer(3, floorBuffer);
        context.PSSetShaderResource(0, shadowView);
        context.PSSetSampler(1, clamped);
        context.Draw(6, 0);
        context.PSSetShaderResource(0, null!);
    }

    /// <summary>The model's triangles' edges over it, as it is posed, lifted a little towards the eye.</summary>
    private void DrawWireframe()
    {
        if (vertices is null || indexes is null) return;
        context.RSSetState(wireframe);
        context.OMSetBlendState(seeThrough);
        context.OMSetDepthStencilState(depthRead);
        context.IASetInputLayout(layout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.IASetVertexBuffer(0, vertices, ModelVertex.Size, 0);
        context.IASetIndexBuffer(indexes, Format.R32_UInt, 0);
        context.VSSetShader(wireVertex);
        context.PSSetShader(wirePixel);
        foreach (DrawnPart part in parts)
        {
            if (part.NodeBase != poseFrom) PoseFrom(part.NodeBase);
            context.DrawIndexed(part.IndexCount, part.FirstIndex, 0);
        }
        context.RSSetState(bothSides);
        context.OMSetBlendState(null);
    }

    /// <summary>The ground grid's lines on the floor, behind the model where it hides them.</summary>
    private void DrawGrid()
    {
        if (gridLines is null) return;
        context.OMSetBlendState(seeThrough);
        context.OMSetDepthStencilState(depthRead);
        context.IASetInputLayout(gridLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.LineList);
        context.IASetVertexBuffer(0, gridLines, 12, 0);
        context.VSSetShader(gridVertex);
        context.VSSetConstantBuffer(3, floorBuffer);
        context.PSSetShader(gridPixel);
        context.PSSetConstantBuffer(3, floorBuffer);
        context.Draw(gridPoints, 0);
        context.OMSetBlendState(null);
    }

    /// <summary>The model's parts, each in its pose from its own node on, with its own textures.</summary>
    private void DrawModel(ID3D11PixelShader pixelShader)
    {
        if (vertices is null || indexes is null) return;
        context.OMSetDepthStencilState(depthOn);
        context.IASetInputLayout(layout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.IASetVertexBuffer(0, vertices, ModelVertex.Size, 0);
        context.IASetIndexBuffer(indexes, Format.R32_UInt, 0);
        context.VSSetShader(modelVertex);
        context.PSSetShader(pixelShader);
        foreach (DrawnPart part in parts)
        {
            var material = new MaterialConstants { Has = (part.Maps[0] is null ? 0 : 1) | (part.Maps[1] is null ? 0 : 2) | (part.Maps[2] is null ? 0 : 4) };
            context.UpdateSubresource(ref material, materialBuffer, 0, 0, 0, null);
            context.PSSetShaderResources(0, part.Maps!);
            if (part.NodeBase != poseFrom) PoseFrom(part.NodeBase);
            context.DrawIndexed(part.IndexCount, part.FirstIndex, 0);
        }
    }

    private void DisposeTargets()
    {
        ReleaseShared();
        foreach (IDisposable? target in new IDisposable?[] { colourView, screenView, resolvedView, depthView, colour, depth, resolved, screen, staging, distanceView, distanceDepthView, distance, distanceDepth, distanceStaging })
            target?.Dispose();
        colourView = screenView = null;
        resolvedView = null;
        depthView = null;
        distanceView = null;
        distanceDepthView = null;
        colour = depth = resolved = screen = staging = distance = distanceDepth = distanceStaging = null;
    }

    /// <summary>Lets go of everything on the graphics card.</summary>
    public void Dispose()
    {
        UnloadModel();
        DisposeTargets();
        width = height = 0;
        if (image is not null) image.IsFrontBufferAvailableChanged -= FrontBufferChanged;
        image = null;
        bitmap = null;
        device9?.Dispose();
        direct3D9?.Dispose();
        foreach (IDisposable resource in new IDisposable[] { layout, gridLayout, frameBuffer, materialBuffer, poseBuffer, floorBuffer, bothSides, wireframe, smooth, clamped, depthOn, depthOff, depthRead,
            lowestKept, seeThrough, shadowTarget, shadowView, shadow, modelVertex, backdropVertex, floorVertex, gridVertex, wireVertex, modelPixel, distancePixel, backdropPixel, screenPixel,
            floorPixel, shadowPixel, gridPixel, wirePixel })
            resource.Dispose();
        context.ClearState();
        context.Dispose();
        device.Dispose();
    }
}
