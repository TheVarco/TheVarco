// Additive animated caustic pattern, ported from UnderwaterEffectDemo/Assets/Shaders/AnimatedCaustics.shader.
//
// Intended for quads laid over cave floors near openings. _Strength is multiplied by the global
// _Underwater_CausticStrength so caustic quads fade with the zone the camera is currently in.
Shader "Varco/Underwater/Caustics"
{
    Properties
    {
        _Color("Color", Color) = (0.35, 1.0, 0.78, 0.18)
        _Scale("Scale", Float) = 1.8
        _Speed("Speed", Float) = 0.32
        _Strength("Strength", Float) = 0.18
        [Toggle] _FollowZone("Follow Zone Strength", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+25" "RenderType" = "Transparent" }

        Pass
        {
            Name "UnderwaterCaustics"
            Blend SrcAlpha One
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
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Scale;
                float _Speed;
                float _Strength;
                float _FollowZone;
            CBUFFER_END

            float _Underwater_CausticStrength;
            float _Underwater_Strength;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * _Scale * 18.0;
                float a = sin(p.x + sin(p.y * 0.73 + _Time.y * _Speed) * 2.1);
                float b = sin(p.y * 1.13 + cos(p.x * 0.61 - _Time.y * _Speed * 1.27) * 2.0);
                float causticLine = pow(saturate(1.0 - abs(a + b) * 0.48), 9.0);

                float strength = _Strength;
                if (_FollowZone > 0.5)
                    strength *= _Underwater_CausticStrength * 38.0 * saturate(_Underwater_Strength);

                return half4(_Color.rgb, causticLine * _Color.a * strength * 5.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
