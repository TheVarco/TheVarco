using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Varco.Underwater
{
    /// <summary>
    /// Records the scene state that UnderwaterEnvironmentBuilder overwrote, so removing the underwater
    /// environment restores exactly what was there instead of guessing at Unity defaults.
    ///
    /// Written once, on the first Apply. Subsequent Apply runs leave it untouched so the original
    /// pre-underwater state survives repeated builds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnderwaterEnvironmentSnapshot : MonoBehaviour
    {
        [SerializeField] private bool captured;

        [SerializeField] private bool fogEnabled;
        [SerializeField] private FogMode fogMode = FogMode.ExponentialSquared;
        [SerializeField] private Color fogColor = Color.gray;
        [SerializeField] private float fogDensity = 0.01f;

        [SerializeField] private AmbientMode ambientMode = AmbientMode.Skybox;
        [SerializeField] private Color ambientSky = Color.gray;
        [SerializeField] private Color ambientEquator = Color.gray;
        [SerializeField] private Color ambientGround = Color.gray;
        [SerializeField] private float ambientIntensity = 1f;
        [SerializeField] private float reflectionIntensity = 1f;
        [SerializeField] private Color subtractiveShadowColor = Color.gray;
        [SerializeField] private Light sun;

        [SerializeField] private Camera targetCamera;
        [SerializeField] private CameraClearFlags cameraClearFlags = CameraClearFlags.Skybox;
        [SerializeField] private Color cameraBackground = Color.black;
        [SerializeField] private bool cameraPostProcessing;
        [SerializeField] private AntialiasingMode cameraAntialiasing = AntialiasingMode.None;
        [SerializeField] private CameraOverrideOption cameraDepthOption = CameraOverrideOption.UsePipelineSettings;
        [SerializeField] private bool cameraDithering;

        [SerializeField] private Light directionalLight;
        [SerializeField] private Color lightColor = Color.white;
        [SerializeField] private float lightIntensity = 1f;
        [SerializeField] private LightShadows lightShadows = LightShadows.Soft;
        [SerializeField] private float lightShadowStrength = 1f;
        [SerializeField] private float lightBounceIntensity = 1f;

        [SerializeField] private Renderer caveShell;
        [SerializeField] private ShadowCastingMode caveShellShadows = ShadowCastingMode.On;

        public bool Captured => captured;

        public void CaptureIfNeeded(Camera camera, Light directional, Renderer shell)
        {
            if (captured)
                return;

            fogEnabled = RenderSettings.fog;
            fogMode = RenderSettings.fogMode;
            fogColor = RenderSettings.fogColor;
            fogDensity = RenderSettings.fogDensity;

            ambientMode = RenderSettings.ambientMode;
            ambientSky = RenderSettings.ambientSkyColor;
            ambientEquator = RenderSettings.ambientEquatorColor;
            ambientGround = RenderSettings.ambientGroundColor;
            ambientIntensity = RenderSettings.ambientIntensity;
            reflectionIntensity = RenderSettings.reflectionIntensity;
            subtractiveShadowColor = RenderSettings.subtractiveShadowColor;
            sun = RenderSettings.sun;

            targetCamera = camera;
            if (camera != null)
            {
                cameraClearFlags = camera.clearFlags;
                cameraBackground = camera.backgroundColor;

                var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData != null)
                {
                    cameraPostProcessing = cameraData.renderPostProcessing;
                    cameraAntialiasing = cameraData.antialiasing;
                    cameraDepthOption = cameraData.requiresDepthOption;
                    cameraDithering = cameraData.dithering;
                }
            }

            directionalLight = directional;
            if (directional != null)
            {
                lightColor = directional.color;
                lightIntensity = directional.intensity;
                lightShadows = directional.shadows;
                lightShadowStrength = directional.shadowStrength;
                lightBounceIntensity = directional.bounceIntensity;
            }

            caveShell = shell;
            if (shell != null)
                caveShellShadows = shell.shadowCastingMode;

            captured = true;
        }

        public void Restore()
        {
            if (!captured)
                return;

            RenderSettings.fog = fogEnabled;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;

            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.subtractiveShadowColor = subtractiveShadowColor;
            RenderSettings.sun = sun;

            if (targetCamera != null)
            {
                targetCamera.clearFlags = cameraClearFlags;
                targetCamera.backgroundColor = cameraBackground;

                var cameraData = targetCamera.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData != null)
                {
                    cameraData.renderPostProcessing = cameraPostProcessing;
                    cameraData.antialiasing = cameraAntialiasing;
                    cameraData.requiresDepthOption = cameraDepthOption;
                    cameraData.dithering = cameraDithering;
                }
            }

            if (directionalLight != null)
            {
                directionalLight.color = lightColor;
                directionalLight.intensity = lightIntensity;
                directionalLight.shadows = lightShadows;
                directionalLight.shadowStrength = lightShadowStrength;
                directionalLight.bounceIntensity = lightBounceIntensity;
            }

            if (caveShell != null)
                caveShell.shadowCastingMode = caveShellShadows;
        }
    }
}
