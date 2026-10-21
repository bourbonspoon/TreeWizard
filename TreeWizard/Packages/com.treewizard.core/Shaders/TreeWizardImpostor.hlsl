#ifndef TREEWIZARD_IMPOSTOR_INCLUDED
#define TREEWIZARD_IMPOSTOR_INCLUDED

// Octahedral impostors for TreeWizard trees, used by the Impostor shader graph's Custom Function nodes.
//
// The atlas holds Frames x Frames orthographic views of the tree from directions spread over the upper
// hemisphere (hemi-octahedral mapping), each frame framing the tree's bounding sphere. The albedo atlas
// has colour and coverage, the normal atlas has object-space normals (rgb) and depth toward the viewer (a).
// ImpostorBaker writes them; the frame directions and bases here must match its C# copies.
//
// The billboard faces whatever is rendering it: the camera, or in the shadow pass the light, since URP
// sets the light's view matrix there. So shadows show the tree's silhouette as seen from the sun.

// Fraction of the radius the shadow caster is pushed away from the light. Without it the caster and the
// camera-facing billboard cross at the centre and the tree shadows half of itself in a hard line.
#define TW_IMPOSTOR_SHADOW_PUSH 0.35

float2 TWHemiOctEncode(float3 d)
{
    d /= abs(d.x) + abs(d.y) + abs(d.z);
    return float2(d.x + d.z, d.x - d.z) * 0.5 + 0.5;
}

float3 TWHemiOctDecode(float2 uv)
{
    float2 p = uv * 2.0 - 1.0;
    float x = (p.x + p.y) * 0.5;
    float z = (p.x - p.y) * 0.5;
    return normalize(float3(x, 1.0 - abs(x) - abs(z), z));
}

// Right and up of a frame looking back along -f.
void TWImpostorBasis(float3 f, out float3 right, out float3 up)
{
    float3 reference = abs(f.y) > 0.999 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
    right = normalize(cross(reference, f));
    up = cross(f, right);
}

// Object-space direction from the tree toward whatever is rendering it.
float3 TWImpostorViewDirection(float3 centerOS)
{
    float3 directionWS;
    if (UNITY_MATRIX_P[3][3] > 0.5) // orthographic, which includes directional light shadows
    {
        directionWS = UNITY_MATRIX_V[2].xyz;
    }
    else
    {
        // From the view matrix rather than _WorldSpaceCameraPos, which stays the camera's in shadow passes.
        float3x3 rotation = (float3x3)UNITY_MATRIX_V;
        float3 eyeWS = -mul(transpose(rotation), UNITY_MATRIX_V._m03_m13_m23);
        directionWS = eyeWS - TransformObjectToWorld(centerOS);
    }
    return normalize(TransformWorldToObjectDir(directionWS, false));
}

// The impostor mesh is a quad with corners at centre +- radius in XY. This turns it to face the viewer.
void TreeWizardImpostorVertex_float(float3 PositionOS, float4 Bounds, out float3 Position, out float3 Normal, out float3 Tangent)
{
    float3 center = Bounds.xyz;
    float radius = Bounds.w;
    float2 corner = (PositionOS.xy - center.xy) / radius;
    float3 toViewer = TWImpostorViewDirection(center);
    float3 right, up;
    TWImpostorBasis(toViewer, right, up);
    // The frame basis's right is the viewer's left in Unity's left-handed space, so mirror x to keep the
    // quad's winding toward the viewer. The atlas lookup doesn't depend on how the quad is oriented.
    Position = center + (-corner.x * right + corner.y * up) * radius;
#if defined(SHADERPASS) && defined(SHADERPASS_SHADOWCASTER) && (SHADERPASS == SHADERPASS_SHADOWCASTER)
    Position -= toViewer * radius * TW_IMPOSTOR_SHADOW_PUSH;
#endif
    Normal = toViewer;
    Tangent = -right;
}

// Where a point, relative to the centre, lands in a frame: 0-1 across the frame.
float2 TWImpostorFrameUV(float3 p, float3 right, float3 up, float radius)
{
    return float2(dot(p, right), dot(p, up)) / (2.0 * radius) + 0.5;
}

