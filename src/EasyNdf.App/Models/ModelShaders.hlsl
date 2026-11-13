// The 3D model viewer's shaders: a model lit by a key light and a fill light with its colour, normal and surface maps,
// a dark backdrop, a floor lit by a lamp above with the model's shadow on it, a grid of 6-metre squares on the floor, the
// model's triangles drawn over it, the game's effects as particles, and the model's distance from the eye, read back to
// find which marks it hides. The model, backdrop, floor and effects are drawn as light, then turned into the screen's
// colours in one last pass. Matrices are laid out as
// System.Numerics lays them out: a point times the matrix.

cbuffer Frame : register(b0)
{
    row_major float4x4 ViewProjection;
    float3 Eye;
    float TrackSlide;      // how far track strips' textures have slid, as the tank drives
    float Far;             // the farthest distance drawn, for the distance picture
    float2 Size;           // the picture's size in pixels, for the backdrop
    float WireLift;        // how far the model's triangles are drawn towards the eye, so its faces do not hide them
    float Ground;          // the ground's height, under the model as it drives
    float3 FrameUnused;
};

cbuffer Material : register(b1)
{
    int Has;               // which maps the part has: 1 colour, 2 normal, 4 surface
    float3 Unused;
};

cbuffer Pose : register(b2)
{
    row_major float4x4 Nodes[256];   // where each node has moved to: a turret turned, a wheel rolled
};

Texture2D ColourMap : register(t0);
Texture2D NormalMap : register(t1);
Texture2D SurfaceMap : register(t2);
SamplerState Smooth : register(s0);

struct VertexIn
{
    float3 Position : POSITION;
    float2 Place : TEXCOORD0;
    float3 Normal : NORMAL;
    float3 Tangent : TANGENT;
    float3 Binormal : BINORMAL;
    uint2 NodeAndTrack : BLENDINDICES;
    uint4 MoreNodes : BLENDINDICES1;   // a soldier's skin follows up to four bones
    float4 Weights : BLENDWEIGHT;      // how much it follows each; all 0 for a vertex that follows its one node
};

struct PixelIn
{
    float4 Clip : SV_Position;
    float3 World : POSITION1;
    float2 Place : TEXCOORD0;
    float3 Normal : NORMAL;
    float3 Tangent : TANGENT;
    float3 Binormal : BINORMAL;
};

PixelIn ModelVertex(VertexIn input)
{
    float4 weights = input.Weights;
    float total = weights.x + weights.y + weights.z + weights.w;
    float4x4 moved = Nodes[input.NodeAndTrack.x];
    if (total > 0)
        moved = (moved * weights.x + Nodes[input.MoreNodes.x] * weights.y + Nodes[input.MoreNodes.y] * weights.z + Nodes[input.MoreNodes.z] * weights.w) / total;
    float4x4 node = moved;
    PixelIn output;
    output.World = mul(float4(input.Position, 1), node).xyz;
    output.Normal = mul(input.Normal * 2 - 1, (float3x3)node);
    output.Tangent = mul(input.Tangent * 2 - 1, (float3x3)node);
    output.Binormal = mul(input.Binormal * 2 - 1, (float3x3)node);
    output.Place = input.Place + float2(0, input.NodeAndTrack.y == 1 ? TrackSlide : 0);
    output.Clip = mul(float4(output.World, 1), ViewProjection);
    return output;
}

// The tone curve: bright parts roll off instead of clipping, then back to the screen's gamma.
float3 ToScreen(float3 light)
{
    light = saturate(light / (light + 0.9) * 1.75);
    return pow(light, 1 / 2.2);
}

float4 ModelPixel(PixelIn input) : SV_Target
{
    float3 normal = normalize(input.Normal);
    float3 toEye = normalize(Eye - input.World);
    if ((Has & 2) != 0)
    {
        float2 lean = NormalMap.Sample(Smooth, input.Place).rg * 2 - 1;
        float3 mapped = input.Tangent * lean.x + input.Binormal * lean.y + normal * sqrt(saturate(1 - dot(lean, lean)));
        if (dot(mapped, mapped) > 0) normal = normalize(mapped);
    }
    if (dot(normal, toEye) < 0) normal = -normal;
    float3 albedo = float3(0.45, 0.47, 0.42);
    if ((Has & 1) != 0) albedo = pow(ColourMap.Sample(Smooth, input.Place).rgb, 2.2);
    float occlusion = 1, roughness = 0.7, metal = 0;
    if ((Has & 4) != 0)
    {
        float3 surface = SurfaceMap.Sample(Smooth, input.Place).rgb;
        occlusion = surface.r;
        roughness = surface.g;
        metal = surface.b;
    }
    float3 keyLight = normalize(float3(-0.35, -0.5, 0.8)), fillLight = normalize(float3(0.6, 0.5, 0.35));
    float key = max(0, dot(normal, keyLight)), fill = max(0, dot(normal, fillLight));
    float shine = pow(max(0, dot(normal, normalize(keyLight + toEye))), 4 + (1 - roughness) * 60) * (1 - roughness) * (0.25 + metal);
    float3 light = albedo * (0.30 * occlusion + 1.5 * key + 0.35 * fill * occlusion) * (1 - metal * 0.4) + shine;
    return float4(light, 1);
}

