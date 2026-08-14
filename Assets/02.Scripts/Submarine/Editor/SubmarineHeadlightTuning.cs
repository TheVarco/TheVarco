using UnityEngine;

namespace Varco.SubmarineTools.EditorTools
{
    /// <summary>
    /// Every authored number for the submarine headlight, in ONE place, so a tuning pass is a single
    /// edit followed by one batch run of <see cref="SubmarineHeadlightBatch.InstallAndCaptureBatch"/>.
    ///
    /// Positions are in the submarine ROOT's local frame, which is the only frame that stays meaningful
    /// as the sub moves. The root is scaled 2, so one local unit is two metres - every comment below
    /// quotes metres to keep that trap visible.
    ///
    /// Measured geometry of Submarine_final (see SUBMARINE_HEADLIGHT_DIAGNOSE):
    ///   hull local extents (1.619, 1.638, 4.057) about local centre (0, 0.15, -2.999)
    ///   -> bow tip at local z +1.058 (world +8.216 at the scene's spawn pose), stern at local z -7.056
    ///   -> the pilot seats sit at local (0, -0.25, -1.3), i.e. 4.7 m behind the bow tip
    /// </summary>
    public static class SubmarineHeadlightTuning
    {
        /// <summary>
        /// Local position in the submarine root's frame. z 1.30 puts the lamp 0.48 m AHEAD of the bow
        /// tip.
        ///
        /// 🔴 WHY IT MOVED. cseo0dev's lamp sat at local (0, 0.99, 0.301) - that is 1.5 m behind the bow
        /// tip and inside the hull's own bounding box, buried under the nose. With shadows off the cone
        /// still reached the water, but the first thing it hit was the nose interior 1.5 m away, where
        /// the inverse-square term is 50/2.25 = 22. The cave 10 m out got 0.5. The lamp was spending its
        /// entire budget on the inside of the submarine's own nose, which is what "잠수함 라이트로도 앞이
        /// 보이지 않는다" was.
        /// </summary>
        public static readonly Vector3 LocalPosition = new Vector3(0f, 0.30f, 1.30f);

        /// <summary>
        /// Pitch below the submarine's forward axis, in degrees. The old lamp was 15° down, which threw
        /// most of the cone into the floor. 10° keeps the corridor ahead in the hot part of the beam and
        /// still catches the floor, which is the surface that reads as "how fast am I moving".
        /// </summary>
        public const float PitchDegrees = 10f;

        /// <summary>
        /// 🔴 WHY SO WIDE. Measured, not guessed. The beam probe found that in Z4 the centre ray hits
        /// nothing within 60 m while the walls sit 20 m out at ~37° off axis - so with the 74°/30° cone
        /// the only rays that touched rock were the ones at the very edge of the falloff, where the
        /// spot attenuation has already reached zero. The lamp was lighting empty water. Widening to
        /// 120°/70° with everything else held fixed moved the brightest lit pixel from 16 to 171 out of
        /// 255. Intensity is in candela, so a wider cone costs nothing in irradiance - it only adds
        /// coverage.
        /// </summary>
        public const float SpotAngle = 120f;
        public const float InnerSpotAngle = 70f;

        /// <summary>
        /// URP fades the light out towards <see cref="Range"/>, so range has to comfortably exceed the
        /// distance that actually matters, or the far end of the useful cone is already fading.
        /// </summary>
        public const float Range = 60f;

        /// <summary>
        /// 🔴 WHY SO MUCH MORE THAN 50. Z4's extinction is 0.545/m in blue and 0.872/m in red, and the
        /// screen pass adds a constant in-scatter floor of (0.0034, 0.0079, 0.0114). A lit surface is
        /// only visible when albedo * (I / d_lamp²) * exp(-k * d_camera) is an appreciable fraction of
        /// that floor, and the nearest rock in Z4 is 20 m from the bow.
        ///
        /// 3000 is where the measured lit pixels stop being a handful of specks, and it is deliberately
        /// short of the 6000 that read marginally brighter: the other five zones have geometry within a
        /// few metres of the hull, and intensity that helps at 20 m blows out rock at 3 m. Verified
        /// against Z1/Z2/Z3/Z5 rather than assumed - see SubmarineHeadlightCapture.
        ///
        /// This alone does NOT make Z4 playable. Nothing on the lamp does; see the capture notes.
        /// </summary>
        public const float Intensity = 3000f;

        /// <summary>
        /// Warm white. Red is the first channel water eats, so a neutral lamp arrives cyan and reads as
        /// more of the same ambient rather than as a lamp. HANDOFF §4-C measured that the lamp - not the
        /// atmosphere values - is what puts red back in frame, and the reference art wants a warm pool
        /// of light in the dark.
        /// </summary>
        public static readonly Color Color = new Color(1f, 0.93f, 0.82f);

        /// <summary>
        /// Shadows stay off. The lamp now sits ahead of the bow tip pointing away from the hull, so
        /// there is no hull geometry inside the cone to bleed through, and a shadow-casting spot at this
        /// range costs a 2048 map for nothing. If a future mount puts hull back in the cone, this is the
        /// first thing to turn on.
        /// </summary>
        public const LightShadows Shadows = LightShadows.None;
    }
}
