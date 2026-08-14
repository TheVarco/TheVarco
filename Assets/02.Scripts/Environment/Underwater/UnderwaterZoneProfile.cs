using System;
using UnityEngine;

namespace Varco.Underwater
{
    /// <summary>
    /// Atmosphere settings for one cave zone. <see cref="zoneId"/> matches
    /// CaveBlockout.CaveZoneMarker.zoneId and CaveBlockout.CaveRouteSection.zoneId ("Z1".."Z6").
    ///
    /// Colours here are LINEAR radiance, not sRGB. Measured behaviour: scaling
    /// RenderSettings.ambientSkyColor by 8 raised the rendered mean luminance by ~8.8x, so the engine
    /// consumes those values linearly rather than applying an sRGB decode. Shader.SetGlobal* performs
    /// no conversion either, so a single linear authoring space keeps every consumer consistent.
    /// Values above 1 are therefore legitimate and expected for the brighter zones.
    /// </summary>
    [Serializable]
    public sealed class UnderwaterZoneProfile
    {
        [Header("Identity")]
        public string zoneId = "Z1";

        [Tooltip("Distance in metres at which geometry is ~95% lost to fog. Taken from " +
                 "UnderwaterCaveLevelGuide/03_art_lighting_guide.png. Fog density is derived from this.")]
        [Min(1f)] public float visibilityMeters = 12f;

        [Header("Water Colour (linear)")]
        [Tooltip("In-scattered water colour. Geometry converges on this at the visibility distance, and " +
                 "it is also used for the RenderSettings fog fallback when the screen pass is off.")]
        [ColorUsage(false, true)] public Color fogColor = new Color(0.55f, 1.50f, 2.60f);

        [Tooltip("Camera clear colour for cave openings. The screen pass leaves far-plane pixels " +
                 "un-attenuated, so this is what an opening reads as - keep it brighter than fogColor " +
                 "so openings pull the eye forward.")]
        [ColorUsage(false, true)] public Color backgroundColor = new Color(1.40f, 3.20f, 5.20f);

        [Header("Ambient Trilight (linear, may exceed 1)")]
        [ColorUsage(false, true)] public Color ambientSky = new Color(0.68f, 1.84f, 3.36f);
        [ColorUsage(false, true)] public Color ambientEquator = new Color(0.36f, 1.04f, 2.00f);
        [ColorUsage(false, true)] public Color ambientGround = new Color(0.10f, 0.36f, 0.72f);
        [Range(0f, 4f)] public float ambientIntensity = 1f;

        [Header("Lighting")]
        [Tooltip("Directional light intensity for this zone. Z4 drops this to zero for the blackout fault.")]
        [Range(0f, 6f)] public float directionalIntensity = 1.5f;

        [Tooltip("Directional light colour for this zone. The scene has exactly ONE light and it was " +
                 "authored cold-blue (0.62, 0.82, 1.00) for the cave, which also painted the open-air " +
                 "island, headland and palms blue once the exterior existed. Driving the colour per zone " +
                 "lets the exterior use daylight while the cave keeps the value it already had.")]
        [ColorUsage(false)] public Color directionalColor = new Color(0.62f, 0.82f, 1.00f);

        [Header("Screen Pass")]
        [Tooltip("Relative per-channel extinction. Blue is the reference at 1.0; red must be the " +
                 "largest so it is lost first, which is the deep-water cue. Absolute magnitude is " +
                 "derived from visibilityMeters, so this only sets the hue of the falloff.")]
        public Vector3 extinctionTint = new Vector3(2.8f, 1.25f, 1.0f);
        [Range(0f, 0.01f)] public float refraction = 0.0022f;
        [Range(0f, 2f)] public float refractionSpeed = 0.6f;
        [Range(0f, 1f)] public float causticStrength = 0.10f;
        [Range(0f, 1f)] public float screenStrength = 1f;