void TWImpostorSample(UnityTexture2D albedo, UnityTexture2D normals, float frames, float2 cell, float2 uv, float2 dx, float2 dy,
    out float4 colour, out float4 normal)
{
    float inset = 0.5 * frames * albedo.texelSize.x; // half a texel of the frame, so samples never reach a neighbour
    float2 atlasUV = (cell + clamp(uv, inset, 1.0 - inset)) / frames;
    colour = SAMPLE_TEXTURE2D_GRAD(albedo.tex, albedo.samplerstate, atlasUV, dx, dy);
    normal = SAMPLE_TEXTURE2D_GRAD(normals.tex, normals.samplerstate, atlasUV, dx, dy);
    if (any(uv != saturate(uv)))
        colour.a = 0.0;
}

// Blends the three frames nearest the view direction. The nearest frame's depth places the visible surface,
// which is then projected into the other two, so the blend doesn't ghost.
void TreeWizardImpostorFragment_float(float3 PositionOS, UnityTexture2D Albedo, UnityTexture2D Normals, float Frames, float4 Bounds,
    out float3 BaseColor, out float3 NormalOS, out float Alpha)
{
    float3 center = Bounds.xyz;
    float radius = Bounds.w;
    float last = Frames - 1.0;
    float3 view = TWImpostorViewDirection(center);
    float3 above = float3(view.x, max(view.y, 0.0), view.z); // the atlas only covers the upper hemisphere
    above = dot(above, above) > 1e-6 ? normalize(above) : float3(1.0, 0.0, 0.0);

    float2 grid = TWHemiOctEncode(above) * last;
    float2 base = min(floor(grid), last - 1.0);
    float2 f = grid - base;
    float2 cell0, cell1, cell2;
    float3 weights;
    if (f.x + f.y <= 1.0)
    {
        cell0 = base;
        cell1 = base + float2(1.0, 0.0);
        cell2 = base + float2(0.0, 1.0);
        weights = float3(1.0 - f.x - f.y, f.x, f.y);
    }
    else
    {
        cell0 = base + 1.0;
        cell1 = base + float2(1.0, 0.0);
        cell2 = base + float2(0.0, 1.0);
        weights = float3(f.x + f.y - 1.0, 1.0 - f.y, 1.0 - f.x);
    }
    // Strongest frame first.
    if (weights.y > weights.x && weights.y >= weights.z)
    {
        float2 c = cell0; cell0 = cell1; cell1 = c;
        weights = weights.yxz;
    }
    else if (weights.z > weights.x)
    {
        float2 c = cell0; cell0 = cell2; cell2 = c;
        weights = weights.zyx;
    }

    float3 dir0 = TWHemiOctDecode(cell0 / last);
    float3 dir1 = TWHemiOctDecode(cell1 / last);
    float3 dir2 = TWHemiOctDecode(cell2 / last);
    float3 right0, up0, right1, up1, right2, up2;
    TWImpostorBasis(dir0, right0, up0);
    TWImpostorBasis(dir1, right1, up1);
    TWImpostorBasis(dir2, right2, up2);

    // Follow the view ray from the billboard to the strongest frame's plane.
    float3 p = PositionOS - center;
    float3 onPlane = p - view * (dot(p, dir0) / max(dot(view, dir0), 0.05));
    float2 uv0 = TWImpostorFrameUV(onPlane, right0, up0, radius);
    float2 dx = ddx(uv0) / Frames;
    float2 dy = ddy(uv0) / Frames;

    float4 colour0, normal0, colour1, normal1, colour2, normal2;
    TWImpostorSample(Albedo, Normals, Frames, cell0, uv0, dx, dy, colour0, normal0);
    float3 surface = onPlane + dir0 * ((normal0.a - 0.5) * 2.0 * radius);
    TWImpostorSample(Albedo, Normals, Frames, cell1, TWImpostorFrameUV(surface, right1, up1, radius), dx, dy, colour1, normal1);
    TWImpostorSample(Albedo, Normals, Frames, cell2, TWImpostorFrameUV(surface, right2, up2, radius), dx, dy, colour2, normal2);

    Alpha = weights.x * colour0.a + weights.y * colour1.a + weights.z * colour2.a;
    BaseColor = weights.x * colour0.rgb + weights.y * colour1.rgb + weights.z * colour2.rgb;
    float3 n = weights.x * (normal0.rgb * 2.0 - 1.0) + weights.y * (normal1.rgb * 2.0 - 1.0) + weights.z * (normal2.rgb * 2.0 - 1.0);
    NormalOS = dot(n, n) > 1e-6 ? normalize(n) : view;
}

#endif
