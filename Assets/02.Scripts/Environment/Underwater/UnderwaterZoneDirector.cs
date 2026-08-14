using System.Collections.Generic;
using CaveBlockout;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Varco.Underwater
{
    /// <summary>
    /// Drives the whole-map underwater atmosphere for MainMap. Resolves which cave zone the tracked
    /// camera is in, blends the neighbouring <see cref="UnderwaterZoneProfile"/>s, and pushes the
    /// result into scene fog, trilight ambient, the screen-space pass globals, the camera clear
    /// colour and the global post-processing volume.
    ///
    /// Deliberately does not write RenderSettings every frame in edit mode: RenderSettings is
    /// serialised into the scene, so an [ExecuteAlways] Update would leave the scene permanently
    /// dirty. Use the "Apply Preview Zone" context-menu item to preview a zone while authoring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnderwaterZoneDirector : MonoBehaviour
    {
        private static readonly int WaterColorId = Shader.PropertyToID("_Underwater_WaterColor");
        private static readonly int ExtinctionId = Shader.PropertyToID("_Underwater_Extinction");
        private static readonly int DistortionId = Shader.PropertyToID("_Underwater_Distortion");
        private static readonly int DistortionSpeedId = Shader.PropertyToID("_Underwater_DistortionSpeed");
        private static readonly int CausticStrengthId = Shader.PropertyToID("_Underwater_CausticStrength");
        private static readonly int StrengthId = Shader.PropertyToID("_Underwater_Strength");

        [Header("Data")]
        [SerializeField] private UnderwaterZoneSet zoneSet;
        [SerializeField] private Volume globalVolume;

        [Header("Tracking")]
        [Tooltip("Main cave route. Zone selection uses arc length along this spline. Auto-found when empty.")]
        [SerializeField] private CaveRoute mainRoute;
        [Tooltip("Camera the atmosphere is evaluated for. Falls back to Camera.main.")]
        [SerializeField] private Camera trackedCamera;
        [Tooltip("Position used for zone lookup. Falls back to the tracked camera's transform.")]
        [SerializeField] private Transform trackedTransform;

        [Header("Blending")]
        [Tooltip("Width of the cross-fade window centred on each zone boundary, in metres.")]
        [Range(2f, 60f)] [SerializeField] private float zoneBlendMeters = 18f;
        [Tooltip("Extra time smoothing, in units per second. Lower is slower and softer.")]
        [Range(0.25f, 12f)] [SerializeField] private float blendSpeed = 2.5f;

        [Header("Outputs")]
        [Tooltip("RenderSettings fog fallback. Only used when the screen pass is inactive - running " +
                 "both fogs every pixel twice and collapses the image towards the water colour.")]
        [SerializeField] private bool driveSceneFog = true;
        [SerializeField] private bool driveAmbient = true;
        [SerializeField] private bool driveScreenPass = true;
        [SerializeField] private bool driveCameraBackground = true;
        [SerializeField] private bool drivePostProcessing = true;
        [SerializeField] private bool driveDirectionalLight = true;
        [SerializeField] private Light directionalLight;

        [Header("Live Tuning")]
        [Tooltip("Master fade for the screen-space pass. 0 disables it entirely.")]
        [Range(0f, 1f)] public float masterStrength = 1f;
        [Range(0.25f, 4f)] public float visibilityMultiplier = 1f;
        [Range(0f, 3f)] public float extinctionMultiplier = 1f;
        [Range(0f, 4f)] public float refractionMultiplier = 1f;
        [Range(0f, 4f)] public float causticMultiplier = 1f;
        [Range(0f, 3f)] public float ambientMultiplier = 1f;
        [Range(0f, 3f)] public float lightMultiplier = 1f;
        [Range(-2f, 2f)] public float exposureOffset;
        [Range(0f, 3f)] public float bloomMultiplier = 1f;

        [Header("Exterior")]
        [Tooltip("Blend to the exterior profile past the cave exit, and fade the underwater effect " +
                 "out above the sea surface. Without this, positions past the route end clamp to Z6 " +
                 "and the beach outside the exit renders as deep cave.")]
        [SerializeField] private bool exteriorBlendEnabled = true;
        [Tooltip("Zone id resolved for open water outside the exit.")]
        [SerializeField] private string exteriorZoneId = "Exterior";
        [Tooltip("Metres past the exit plane over which cave grading crossfades to the exterior profile.")]
        [SerializeField] private float exteriorBlendMeters = 12f;
        [Tooltip("World Y of the exterior sea surface. The exit mouth top sits at ~268 after the " +
                 "24x16 shrink, so 273 keeps the mouth fully submerged.")]
        [SerializeField] private float seaSurfaceY = 273f;
        [Tooltip("Vertical crossfade window at the waterline, so surfacing has no pop.")]
        [SerializeField] private float surfaceBlendMeters = 1.5f;

        [Header("Authoring")]
        [Tooltip("Zone applied by the \"Apply Preview Zone\" context-menu item.")]
        [SerializeField] private string previewZoneId = "Z1";

        [Header("Debug")]
        [Tooltip("F1/F2 are taken by CavePlaytestSwitcher, so the tuning panel uses F3.")]
        [SerializeField] private KeyCode debugPanelKey = KeyCode.F3;
        [SerializeField] private bool showDebugPanel;

        private readonly UnderwaterZoneProfile current = new UnderwaterZoneProfile();
        private readonly UnderwaterZoneProfile target = new UnderwaterZoneProfile();
        private readonly UnderwaterRouteSampler sampler = new UnderwaterRouteSampler();
        private readonly List<CaveZoneMarker> markers = new List<CaveZoneMarker>();

        private bool routeReady;
        private bool routeSearched;
        private bool markersSearched;
        private bool stateCaptured;
        private bool primed;
        /// <summary>0 fully submerged, 1 fully above <see cref="seaSurfaceY"/>. Set per resolve.</summary>
        private float aboveWaterWeight;
        private Rect panelRect = new Rect(18f, 18f, 320f, 420f);

        private bool capturedFogEnabled;
        private FogMode capturedFogMode;
        private Color capturedFogColor;
        private float capturedFogDensity;
        private AmbientMode capturedAmbientMode;
        private Color capturedAmbientSky;
        private Color capturedAmbientEquator;
        private Color capturedAmbientGround;
        private float capturedAmbientIntensity;

        public string CurrentZoneId => current.zoneId;
        public float RouteDistance { get; private set; }
        public float RouteLateralDistance { get; private set; }
        public UnderwaterZoneProfile CurrentProfile => current;

        /// <summary>
        /// The screen pass material lives on a project-wide renderer feature, so the global strength
        /// must start at zero. Otherwise a scene without a director would inherit whatever value the
        /// previously loaded scene left behind.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ClearGlobalsBeforeSceneLoad()
        {
            Shader.SetGlobalFloat(StrengthId, 0f);
        }

        private void OnEnable()
        {
            CaptureSceneState();
            ResolveReferences();
            primed = false;
            RenderPipelineManager.beginCameraRendering += ApplyScreenPassForCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= ApplyScreenPassForCamera;
            Shader.SetGlobalFloat(StrengthId, 0f);
            RestoreSceneState();
        }

        /// <summary>
        /// Re-decides the screen pass for the camera actually being rendered.
        ///
        /// 🔴 WHY THIS EXISTS. _Underwater_Strength is a GLOBAL shader property and the underwater pass
        /// is a renderer feature, so it runs on every camera in the frame. <see cref="Apply"/> sets that
        /// global once per frame from <see cref="trackedCamera"/>, which is right for the player's view
        /// and wrong for every other view: with the sub still inside the cave, the scene view flown out
        /// above the island was drawn fully submerged - open sky, palms and sand all graded as deep
        /// water. That is what "물 밖에 있는데도 물에 있는 효과가 적용됨" was.
        ///
        /// It matters beyond authoring comfort: the point of the exterior work is a Cinemachine cutscene
        /// that surfaces, and a cutscene camera is not the tracked camera. Without this the shot would
        /// stay underwater however high it climbed.
        ///
        /// Only the strength is re-evaluated. Water colour, extinction and everything scene-wide
        /// (ambient, fog, the sun, post processing) genuinely cannot be per-camera, and at strength 0
        /// none of the screen pass survives anyway.
        /// </summary>
        private void ApplyScreenPassForCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!driveScreenPass || camera == null || zoneSet == null)
                return;

            float weight = !exteriorBlendEnabled
                ? 0f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                    (camera.transform.position.y - seaSurfaceY) / Mathf.Max(0.01f, surfaceBlendMeters)));

            Shader.SetGlobalFloat(StrengthId,
                Mathf.Clamp01(current.screenStrength * masterStrength) * (1f - weight));
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
                return;

            if (Input.GetKeyDown(debugPanelKey))
                showDebugPanel = !showDebugPanel;

            ResolveReferences();

            Vector3 samplePosition = ResolveSamplePosition();
            ResolveTargetProfile(samplePosition, target);

            if (!primed)
            {
                current.SetToLerp(target, target, 0f);
                primed = true;
            }
            else
            {
                current.MoveTowards(target, 1f - Mathf.Exp(-blendSpeed * Time.deltaTime));
            }

            Apply(current);
        }

        /// <summary>
        /// Resolves and applies the atmosphere for an arbitrary world position with no time smoothing.
        /// Used by UnderwaterReviewCapture to render each zone at its authored values.
        /// </summary>
        public void EvaluateAndApplyAt(Vector3 worldPosition)
        {
            // Capture first so a later ClearUnderwaterEffect can restore the scene's authored state,
            // including when this is driven from an editor tool that never ran OnEnable.
            CaptureSceneState();
            ResolveReferences();
            ResolveTargetProfile(worldPosition, target);
            current.SetToLerp(target, target, 0f);
            primed = true;
            Apply(current);
        }

        [ContextMenu("Apply Preview Zone")]
        private void ApplyPreviewZone()
        {
            CaptureSceneState();
            ResolveReferences();
            if (zoneSet == null)
            {
                Debug.LogWarning($"{nameof(UnderwaterZoneDirector)}: no zone set assigned.", this);
                return;
            }

            current.SetToLerp(zoneSet.Resolve(previewZoneId), zoneSet.Resolve(previewZoneId), 0f);
            primed = true;
            Apply(current);
        }

        /// <summary>Zeroes the screen pass and restores the fog/ambient values captured on enable.</summary>
        [ContextMenu("Clear Underwater Effect")]
        public void ClearUnderwaterEffect()
        {
            Shader.SetGlobalFloat(StrengthId, 0f);
            RestoreSceneState();
            primed = false;
        }

        /// <summary>
        /// Forces the next resolve to re-scan the scene. Call after rebuilding the cave blockout.
        /// </summary>
        public void InvalidateSceneCache()
        {
            routeSearched = false;
            routeReady = false;
            markersSearched = false;
            markers.Clear();
        }

        private void ResolveReferences()
        {
            // Each scan is guarded by a flag rather than a null check so a scene genuinely missing a
            // route or markers does not run FindObjectsByType on every frame.
            if (mainRoute == null && !routeSearched)
            {
                routeSearched = true;
                CaveRoute[] routes = FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (CaveRoute route in routes)
                {
                    foreach (CaveRouteSplineDefinition definition in route.Definitions)
                    {
                        if (!definition.isMainRoute)
                            continue;
                        mainRoute = route;
                        break;
                    }
                    if (mainRoute != null)
                        break;
                }
            }

            if (!routeReady && mainRoute != null)
                routeReady = sampler.Build(mainRoute);

            if (!markersSearched)
            {
                markersSearched = true;
                markers.Clear();
                markers.AddRange(FindObjectsByType<CaveZoneMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            }

            if (trackedCamera == null)
                trackedCamera = Camera.main;

            if (trackedTransform == null && trackedCamera != null)
                trackedTransform = trackedCamera.transform;

            if (directionalLight == null)
            {
                foreach (Light candidate in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (candidate.type != LightType.Directional)
                        continue;
                    directionalLight = candidate;
                    break;
                }
            }
        }

        private Vector3 ResolveSamplePosition()
        {
            if (trackedTransform != null)
                return trackedTransform.position;
            return trackedCamera != null ? trackedCamera.transform.position : transform.position;
        }

        private void ResolveTargetProfile(Vector3 worldPosition, UnderwaterZoneProfile result)
        {
            if (zoneSet == null)
                return;

            aboveWaterWeight = !exteriorBlendEnabled
                ? 0f
                : Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((worldPosition.y - seaSurfaceY) / Mathf.Max(0.01f, surfaceBlendMeters)));

            if (routeReady && TryResolveFromRoute(worldPosition, result))
            {
                ApplyExteriorBlend(worldPosition, result);
                return;
            }

            ResolveFromMarkers(worldPosition, result);
        }

        /// <summary>
        /// Crossfades the resolved cave profile into the exterior one past the exit plane.
        ///
        /// FindSectionIndex clamps past-the-end distances to the last section, so without this the
        /// open water outside the mouth is graded as deep-cave Z6 forever. The projection onto the
        /// exit tangent only counts when the sample already sits near the route's end - a lateral
        /// fold of the route elsewhere can never read as "outside".
        /// </summary>
        private void ApplyExteriorBlend(Vector3 worldPosition, UnderwaterZoneProfile result)
        {
            if (!exteriorBlendEnabled || RouteDistance < sampler.TotalLength - 30f)
                return;
            if (!sampler.TryGetEndPose(out Vector3 exitPosition, out Vector3 exitDirection))
                return;

            float beyond = Vector3.Dot(worldPosition - exitPosition, exitDirection);
            if (beyond <= 0f)
                return;

            float weight = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(beyond / Mathf.Max(0.01f, exteriorBlendMeters)));
            result.SetToLerp(result, zoneSet.Resolve(exteriorZoneId), weight);
        }

        private bool TryResolveFromRoute(Vector3 worldPosition, UnderwaterZoneProfile result)
        {
            IReadOnlyList<CaveRouteSplineDefinition> definitions = mainRoute.Definitions;
            if (definitions == null || definitions.Count == 0)
                return false;

            List<CaveRouteSection> sections = null;
            foreach (CaveRouteSplineDefinition definition in definitions)
            {
                if (!definition.isMainRoute || definition.sections == null || definition.sections.Count == 0)
                    continue;
                sections = definition.sections;
                break;
            }

            if (sections == null)
                return false;

            RouteDistance = sampler.Project(worldPosition, out float lateral);
            RouteLateralDistance = lateral;

            int index = FindSectionIndex(sections, RouteDistance);
            CaveRouteSection section = sections[index];
            UnderwaterZoneProfile primary = zoneSet.Resolve(section.zoneId);

            float half = Mathf.Max(0.5f, zoneBlendMeters * 0.5f);
            float start = SectionStart(sections, index);
            float end = SectionEnd(sections, index);
            float toStart = RouteDistance - start;
            float toEnd = end - RouteDistance;

            if (toStart < half && index > 0)
            {
                UnderwaterZoneProfile previous = zoneSet.Resolve(sections[index - 1].zoneId);
                float weight = 0.5f - 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(toStart / half));
                result.SetToLerp(primary, previous, weight);
                return true;
            }

            if (toEnd < half && index < sections.Count - 1)
            {
                UnderwaterZoneProfile next = zoneSet.Resolve(sections[index + 1].zoneId);
                float weight = 0.5f - 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(toEnd / half));
                result.SetToLerp(primary, next, weight);
                return true;
            }

            result.SetToLerp(primary, primary, 0f);
            return true;
        }

        private static float SectionStart(List<CaveRouteSection> sections, int index)
        {
            CaveRouteSection section = sections[index];
            if (section.startDistanceMeters >= 0f)
                return section.startDistanceMeters;

            float accumulated = 0f;
            for (int i = 0; i < index; i++)
                accumulated += Mathf.Max(0f, sections[i].nominalLength);
            return accumulated;
        }

        private static float SectionEnd(List<CaveRouteSection> sections, int index)
        {
            CaveRouteSection section = sections[index];
            if (section.endDistanceMeters >= 0f)
                return section.endDistanceMeters;
            return SectionStart(sections, index) + Mathf.Max(1f, section.nominalLength);
        }

        private static int FindSectionIndex(List<CaveRouteSection> sections, float distance)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                if (distance <= SectionEnd(sections, i))
                    return i;
            }
            return sections.Count - 1;
        }

        /// <summary>
        /// Fallback for positions the route cannot describe (branch caves, free-flying editor camera).
        /// Scores each CaveZoneMarker OBB by how far outside it the point sits, in units of half-extent.
        /// </summary>
        private void ResolveFromMarkers(Vector3 worldPosition, UnderwaterZoneProfile result)
        {
            CaveZoneMarker best = null;
            CaveZoneMarker runnerUp = null;
            float bestScore = float.PositiveInfinity;
            float runnerUpScore = float.PositiveInfinity;

            foreach (CaveZoneMarker marker in markers)
            {
                if (marker == null)
                    continue;

                Vector3 local = marker.transform.InverseTransformPoint(worldPosition);
                Vector3 half = marker.guideSize * 0.5f;
                float score = Mathf.Max(
                    Mathf.Abs(local.x) / Mathf.Max(0.01f, half.x),
                    Mathf.Max(Mathf.Abs(local.y) / Mathf.Max(0.01f, half.y),
                              Mathf.Abs(local.z) / Mathf.Max(0.01f, half.z)));

                if (score < bestScore)
                {
                    runnerUp = best;
                    runnerUpScore = bestScore;
                    best = marker;
                    bestScore = score;
                }
                else if (score < runnerUpScore)
                {
                    runnerUp = marker;
                    runnerUpScore = score;
                }
            }

            if (best == null)
            {
                UnderwaterZoneProfile fallback = zoneSet.Fallback;
                result.SetToLerp(fallback, fallback, 0f);
                return;
            }

            UnderwaterZoneProfile primary = zoneSet.Resolve(best.zoneId);
            if (runnerUp == null || runnerUpScore > 2f)
            {
                result.SetToLerp(primary, primary, 0f);
                return;
            }

            // Blend the two closest boxes so marker-driven transitions are not a hard switch either.
            // Equidistant scores give 0.5 (even blend); a clearly closer best gives 0 (pure primary).
            float weight = 0.5f * Mathf.Clamp01(bestScore / Mathf.Max(0.01f, runnerUpScore));
            result.SetToLerp(primary, zoneSet.Resolve(runnerUp.zoneId), weight);
        }

        private void Apply(UnderwaterZoneProfile profile)
        {
            float visibility = Mathf.Max(1f, profile.visibilityMeters * visibilityMultiplier);
            // Above the sea surface there is no water to absorb light: the screen pass fades out over
            // the waterline window instead of switching, so breaking the surface has no pop.
            float screenStrength = driveScreenPass
                ? Mathf.Clamp01(profile.screenStrength * masterStrength) * (1f - aboveWaterWeight)
                : 0f;
            bool screenPassActive = screenStrength > 0.001f;

            // Exactly one fog path runs at a time. The screen pass already does per-channel extinction
            // and in-scatter; letting per-material MixFog run alongside it applies fog twice and then
            // multiplies extinction over the already-fogged colour, which flattens the whole frame to
            // the water colour no matter how brightly the cave is lit.
            if (driveSceneFog && !screenPassActive)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = profile.fogColor;
                RenderSettings.fogDensity = 1.7308f / visibility;
            }
            else if (screenPassActive)
            {
                RenderSettings.fog = false;
            }

            // The screen-pass fade above hands fog duty back to RenderSettings the moment the camera
            // surfaces - which would paint the open-air island with underwater fog. Above the
            // waterline neither fog path may run.
            if (aboveWaterWeight > 0.5f)
                RenderSettings.fog = false;

            if (driveAmbient)
            {
                // ambientIntensity only scales the skybox probe, so the trilight level is folded into
                // the colours instead and the built-in multiplier is pinned to 1.
                float scale = profile.ambientIntensity * ambientMultiplier;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = ScaleRgb(profile.ambientSky, scale);
                RenderSettings.ambientEquatorColor = ScaleRgb(profile.ambientEquator, scale);
                RenderSettings.ambientGroundColor = ScaleRgb(profile.ambientGround, scale);
                RenderSettings.ambientIntensity = 1f;
            }

            if (driveDirectionalLight && directionalLight != null)
            {
                directionalLight.intensity = profile.directionalIntensity * lightMultiplier;
                // The scene has exactly one light, authored cold-blue for the cave. Left undriven it
                // also lit the island and headland, which is why dry land read teal from outside.
                directionalLight.color = profile.directionalColor;
            }

            // Profile colours are already linear, and Shader.SetGlobal* applies no conversion, so they
            // are passed through untouched.
            Vector3 extinction = profile.ExtinctionRgb * extinctionMultiplier / Mathf.Max(0.0001f, visibilityMultiplier);
            Shader.SetGlobalVector(WaterColorId,
                new Vector4(profile.fogColor.r, profile.fogColor.g, profile.fogColor.b, 1f));
            Shader.SetGlobalVector(ExtinctionId, new Vector4(extinction.x, extinction.y, extinction.z, 0f));
            Shader.SetGlobalFloat(DistortionId, profile.refraction * refractionMultiplier);
            Shader.SetGlobalFloat(DistortionSpeedId, profile.refractionSpeed);
            Shader.SetGlobalFloat(CausticStrengthId, profile.causticStrength * causticMultiplier);
            Shader.SetGlobalFloat(StrengthId, screenStrength);

            if (driveCameraBackground && trackedCamera != null)
            {
                // Underwater, the solid clear colour IS the water beyond the last drawn surface.
                // Above the waterline that same colour reads as a wall of water in the sky, so the
                // camera clears to the skybox instead.
                if (aboveWaterWeight > 0.5f)
                {
                    trackedCamera.clearFlags = CameraClearFlags.Skybox;
                }
                else
                {
                    trackedCamera.clearFlags = CameraClearFlags.SolidColor;
                    trackedCamera.backgroundColor = profile.backgroundColor;
                }
            }

            if (drivePostProcessing)
                ApplyPostProcessing(profile, visibility);
        }

        private void ApplyPostProcessing(UnderwaterZoneProfile profile, float visibility)
        {
            if (globalVolume == null || globalVolume.profile == null)
                return;

            VolumeProfile volumeProfile = globalVolume.profile;

            if (volumeProfile.TryGet(out ColorAdjustments colorAdjustments))
            {
                colorAdjustments.postExposure.value = profile.postExposure + exposureOffset;
                colorAdjustments.contrast.value = profile.contrast;
                colorAdjustments.saturation.value = profile.saturation;
                colorAdjustments.colorFilter.value = profile.colorFilter;
            }

            if (volumeProfile.TryGet(out Bloom bloom))
                bloom.intensity.value = profile.bloomIntensity * bloomMultiplier;

            if (volumeProfile.TryGet(out Vignette vignette))
                vignette.intensity.value = profile.vignetteIntensity;

            if (volumeProfile.TryGet(out WhiteBalance whiteBalance))
            {
                whiteBalance.temperature.value = profile.whiteBalanceTemperature;
                whiteBalance.tint.value = profile.whiteBalanceTint;
            }

            if (volumeProfile.TryGet(out DepthOfField depthOfField))
            {
                // Tie defocus to visibility so the far wall softens, but keep it well short of the
                // in-scatter distance and cap the radius: starting the blur at half the visibility with
                // a near-full radius smeared the rock detail into flat blobs.
                //
                // Above the waterline the defocus range opens toward infinity - air has no turbidity,
                // and an island 140 m out would otherwise sit fully inside the blur band.
                float dofVisibility = Mathf.Lerp(visibility, 4000f, aboveWaterWeight);
                depthOfField.gaussianStart.value = dofVisibility * 0.9f;
                depthOfField.gaussianEnd.value = dofVisibility * 2.2f;
                depthOfField.gaussianMaxRadius.value = Mathf.Lerp(0.5f, 0.3f, Mathf.Clamp01(visibility / 45f));
            }
        }

        private static Color ScaleRgb(Color color, float scale)
        {
            return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
        }

        private void CaptureSceneState()
        {
            if (stateCaptured)
                return;

            capturedFogEnabled = RenderSettings.fog;
            capturedFogMode = RenderSettings.fogMode;
            capturedFogColor = RenderSettings.fogColor;
            capturedFogDensity = RenderSettings.fogDensity;
            capturedAmbientMode = RenderSettings.ambientMode;
            capturedAmbientSky = RenderSettings.ambientSkyColor;
            capturedAmbientEquator = RenderSettings.ambientEquatorColor;
            capturedAmbientGround = RenderSettings.ambientGroundColor;
            capturedAmbientIntensity = RenderSettings.ambientIntensity;
            stateCaptured = true;
        }

        private void RestoreSceneState()
        {
            if (!stateCaptured)
                return;

            RenderSettings.fog = capturedFogEnabled;
            RenderSettings.fogMode = capturedFogMode;
            RenderSettings.fogColor = capturedFogColor;
            RenderSettings.fogDensity = capturedFogDensity;
            RenderSettings.ambientMode = capturedAmbientMode;
            RenderSettings.ambientSkyColor = capturedAmbientSky;
            RenderSettings.ambientEquatorColor = capturedAmbientEquator;
            RenderSettings.ambientGroundColor = capturedAmbientGround;
            RenderSettings.ambientIntensity = capturedAmbientIntensity;
            stateCaptured = false;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !showDebugPanel)
                return;

            panelRect = GUI.Window(4137, panelRect, DrawDebugPanel, "UNDERWATER ZONES");
        }

        private void DrawDebugPanel(int id)
        {
            float y = 26f;
            GUI.Label(new Rect(16f, y, 290f, 20f),
                $"zone {current.zoneId}   route {RouteDistance:0.0} m   lateral {RouteLateralDistance:0.0} m");
            y += 20f;
            GUI.Label(new Rect(16f, y, 290f, 20f),
                $"visibility {current.visibilityMeters * visibilityMultiplier:0.0} m   density {1.7308f / Mathf.Max(1f, current.visibilityMeters * visibilityMultiplier):0.000}");
            y += 26f;

            masterStrength = Slider("Screen strength", masterStrength, 0f, 1f, ref y);
            visibilityMultiplier = Slider("Visibility x", visibilityMultiplier, 0.25f, 4f, ref y);
            extinctionMultiplier = Slider("Extinction x", extinctionMultiplier, 0f, 3f, ref y);
            refractionMultiplier = Slider("Refraction x", refractionMultiplier, 0f, 4f, ref y);
            causticMultiplier = Slider("Caustics x", causticMultiplier, 0f, 4f, ref y);
            ambientMultiplier = Slider("Ambient x", ambientMultiplier, 0f, 3f, ref y);
            lightMultiplier = Slider("Sun x", lightMultiplier, 0f, 3f, ref y);
            bloomMultiplier = Slider("Bloom x", bloomMultiplier, 0f, 3f, ref y);
            exposureOffset = Slider("Exposure +", exposureOffset, -2f, 2f, ref y);

            if (GUI.Button(new Rect(16f, y + 6f, 140f, 26f), "Reset tuning"))
                ResetTuning();

            GUI.DragWindow(new Rect(0f, 0f, 320f, 22f));
        }

        private static float Slider(string label, float value, float min, float max, ref float y)
        {
            GUI.Label(new Rect(16f, y, 290f, 18f), $"{label}  {value:0.000}");
            value = GUI.HorizontalSlider(new Rect(16f, y + 18f, 280f, 16f), value, min, max);
            y += 38f;
            return value;
        }

        private void ResetTuning()
        {
            masterStrength = 1f;
            visibilityMultiplier = 1f;
            extinctionMultiplier = 1f;
            refractionMultiplier = 1f;
            causticMultiplier = 1f;
            ambientMultiplier = 1f;
            lightMultiplier = 1f;
            bloomMultiplier = 1f;
            exposureOffset = 0f;
        }
    }
}
