// Suspended particle motes for the underwater cave.
//
// A dedicated shader rather than URP/Particles/Unlit because that shader's transparency needs its
// ShaderGUI to set blend keywords, render queue and blend factors, none of which run when a material
// is created from an editor script. This is deterministic and additionally fades motes with the
// zone's fog so they never float as bright dots in front of a dark Z4 wall.
Shader "Varco/Underwater/Motes"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.62, 0.92, 1.0, 0.42)
        _SoftEdge("Soft Edge", Range(0.5, 8)) = 2.2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "UnderwaterMotes"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
                float viewDistance : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _SoftEdge;
            CBUFFER_END

            // Set by UnderwaterZoneDirector. Motes cannot rely on RenderSettings fog because the
            // director turns it off while the screen pass is active, and the screen pass itself keys
            // off scene depth, which transparent motes never write.
            float4 _Underwater_Extinction;
            float _Underwater_Strength;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.uv = input.uv;
                output.color = input.color;
                output.viewDistance = length(positionInputs.positionWS - _WorldSpaceCameraPos);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Round soft dot from the quad UVs, so no particle texture asset is required.
                half distanceFromCentre = saturate(length(input.uv - 0.5h) * 2.0h);
                half mote = pow(saturate(1.0h - distanceFromCentre), _SoftEdge);

                half3 color = _BaseColor.rgb * input.color.rgb;
                half alpha = mote * _BaseColor.a * input.color.a;

                // Fade out with distance rather than tinting towards the water colour: an unattenuated
                // mote in front of a far wall reads as a bright speck instead of suspended sediment.
                // Uses the green channel of the same extinction the screen pass applies, so motes
                // disappear at the same distance the rock does.
                float visibilityExtinction = _Underwater_Strength > 0.0 ? _Underwater_Extinction.g : 0.0;
                alpha *= saturate(exp(-visibilityExtinction * input.viewDistance));

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
