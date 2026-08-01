// Screen-space underwater absorption, refraction and depth fog.
//
// This is the SINGLE source of underwater optics for opaque geometry. UnderwaterZoneDirector turns
// RenderSettings.fog OFF while this pass is active, because letting the per-material MixFog run as
// well fogs every pixel twice and then multiplies absorption over the already-fogged colour, which
// collapses the whole image towards the fog colour regardless of how the scene is lit.
//
// The model is the standard single-scattering one, evaluated per channel so red dies first:
//     transmittance = exp(-extinction * distance)
//     result        = sceneColour * transmittance + waterColour * (1 - transmittance)
//
// Driven entirely by global shader properties, so one material instance serves every cave zone.
// _Underwater_Strength is the master gate: it defaults to 0 and is only raised by an active director,
// which keeps this project-wide renderer feature inert in scenes with no underwater environment.
Shader "Varco/Underwater/FullScreen"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "UnderwaterFullScreen"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            // Vert and the _BlitTexture declaration come from Blit.hlsl; Core.hlsl goes first so
            // _ZBufferParams and _Time are already declared when it is pulled in.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // Global uniforms, deliberately not material properties. UnderwaterZoneDirector owns them.
            float4 _Underwater_WaterColor;   // linear in-scattered water colour
            float4 _Underwater_Extinction;   // xyz = per-metre per-channel extinction
            float _Underwater_Distortion;
            float _Underwater_DistortionSpeed;
            float _Underwater_Strength;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half3 sourceColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                if (_Underwater_Strength <= 0.0)
                    return half4(sourceColor, 1.0);

                float rawDepth = SampleSceneDepth(uv);

                // Pixels left at the far plane are cave openings and cleared background. The reference
                // art reads those as the brightest thing in frame, so they are refracted and lightly
                // hazed but never pushed towards the local water colour.
                #if UNITY_REVERSED_Z
                    bool isBackground = rawDepth < 1e-6;
                #else
                    bool isBackground = rawDepth > 1.0 - 1e-6;
                #endif

                float eyeDepth = isBackground ? 0.0 : LinearEyeDepth(rawDepth, _ZBufferParams);

                float2 waves;
                waves.x = sin(uv.y * 34.0 + _Time.y * _Underwater_DistortionSpeed)
                        + sin(uv.y * 71.0 - _Time.y * 0.41);
                waves.y = cos(uv.x * 29.0 - _Time.y * _Underwater_DistortionSpeed * 0.83)
                        + sin(uv.x * 63.0 + _Time.y * 0.37);

                // Refraction ramps in over the first few metres so geometry right at the lens does not
                // visibly swim, and openings still shimmer a little.
                float refractionMask = isBackground ? 0.6 : saturate(eyeDepth * 0.12);
                float2 refractedUv = saturate(uv + waves * _Underwater_Distortion * refractionMask);
                half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, refractedUv).rgb;

                if (!isBackground)
                {
                    // Break up the boundary slightly so dense zones do not show a clean iso-distance
                    // ring where the water closes in.
                    float grain = sin((uv.x + uv.y) * 42.0 + _Time.y * 0.7)
                                * sin((uv.x - uv.y) * 57.0 - _Time.y * 0.53);
                    float distance = max(0.0, eyeDepth * (1.0 + grain * 0.02));

                    half3 transmittance = exp(-_Underwater_Extinction.rgb * distance);
                    color = color * transmittance + _Underwater_WaterColor.rgb * (1.0 - transmittance);
                }

                // No caustics here on purpose. A screen-space sin(uv) lattice renders as a fixed grid of
                // dots pasted over the frame and slides across the world as the camera turns, which
                // reads as a bug rather than light. Caustics belong on world-space geometry: use
                // Varco/Underwater/Caustics on floor quads, which picks up _Underwater_CausticStrength
                // so it still follows the zone.

                return half4(lerp(sourceColor, color, saturate(_Underwater_Strength)), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
