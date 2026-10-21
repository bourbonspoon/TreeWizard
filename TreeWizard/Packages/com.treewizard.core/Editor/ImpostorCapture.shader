// Renders one impostor frame for ImpostorBaker: colour and coverage to the first target, object-space
// normal and depth toward the viewer to the second. Unlit and self-contained, so the atlas holds the
// surface itself and the Impostor shader lights it at runtime. Reads the same properties as the Tree
// shader and URP Lit (_BaseMap, _BaseColor, _Cutoff).
Shader "Hidden/TreeWizard/ImpostorCapture"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff (below 0: opaque)", Float) = -1
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            float4x4 _TWCaptureObjectToClip;
            float4x4 _TWCaptureObjectToRoot;
            float4 _TWCaptureBounds;    // xyz: centre, w: radius, in the prefab root's space
            float4 _TWCaptureDirection; // xyz: from the centre toward the viewer

            Texture2D _BaseMap;
            SamplerState sampler_BaseMap;
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Cutoff;

            struct Attributes
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 rootPosition : TEXCOORD2;
            };

            struct Targets
            {
                float4 albedo : SV_Target0;
                float4 normal : SV_Target1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.position = mul(_TWCaptureObjectToClip, float4(input.position.xyz, 1.0));
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.normal = mul((float3x3)_TWCaptureObjectToRoot, input.normal);
                output.rootPosition = mul(_TWCaptureObjectToRoot, float4(input.position.xyz, 1.0)).xyz;
                return output;
            }

            Targets Fragment(Varyings input)
            {
                float4 colour = _BaseMap.Sample(sampler_BaseMap, input.uv) * _BaseColor;
                if (_Cutoff >= 0.0)
                    clip(colour.a - _Cutoff);

                // Two-sided like the Tree shader: the side of the triangle being looked at decides which way
                // the normal points. The facing comes from the triangle itself, which doesn't depend on the
                // platform's winding or y-flip conventions.
                float3 view = _TWCaptureDirection.xyz;
                float3 face = cross(ddx(input.rootPosition), ddy(input.rootPosition));
                if (dot(face, view) < 0.0)
                    face = -face;
                float3 normal = normalize(input.normal);
                if (dot(normal, face) < 0.0)
                    normal = -normal;

                float depth = dot(input.rootPosition - _TWCaptureBounds.xyz, view) / (2.0 * _TWCaptureBounds.w) + 0.5;
                Targets output;
                output.albedo = float4(colour.rgb, 1.0);
                output.normal = float4(normal * 0.5 + 0.5, saturate(depth));
                return output;
            }
            ENDHLSL
        }
    }
}
