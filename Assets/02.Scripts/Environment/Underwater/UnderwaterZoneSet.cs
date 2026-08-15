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
        ///
        /// Version 12 added UnderwaterZoneProfile.directionalColor, which the director now writes to the
        /// scene's only light. A version-11 asset has no value stored for it, so it must be regenerated
        /// rather than trusted - the field decides whether the sun is daylight or cave-blue.
        ///
        /// Version 13 re-graded Z4. postExposure moved 0.10 -> 5.10, which is outside the range the
        /// field used to allow, so a version-12 asset does not merely hold an older value - it holds one
        /// that renders the zone as literal black. It has to be regenerated, not left alone.
        /// </summary>
        private const int CurrentDataVersion = 13;

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
                    // Halved from 1.15. Z2 is the bioluminescent basin, and at 1.15 the ambient was
                    // bright enough that the coral's own light did nothing: a point light at intensity
                    // 100 on the rock beside a prop was invisible, and only at 250 did it register, as
                    // specular rather than as a pool. Dropping ambient is what lets the glow read at all.
                    // Measured at 0.58: lit pixels in the Z2 close-up went 23,625 -> 43,351 once the
                    // prop lights were added, where at 1.15 the same lights moved almost nothing.
                    //
                    // postExposure is deliberately left at 0.55. Screen brightness goes roughly as
                    // ambient * 2^postExposure and Lerp() blends the pair in log space to keep zone
                    // crossings monotonic, so this was checked at the boundary rather than assumed: mean
                    // blue across Z1->Z2 in MainScene_final runs 80.7 / 77.2 / 68.0 / 61.9 / 55.6, a
                    // smooth ramp with no step. See Artifacts/GlowVariants/FINDINGS.md.
                    ambientIntensity = 0.58f,
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
                //
                // 🔴 가시거리 5.5는 MAP_GUIDE:27의 설계된 기믹이라 그대로다. 바뀐 것은 그레이딩뿐이다.
                // 배치모드 측정 결과 Z4는 화면이 "어두운" 정도가 아니라 non-black 픽셀 0.0%, 즉 문자
                // 그대로 (0,0,0)이었다. 원인은 안개가 아니라 노출이다. 이 존은 directionalIntensity가
                // 0이고 앰비언트가 다른 존의 1/15라 씬 광량이 30배 낮은데, postExposure만 다른 존과
                // 같은 0.10을 쓰고 있었다. 그 결과 프레임 전체가 ACES 토 아래로 들어가 톤매퍼가 0으로
                // 클램프했다. 노출을 5.10으로 올리고(암순응) contrast 18 -> 6, vignette 0.55 -> 0.40으로
                // 완화하니 물빛과 조종석이 읽히고, 잠수함 헤드라이트가 5 m 안쪽 물체를 실제로 드러낸다.
                //
                // 헤드라이트로 20 m 밖 동굴 벽을 밝히는 것은 여기서도 여전히 불가능하다. Z4 통로는 뱃머리
                // 기준 전방 최근접 표면이 17-21 m인데 5.5 m 가시거리의 25 m 투과율은 1e-6이라, 스포트라이트
                // 강도를 50,000까지 올려도 화면에 점 몇 개만 찍힌다. "가까이 가야 보인다"가 이 존의 규칙이다.
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
                    postExposure = 5.10f,
                    contrast = 6f,
                    saturation = -20f,
                    colorFilter = new Color(0.85f, 0.92f, 1.00f),
                    bloomIntensity = 0.15f,
                    vignetteIntensity = 0.40f,
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
                },

                // Exterior - 동굴 밖 개방 수역. 존이 아니라 UnderwaterZoneDirector의 exteriorZoneId가
                // 출구 평면을 지난 위치에서 명시적으로 찾는 프로필이다 (경로 거리는 마지막 섹션에
                // 클램프되므로 섹션 데이터로는 도달할 수 없다). 햇빛 드는 얕은 열대 수역: 시야가 길고,
                // 빨강이 살아 있고, 그레이딩은 중립에 가깝게 - 수면 위 컷씬 프레임이 이 그레이딩을
                // 그대로 물려받기 때문이다.
                new UnderwaterZoneProfile
                {
                    zoneId = "Exterior",
                    visibilityMeters = 45f,
                    fogColor = new Color(0.0800f, 0.4200f, 0.5200f),
                    backgroundColor = new Color(0.3000f, 1.0500f, 1.3000f),
                    // 수면 위 프레임이 이 앰비언트를 그대로 받는다. 동굴 존들(3.0-3.6)은 톤매핑
                    // 전제의 심해 값이라, 그 수준이면 야외 지형·산체가 흰색으로 날아간다 - 야외는
                    // 디렉셔널 라이트가 주광이고 앰비언트는 채움광 수준이어야 한다.
                    // 청록 캐스트 제거 (4세션차). 예전 값은 B/R 1.5였는데, 수면 위 물체는 앰비언트가
                    // 채움광이므로 그 편차가 섬·산체·야자수를 그대로 파랗게 칠했다. 총 밝기는 유지한 채
                    // B/R만 1.1로 낮춘다. 지면 바운스는 모래에서 오므로 오히려 따뜻해야 맞다.
                    ambientSky = new Color(1.5500f, 1.6000f, 1.7000f),
                    ambientEquator = new Color(0.8600f, 0.8800f, 0.9400f),
                    ambientGround = new Color(0.3400f, 0.3100f, 0.2600f),
                    ambientIntensity = 1.00f,
                    directionalIntensity = 1.40f,
                    // 🔴 씬에는 디렉셔널 라이트가 하나뿐이고 그 색 (0.62, 0.82, 1.00)은 동굴용 한색이다.
                    // 존별로 구동하기 전까지 그 파란 태양이 물 밖 지형까지 칠하고 있었다 - 사용자가 보고한
                    // "물 밖인데도 청록/파랑" 증상의 주원인. 여기서만 노을 스카이박스(SKy 22)에 맞는
                    // 따뜻한 주광으로 바꾸고, Z1-Z6은 필드 기본값을 그대로 물려받아 기준선이 움직이지 않는다.
                    directionalColor = new Color(1.00f, 0.95f, 0.86f),
                    // 얕은 물이라 빨강이 오래 살아남는다 - 심해 단서의 역함수가 곧 "밖으로 나왔다"는 단서
                    extinctionTint = new Vector3(1.4f, 1.01f, 1.0f),
                    refraction = 0.0022f,
                    refractionSpeed = 0.70f,
                    causticStrength = 0.45f,
                    postExposure = 0.10f,
                    contrast = 0f,
                    // 채도 +5는 남은 색편차를 증폭하기만 했다. 야외는 스카이박스와 지형 알베도가 이미
                    // 충분히 채도가 있으므로 중립으로 둔다.
                    saturation = 0f,
                    colorFilter = Color.white,
                    bloomIntensity = 0.30f,
                    vignetteIntensity = 0.12f,
                    // 음수 temperature는 파랑을 민다 (URP ColorUtils.ColorBalanceToLMSCoeffs 확인, §2-E).
                    // 동굴에서는 의도한 것이지만 야외에서는 청록 캐스트에 그대로 얹혔다.
                    whiteBalanceTemperature = 0f,
                    whiteBalanceTint = 0f,
                    particleDensityScale = 0.40f,
                    shaftIntensity = 1.00f,
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