// The model's distance from the eye, as a share of the farthest distance drawn.
float DistancePixel(PixelIn input) : SV_Target
{
    return distance(input.World, Eye) / Far;
}

// The backdrop: one triangle over the whole picture.
float4 BackdropVertex(uint corner : SV_VertexID) : SV_Position
{
    float2 place = float2((corner << 1) & 2, corner & 2);
    return float4(place * float2(2, -2) + float2(-1, 1), 1, 1);
}

// The backdrop's own light, which the floor fades into far away, so no edge shows where it ends.
static const float3 BackdropLight = float3(0.012, 0.016, 0.014);

float4 BackdropPixel(float4 pixel : SV_Position) : SV_Target
{
    return float4(BackdropLight, 1);
}

// The floor the model stands on, lit by a lamp straight above it: a pool of light that fades out smoothly, and the
// model's shadow, sharp where the model nearly touches the floor and softer the higher up what casts it is (as a lamp
// of some size makes it). The shadow comes from a picture of the model seen from straight above (ShadowPixel), with
// smaller copies of it that blur it: each spot of floor reads the copy as blurred as its shadow should be.
cbuffer Floor : register(b3)
{
    float4 FloorPlace;     // the middle of the floor under the model (x, y), its height, and how far it reaches
    float4 FloorShadow;    // the middle of the shadow picture (x, y), its half width, the model's height
    float FloorLamp;       // the lamp's height above the floor
    float3 FloorGrid;      // the middle of the grid (x, y), on one of its lines, and how far its lines reach
};

Texture2D ShadowMap : register(t0);
SamplerState Clamped : register(s1);

// The shadow picture is this many pixels across.
static const float ShadowSize = 512;

// How a lamp above a point shows it from below: how high the lowest part of the model over it is (as a share of the
// model's height, 1 for none) and, in the second channel, 0 where the model is over it (1 where not). Drawn with the
// lowest kept, so their averages in the smaller copies tell how much of an area is covered and how high it is.
float2 ShadowPixel(PixelIn input) : SV_Target
{
    return float2(saturate((input.World.z - FloorPlace.z) / max(FloorShadow.w, 1)), 0);
}

struct FloorPixelIn
{
    float4 Clip : SV_Position;
    float3 World : POSITION1;
};

// Two triangles, a square of floor around the model.
FloorPixelIn FloorVertex(uint corner : SV_VertexID)
{
    static const float2 corners[6] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(-1, 1), float2(1, -1), float2(1, 1) };
    FloorPixelIn output;
    output.World = float3(FloorPlace.xy + corners[corner] * FloorPlace.w, FloorPlace.z);
    output.Clip = mul(float4(output.World, 1), ViewProjection);
    return output;
}

float4 FloorPixel(FloorPixelIn input) : SV_Target
{
    float2 across = input.World.xy - FloorPlace.xy;
    // a lamp lights the floor less the farther off it a point is, and more slantwise: no edge anywhere
    float lamp = FloorLamp * FloorLamp;
    float pool = pow(lamp / (lamp + dot(across, across)), 3);
    float2 place = float2(input.World.x - FloorShadow.x, FloorShadow.y - input.World.y) / FloorShadow.z * 0.5 + 0.5;
    // how high what casts the shadow is, on average around the point, gives how blurred the shadow is there
    float2 around = ShadowMap.SampleLevel(Clamped, place, 4);
    float covered = 1 - around.y;
    // (fading to none at the shadow's edge, where too little is covered to tell)
    float casterHeight = saturate((around.x - around.y) / max(covered, 0.02));
    float pixelWidth = 2 * FloorShadow.z / ShadowSize;
    // (three pixels at the least, and half the caster's height more for its height: the lamp's size, chosen by eye)
    float blur = 3 + casterHeight * FloorShadow.w * 0.5 / pixelWidth;
    // twelve points spread over the blur, each from a copy a little less blurred, average out the copies' blocks
    static const float2 disc[12] = { float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
        float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893), float2(0.507, 0.064),
        float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598) };
    float level = max(0, log2(blur) - 1.5), reach = blur / 2 / ShadowSize, open = 0;
    [unroll] for (int i = 0; i < 12; i++) open += ShadowMap.SampleLevel(Clamped, place + disc[i] * reach, level).y;
    float shade = 1 - open / 12;
    float3 light = BackdropLight * (1 - 0.6 * shade) + float3(0.08, 0.097, 0.089) * pool * (1 - 0.9 * shade);
    return float4(light, 1);
}

