using System.Collections.Generic;
using UnityEngine;

namespace Varco.Underwater
{
    /// <summary>
    /// The Z1..Z6 atmosphere profiles for the deep-sea cave. Defaults come from
    /// UnderwaterCaveLevelGuide/03_art_lighting_guide.png (visibility ranges) and MAP_GUIDE.md
    /// (per-zone colour rules); <see cref="ResetToGuideDefaults"/> restores them at any time.
    /// </summary>
    [CreateAssetMenu(menuName = "Varco/Underwater/Zone Set", fileName = "UnderwaterZoneSet")]
    public sealed class UnderwaterZoneSet : ScriptableObject
    {
        /// <summary>
        /// Bump when the meaning of existing fields changes rather than just their values. Version 2
        /// moved every colour from sRGB to linear radiance and replaced the absolute absorption vector
        /// with a relative tint, so a version-1 asset deserialises into fields that still exist but now
        /// mean something different - it has to be regenerated, not migrated field by field.
        /// </summary>
        private const int CurrentDataVersion = 10;

        [SerializeField] private int dataVersion;
        [SerializeField] private List<UnderwaterZoneProfile> zones = new List<UnderwaterZoneProfile>();

        [Tooltip("Used when the tracked camera cannot be matched to any zone.")]
        [SerializeField] private UnderwaterZoneProfile fallback = new UnderwaterZoneProfile();

        public IReadOnlyList<UnderwaterZoneProfile> Zones => zones;
        public UnderwaterZoneProfile Fallback => fallback;
        public bool NeedsRegeneration => dataVersion < CurrentDataVersion || zones == null || zones.Count == 0;

