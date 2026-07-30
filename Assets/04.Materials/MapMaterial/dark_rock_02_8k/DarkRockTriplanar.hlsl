#ifndef THEVARCO_DARK_ROCK_TRIPLANAR_INCLUDED
#define THEVARCO_DARK_ROCK_TRIPLANAR_INCLUDED

// Shared surface inputs and triplanar sampling for TheVarco/URP/Dark Rock Triplanar.
//
// Kept in one include so every pass declares an identical UnityPerMaterial block, which is what the
// SRP Batcher requires, and so ForwardLit and DepthNormals derive normals the same way.

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _TextureScale;
    half _BlendSharpness;
    half _NormalStrength;
    half _RoughnessMultiplier;
    half _MetallicMultiplier;
    half _OcclusionStrength;
CBUFFER_END

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
TEXTURE2D(_ArmMap); SAMPLER(sampler_ArmMap);

half3 TriplanarWeights(half3 normalWS)
{
    half3 weights = pow(abs(normalWS), _BlendSharpness);
    return weights / max(weights.x + weights.y + weights.z, 0.0001h);
}

half4 SampleTriplanar(TEXTURE2D_PARAM(textureToSample, samplerToSample), float3 positionWS, half3 weights)
{
    float2 uvX = positionWS.zy * _TextureScale;
    float2 uvY = positionWS.xz * _TextureScale;
    float2 uvZ = positionWS.xy * _TextureScale;
    return SAMPLE_TEXTURE2D(textureToSample, samplerToSample, uvX) * weights.x +
           SAMPLE_TEXTURE2D(textureToSample, samplerToSample, uvY) * weights.y +
           SAMPLE_TEXTURE2D(textureToSample, samplerToSample, uvZ) * weights.z;
}

half3 SampleTriplanarNormal(float3 positionWS, half3 normalWS, half3 weights)
{
    float2 uvX = positionWS.zy * _TextureScale;
    float2 uvY = positionWS.xz * _TextureScale;
    float2 uvZ = positionWS.xy * _TextureScale;
    half3 sampleX = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvX), _NormalStrength);
    half3 sampleY = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvY), _NormalStrength);
    half3 sampleZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvZ), _NormalStrength);
    half3 normalX = half3(sampleX.z * sign(normalWS.x), sampleX.y, sampleX.x);
    half3 normalY = half3(sampleY.x, sampleY.z * sign(normalWS.y), sampleY.y);
    half3 normalZ = half3(sampleZ.x, sampleZ.y, sampleZ.z * sign(normalWS.z));
    return normalize(normalX * weights.x + normalY * weights.y + normalZ * weights.z);
}

#endif // THEVARCO_DARK_ROCK_TRIPLANAR_INCLUDED