// The ground grid: lines 6 metres apart on the floor around the model, every fifth one stronger, fading out before
// they end. Each line's ends are given in metres from the grid's middle, with 1 for a fifth line.
static const float GridMetre = 215;   // the game's units in a metre (GameUnitsPerLBU)

struct GridPixelIn
{
    float4 Clip : SV_Position;
    float2 Across : TEXCOORD0;
    float Strong : TEXCOORD1;
};

GridPixelIn GridVertex(float3 place : POSITION)
{
    GridPixelIn output;
    output.Across = place.xy * GridMetre;
    output.Strong = place.z;
    output.Clip = mul(float4(FloorGrid.xy + output.Across, FloorPlace.z + 1, 1), ViewProjection);
    return output;
}

float4 GridPixel(GridPixelIn input) : SV_Target
{
    float fade = 1 - smoothstep(0.45, 1, length(input.Across) / FloorGrid.z);
    return float4(float3(0.10, 0.17, 0.12), lerp(0.35, 0.8, input.Strong) * fade);
}

// The model's triangles, drawn over it a little towards the eye.
PixelIn WireVertex(VertexIn input)
{
    PixelIn output = ModelVertex(input);
    output.Clip = mul(float4(output.World + normalize(Eye - output.World) * WireLift, 1), ViewProjection);
    return output;
}

float4 WirePixel(PixelIn input) : SV_Target
{
    return float4(0.25, 0.7, 0.35, 0.2);
}

// The last pass: the light drawn turned into the screen's colours, with a speck of noise so smooth shades of dark
// show no steps.
Texture2D Drawn : register(t0);

float4 ScreenPixel(float4 pixel : SV_Position) : SV_Target
{
    float noise = frac(sin(dot(pixel.xy, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
    return float4(ToScreen(max(0, Drawn.Load(int3(pixel.xy, 0)).rgb)) + noise / 255, 1);
}

// The game's effects: each particle a picture facing the camera (built on the processor), from one frame of its
// texture's animation. Its own colour, lit as the model is, shows as much as its opacity, covering what is behind it
// that much (smoke of opacity 0.05 is a faint haze); the light it gives off is added whatever its opacity, so a
// fireball of opacity 0.3 glows fully but hides little. The ground hides what is below it, as the game's terrain hides
// dust and mud falling through it: nothing of a particle below the ground is drawn, from above or below; seen from
// above, it fades over a short way so dust and smoke lying on it have no hard edge.
Texture2DArray Frames : register(t0);

// how far behind a particle, along the line of sight, the ground must be for it to show fully (chosen by eye)
static const float GroundSoftness = 50;

struct ParticleIn
{
    float3 Position : POSITION;
    float2 Place : TEXCOORD0;
    float4 Colour : COLOR0;        // colour, and how much of it shows
    float3 LitGlowOpacity : TEXCOORD1;
    float Frame : TEXCOORD2;
};

struct ParticlePixel
{
    float4 Clip : SV_Position;
    float2 Place : TEXCOORD0;
    float4 Colour : COLOR0;
    float3 LitGlowOpacity : TEXCOORD1;
    float Frame : TEXCOORD2;
    float3 World : TEXCOORD3;
};

ParticlePixel ParticleVertex(ParticleIn input)
{
    ParticlePixel output;
    output.Clip = mul(float4(input.Position, 1), ViewProjection);
    output.Place = input.Place;
    output.Colour = input.Colour;
    output.LitGlowOpacity = input.LitGlowOpacity;
    output.Frame = input.Frame;
    output.World = input.Position;
    return output;
}

// How much of a particle's point the ground leaves showing, seen from above it: none below the ground, all once the
// ground is far enough behind it along the line of sight.
float AboveGround(float3 world)
{
    if (Eye.z <= Ground) return 1;
    float height = world.z - Ground;
    float behind = height * length(world - Eye) / max(Eye.z - world.z, 1e-3);
    return world.z >= Eye.z ? 1 : saturate(behind / GroundSoftness);
}

float4 ParticlePixelShader(ParticlePixel input) : SV_Target
{
    clip(input.World.z - Ground);
    float4 picture = Frames.Sample(Smooth, float3(input.Place, input.Frame));
    float shows = saturate(picture.a * input.Colour.a) * AboveGround(input.World);
    float covers = shows * input.LitGlowOpacity.z;
    float3 light = picture.rgb * input.Colour.rgb * (input.LitGlowOpacity.x * covers + input.LitGlowOpacity.y * shows);
    return float4(light, covers);
}