        [Header("Post Processing")]
        /// <summary>
        /// The lit zones all sit between 0.32 and 0.60. The upper bound is 6 rather than 2 because Z4
        /// needs 5.10: its scene radiance is roughly 30x below every other zone (directional light 0,
        /// ambient a fifteenth of the rest), which puts the whole frame under the ACES toe, and the
        /// tonemapper then clamps it to literal black - measured at 0.0% non-black pixels. Five stops
        /// of extra exposure is the dark-adapted-eye compensation that buys the image back. Treat a
        /// value above 2 as a statement that the zone has almost no light in it, not as a fix for
        /// grading that merely looks dim.
        /// </summary>
        [Range(-2f, 6f)] public float postExposure = 0.35f;
        [Range(-100f, 100f)] public float contrast = 12f;
        [Range(-100f, 100f)] public float saturation = 6f;
        [ColorUsage(false)] public Color colorFilter = new Color(0.82f, 0.95f, 1f);
        [Range(0f, 2f)] public float bloomIntensity = 0.30f;
        [Range(0f, 1f)] public float vignetteIntensity = 0.30f;
        [Range(-100f, 100f)] public float whiteBalanceTemperature = -14f;
        [Range(-100f, 100f)] public float whiteBalanceTint = -6f;

        [Header("Particles")]
        [Range(0f, 4f)] public float particleDensityScale = 1f;

        [Header("Reserved For Follow-Up Lighting Work")]
        [Tooltip("Not consumed yet. Volumetric god-ray intensity for Z2/Z3/Z6 when light shafts are added.")]
        [Range(0f, 2f)] public float shaftIntensity;
        [Tooltip("Not consumed yet. Bioluminescent accent colour for Z2 coral and Z6 mineral veins.")]
        [ColorUsage(false)] public Color bioluminescenceTint = Color.black;

        /// <summary>
        /// FogMode.ExponentialSquared evaluates exp(-(density*d)^2). Solving for 95% extinction at the
        /// authored visibility distance gives density = sqrt(-ln(0.05)) / d. Only used for the
        /// RenderSettings fog fallback and the edit-mode bake; the screen pass uses
        /// <see cref="ExtinctionRgb"/> instead.
        /// </summary>
        public float FogDensity => 1.7308f / Mathf.Max(1f, visibilityMeters);

        /// <summary>
        /// Per-metre per-channel extinction for the screen pass. The blue channel is scaled so that
        /// exp(-k_b * visibility) = 0.05, i.e. blue is 95% gone exactly at the authored visibility
        /// distance; red and green are scaled off it by <see cref="extinctionTint"/> so red is lost
        /// several times sooner.
        /// </summary>
        public Vector3 ExtinctionRgb
        {
            get
            {
                float blue = 2.9957f / Mathf.Max(1f, visibilityMeters);
                float reference = Mathf.Max(0.0001f, extinctionTint.z);
                return new Vector3(
                    blue * extinctionTint.x / reference,
                    blue * extinctionTint.y / reference,
                    blue);
            }
        }

        public UnderwaterZoneProfile Clone()
        {
            return (UnderwaterZoneProfile)MemberwiseClone();
        }