        /// <summary>
        /// Resolves a zone by id. Branch sections are named "Z2_Branch" and friends, so an exact miss
        /// falls back to the leading "Z&lt;n&gt;" token and inherits the parent zone's atmosphere.
        /// </summary>
        public UnderwaterZoneProfile Resolve(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId))
                return fallback;

            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] != null && zones[i].zoneId == zoneId)
                    return zones[i];
            }

            int separator = zoneId.IndexOf('_');
            if (separator > 0)
            {
                string root = zoneId.Substring(0, separator);
                for (int i = 0; i < zones.Count; i++)
                {
                    if (zones[i] != null && zones[i].zoneId == root)
                        return zones[i];
                }
            }

            return fallback;
        }

        /// <summary>
        /// Values are LINEAR radiance, not sRGB (see <see cref="UnderwaterZoneProfile"/>). Calibrated
        /// from a render sweep on Z2: ambient at this level puts the dark basalt cave material at a
        /// readable exposure, and fogColor is the in-scattered radiance that survives ACES tonemapping
        /// as the haze seen in the reference art.
        /// </summary>
        public void ResetToGuideDefaults()
        {
            zones = new List<UnderwaterZoneProfile>
            {
                // Z1 심해 추락지점 - 26-30m, 짙은 남색, 첫 우상향 통로
                new UnderwaterZoneProfile
                {
                    zoneId = "Z1",
                    visibilityMeters = 28f,
                    fogColor = new Color(0.0356f, 0.2216f, 0.3409f),
                    backgroundColor = new Color(0.1422f, 0.8860f, 1.2451f),
                    ambientSky = new Color(1.5750f, 2.1700f, 3.5000f),
                    ambientEquator = new Color(0.8663f, 1.1935f, 1.9250f),
                    ambientGround = new Color(0.3150f, 0.4340f, 0.7000f),
                    ambientIntensity = 1.00f,
                    directionalIntensity = 1.20f,
                    extinctionTint = new Vector3(2.2f, 1.02f, 1.0f),
                    refraction = 0.0022f,
                    refractionSpeed = 0.60f,
                    causticStrength = 0.08f,
                    postExposure = 0.35f,
                    contrast = 5f,
                    saturation = 0f,
                    colorFilter = new Color(0.82f, 0.95f, 1.00f),
                    bloomIntensity = 0.30f,
                    vignetteIntensity = 0.30f,
                    whiteBalanceTemperature = -14f,
                    whiteBalanceTint = 0f,
                    particleDensityScale = 1.15f,
                    shaftIntensity = 0.15f,
                    bioluminescenceTint = new Color(0.20f, 0.45f, 0.85f)
                },

                // Z2 발광 산호 분지 - 36-44m, 청록·보라 발광 산호 집중
                new UnderwaterZoneProfile
                {
                    zoneId = "Z2",
                    visibilityMeters = 40f,
                    fogColor = new Color(0.0279f, 0.2066f, 0.2448f),
                    backgroundColor = new Color(0.1119f, 0.7514f, 0.8395f),
                    ambientSky = new Color(1.4400f, 1.9840f, 3.2000f),
                    ambientEquator = new Color(0.7920f, 1.0912f, 1.7600f),
                    ambientGround = new Color(0.2880f, 0.3968f, 0.6400f),
                    ambientIntensity = 1.15f,
                    directionalIntensity = 1.80f,
                    extinctionTint = new Vector3(2.2f, 1.02f, 1.0f),
                    refraction = 0.0020f,
                    refractionSpeed = 0.65f,
                    causticStrength = 0.25f,
                    postExposure = 0.55f,
                    contrast = 4f,
                    saturation = 4f,
                    colorFilter = new Color(0.80f, 0.97f, 1.00f),
                    bloomIntensity = 0.55f,
                    vignetteIntensity = 0.20f,
                    whiteBalanceTemperature = -16f,
                    whiteBalanceTint = 0f,
                    particleDensityScale = 0.85f,
                    shaftIntensity = 0.90f,
                    bioluminescenceTint = new Color(0.35f, 0.70f, 1.00f)
                },

                // Z3 난류 협곡 - 24-28m, 강청색, 빠른 조류와 낙석
                new UnderwaterZoneProfile
                {
                    zoneId = "Z3",
                    visibilityMeters = 26f,
                    fogColor = new Color(0.0265f, 0.2038f, 0.2482f),
                    backgroundColor = new Color(0.1190f, 0.8736f, 0.9592f),
                    ambientSky = new Color(1.3500f, 1.8600f, 3.0000f),
                    ambientEquator = new Color(0.7425f, 1.0230f, 1.6500f),
                    ambientGround = new Color(0.2700f, 0.3720f, 0.6000f),
                    ambientIntensity = 1.05f,
                    directionalIntensity = 1.50f,
                    extinctionTint = new Vector3(2.2f, 1.02f, 1.0f),
                    refraction = 0.0030f,
                    refractionSpeed = 0.95f,
                    causticStrength = 0.15f,
                    postExposure = 0.45f,
                    contrast = 5f,
                    saturation = 0f,
                    colorFilter = new Color(0.80f, 0.95f, 1.00f),
                    bloomIntensity = 0.38f,
                    vignetteIntensity = 0.26f,
                    whiteBalanceTemperature = -14f,
                    whiteBalanceTint = 0f,
                    particleDensityScale = 1.35f,
                    shaftIntensity = 0.70f,
                    bioluminescenceTint = new Color(0.18f, 0.42f, 0.80f)
                },

                // Z4 완전 암흑 단층 - 4-7m, 손전등 외 조명 0, 발광 생물 없음.
                // 헤드라이트가 후속 작업이라 지금은 의도적으로 거의 보이지 않는 것이 정상이다.
                new UnderwaterZoneProfile
                {
                    zoneId = "Z4",
                    visibilityMeters = 5.5f,
                    fogColor = new Color(0.0034f, 0.0079f, 0.0114f),
                    backgroundColor = new Color(0.0044f, 0.0126f, 0.0198f),
                    ambientSky = new Color(0.1132f, 0.2211f, 0.2688f),
                    ambientEquator = new Color(0.0633f, 0.1263f, 0.1556f),
                    ambientGround = new Color(0.0204f, 0.0411f, 0.0538f),
                    ambientIntensity = 1.00f,
                    directionalIntensity = 0f,
                    extinctionTint = new Vector3(1.6f, 1.02f, 1.0f),
                    refraction = 0.0016f,
                    refractionSpeed = 0.45f,
                    causticStrength = 0f,
                    postExposure = 0.10f,
                    contrast = 18f,
                    saturation = -20f,
                    colorFilter = new Color(0.85f, 0.92f, 1.00f),
                    bloomIntensity = 0.15f,
                    vignetteIntensity = 0.55f,
                    whiteBalanceTemperature = -8f,
                    whiteBalanceTint = -2f,
                    particleDensityScale = 1.60f,
                    shaftIntensity = 0f,
                    bioluminescenceTint = Color.black
                },

                // Z5 열수 굴뚝 - 22-26m, 검은 화산암, 백색 열수, 붉은 광물선
                new UnderwaterZoneProfile
                {
                    zoneId = "Z5",
                    visibilityMeters = 24f,
                    fogColor = new Color(0.0439f, 0.2462f, 0.3966f),
                    backgroundColor = new Color(0.1709f, 1.0191f, 1.4641f),
                    ambientSky = new Color(1.6200f, 2.2320f, 3.6000f),
                    ambientEquator = new Color(0.8910f, 1.2276f, 1.9800f),
                    ambientGround = new Color(0.3240f, 0.4464f, 0.7200f),
                    ambientIntensity = 0.95f,
                    directionalIntensity = 1.20f,
                    extinctionTint = new Vector3(2.2f, 1.02f, 1.0f),
                    refraction = 0.0026f,
                    refractionSpeed = 0.80f,
                    causticStrength = 0.06f,
                    postExposure = 0.32f,
                    contrast = 6f,
                    saturation = -2f,
                    colorFilter = new Color(0.86f, 0.94f, 1.00f),
                    bloomIntensity = 0.50f,
                    vignetteIntensity = 0.32f,
                    whiteBalanceTemperature = -12f,
                    whiteBalanceTint = 0f,
                    particleDensityScale = 1.25f,
                    shaftIntensity = 0.20f,
                    bioluminescenceTint = new Color(0.85f, 0.20f, 0.12f)
                },

                // Z6 출구 목구멍 - 50-60m, 작은 수면광 출구, 대형 상어
                new UnderwaterZoneProfile
                {
                    zoneId = "Z6",
                    visibilityMeters = 55f,
                    fogColor = new Color(0.0258f, 0.1387f, 0.1784f),
                    backgroundColor = new Color(0.1297f, 0.5253f, 0.5947f),
                    ambientSky = new Color(1.6200f, 2.2320f, 3.6000f),
                    ambientEquator = new Color(0.8910f, 1.2276f, 1.9800f),
                    ambientGround = new Color(0.3240f, 0.4464f, 0.7200f),
                    ambientIntensity = 1.25f,
                    directionalIntensity = 2.40f,
                    extinctionTint = new Vector3(2.3f, 1.02f, 1.0f),
                    refraction = 0.0018f,
                    refractionSpeed = 0.60f,
                    causticStrength = 0.35f,
                    postExposure = 0.60f,
                    contrast = 3f,
                    saturation = 2f,
                    colorFilter = new Color(0.82f, 0.97f, 1.00f),
                    bloomIntensity = 0.62f,
                    vignetteIntensity = 0.16f,
                    whiteBalanceTemperature = -18f,
                    whiteBalanceTint = 0f,
                    particleDensityScale = 0.70f,
                    shaftIntensity = 1.60f,
                    bioluminescenceTint = new Color(0.30f, 0.65f, 1.00f)
                }
            };

            // Off-route positions keep the Z1 deep-water read rather than snapping to clear air.
            fallback = zones[0].Clone();
            fallback.zoneId = "Fallback";
            dataVersion = CurrentDataVersion;
        }

        private void OnValidate()
        {
            // Only fill a genuinely empty asset. Regenerating here on a version mismatch would run on
            // every domain reload and silently throw away any hand-tuning, and because it clears the
            // version flag before the builder ever sees it the asset would never be written back to
            // disk either. Version upgrades are the builder's job, where they get saved.
            if (zones == null || zones.Count == 0)
                ResetToGuideDefaults();
        }
    }
}
