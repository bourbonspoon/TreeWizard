#ifndef TREEWIZARD_WIND_INCLUDED
#define TREEWIZARD_WIND_INCLUDED

// Wind for TreeWizard trees, used by the Tree shader graph's Custom Function node.
// Works in object space with the tree's base at the origin, so it needs no special vertex data
// and also moves baked prefabs and exported/imported FBX trees.

// Set by the TreeWizardWind component. xyz: world-space direction (unit length). w: strength, 0 = calm.
float4 _TreeWizardWind;
// x: speed multiplier for sway and gusts (0 means 1).
float4 _TreeWizardWindTiming;

void TreeWizardWind_float(float3 PositionOS, float Strength, float Flutter, out float3 Out)
{
    Out = PositionOS;
    float strength = _TreeWizardWind.w * Strength;
    if (strength <= 0.0)
        return;

    float3 direction = TransformWorldToObjectDir(_TreeWizardWind.xyz);
    float3 origin = GetObjectToWorldMatrix()._m03_m13_m23;
    float phase = dot(origin, float3(0.37, 0.0, 0.71)); // neighbouring trees don't move in lockstep
    float speed = _TreeWizardWindTiming.x > 0.0 ? _TreeWizardWindTiming.x : 1.0;
    float t = _TimeParameters.x * speed;

    // Whole tree: slow gusting sway that grows with height squared, so the base stays planted.
    float height = max(PositionOS.y, 0.0);
    float gust = 0.65 + 0.35 * sin(t * 0.6 + phase) * sin(t * 0.21 + phase * 1.3);
    float sway = gust + 0.15 * sin(t * 1.7 + phase);
    float bend = height * height * 0.004 * sway * strength;
    Out += direction * bend;
    Out.y -= bend * bend * 0.5 / max(height, 0.5); // keeps the trunk roughly the same length as it bends

    // Branches: the further from the trunk, the more they bob, out of phase across the crown.
    // Faded out over the first metre up, so roots and the base stay put on the ground.
    float reach = length(PositionOS.xz);
    float wave = sin(t * 2.1 + phase + reach * 0.8 + PositionOS.y * 0.5);
    Out += (float3(0.0, 0.6, 0.0) + direction * 0.4) * wave * reach * 0.02 * strength * saturate(PositionOS.y);

    // Leaves: a small, quick flutter with its own phase per vertex.
    float3 q = PositionOS * 4.0;
    float flutter = sin(t * 6.5 + q.x + q.y * 1.7 + q.z * 1.3);
    Out += float3(0.3, 1.0, 0.25) * flutter * 0.015 * Flutter * strength;
}

void TreeWizardWind_half(half3 PositionOS, half Strength, half Flutter, out half3 Out)
{
    float3 result;
    TreeWizardWind_float(PositionOS, Strength, Flutter, result);
    Out = result;
}

#endif