        /// <summary>Writes the weighted blend of <paramref name="a"/> and <paramref name="b"/> into this instance.</summary>
        public void SetToLerp(UnderwaterZoneProfile a, UnderwaterZoneProfile b, float t)
        {
            // Captured before anything is written, because SetToLerp(this, target, t) is a supported
            // call and the ambient fields below would otherwise be overwritten before they are read.
            float exposureBlend = BlendPostExposure(a, b, t);

            zoneId = t < 0.5f ? a.zoneId : b.zoneId;
            visibilityMeters = Mathf.Lerp(a.visibilityMeters, b.visibilityMeters, t);

            fogColor = Color.Lerp(a.fogColor, b.fogColor, t);
            backgroundColor = Color.Lerp(a.backgroundColor, b.backgroundColor, t);

            ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t);
            ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t);
            ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t);
            ambientIntensity = Mathf.Lerp(a.ambientIntensity, b.ambientIntensity, t);
            directionalIntensity = Mathf.Lerp(a.directionalIntensity, b.directionalIntensity, t);
            directionalColor = Color.Lerp(a.directionalColor, b.directionalColor, t);

            extinctionTint = Vector3.Lerp(a.extinctionTint, b.extinctionTint, t);
            refraction = Mathf.Lerp(a.refraction, b.refraction, t);
            refractionSpeed = Mathf.Lerp(a.refractionSpeed, b.refractionSpeed, t);
            causticStrength = Mathf.Lerp(a.causticStrength, b.causticStrength, t);
            screenStrength = Mathf.Lerp(a.screenStrength, b.screenStrength, t);

            postExposure = exposureBlend;
            contrast = Mathf.Lerp(a.contrast, b.contrast, t);
            saturation = Mathf.Lerp(a.saturation, b.saturation, t);
            colorFilter = Color.Lerp(a.colorFilter, b.colorFilter, t);
            bloomIntensity = Mathf.Lerp(a.bloomIntensity, b.bloomIntensity, t);
            vignetteIntensity = Mathf.Lerp(a.vignetteIntensity, b.vignetteIntensity, t);
            whiteBalanceTemperature = Mathf.Lerp(a.whiteBalanceTemperature, b.whiteBalanceTemperature, t);
            whiteBalanceTint = Mathf.Lerp(a.whiteBalanceTint, b.whiteBalanceTint, t);

            particleDensityScale = Mathf.Lerp(a.particleDensityScale, b.particleDensityScale, t);

            shaftIntensity = Mathf.Lerp(a.shaftIntensity, b.shaftIntensity, t);
            bioluminescenceTint = Color.Lerp(a.bioluminescenceTint, b.bioluminescenceTint, t);
        }

        /// <summary>
        /// Blends <see cref="postExposure"/> so the crossing is never brighter than either side of it.
        ///
        /// 🔴 WHY THIS IS NOT A Mathf.Lerp. Exposure and ambient are two halves of one quantity: how
        /// bright the zone ends up on screen is roughly ambient * 2^postExposure. Z4 sets ambient to a
        /// fifteenth of its neighbours and postExposure to 5.10 to compensate, and those two cancel out
        /// at the zone's centre - but not on the way in. Lerping both linearly means the halfway point
        /// carries Z3's ambient with most of Z4's exposure, and the measured result was a frame 2.2x
        /// BRIGHTER than Z3 itself: flying into the blackout zone flashed white before going dark.
        ///
        /// Interpolating the product geometrically instead, and solving for the exposure that produces
        /// it, keeps the crossing monotonic by construction. Where two zones share an ambient level this
        /// reduces to the linear blend it replaces, so only the Z3/Z4 and Z4/Z5 boundaries change.
        /// </summary>
        private static float BlendPostExposure(UnderwaterZoneProfile a, UnderwaterZoneProfile b, float t)
        {
            float ambientA = AmbientReference(a);
            float ambientB = AmbientReference(b);

            float productA = ambientA * Mathf.Pow(2f, a.postExposure);
            float productB = ambientB * Mathf.Pow(2f, b.postExposure);

            // Geometric interpolation of the product, i.e. linear in stops.
            float product = Mathf.Pow(productA, 1f - t) * Mathf.Pow(productB, t);
            float ambient = Mathf.Max(1e-4f, Mathf.Lerp(ambientA, ambientB, t));

            return Mathf.Log(product / ambient, 2f);
        }

        /// <summary>
        /// Scalar stand-in for "how much light this zone puts on a surface". Only the sky term is used:
        /// equator and ground track it by construction in every authored zone, and what matters here is
        /// the ratio between zones, not an accurate irradiance.
        /// </summary>
        private static float AmbientReference(UnderwaterZoneProfile profile)
        {
            float luma = 0.2126f * profile.ambientSky.r
                       + 0.7152f * profile.ambientSky.g
                       + 0.0722f * profile.ambientSky.b;
            return Mathf.Max(1e-4f, luma * Mathf.Max(0.01f, profile.ambientIntensity));
        }

        /// <summary>
        /// Exponential smoothing towards <paramref name="target"/>, used to damp zone crossings.
        /// Safe to pass this instance as the source: SetToLerp reads each field before writing it.
        /// </summary>
        public void MoveTowards(UnderwaterZoneProfile target, float t)
        {
            SetToLerp(this, target, Mathf.Clamp01(t));
        }
    }
}
