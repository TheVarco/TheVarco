using System.Text;
using CaveBlockout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Varco.Underwater.EditorTools
{
    /// <summary>
    /// Reports why the underwater review capture sees no geometry: shader compile state, renderer
    /// presence and bounds, whether the capture viewpoints are actually inside the cave, and the
    /// resolved fog/ambient values.
    /// </summary>
    public static class UnderwaterDiagnostics
    {
        private const string MainMapPath = "Assets/01.Scenes/MainMap.unity";

        [MenuItem("Tools/Underwater Cave/Diagnose Underwater Setup")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            Debug.Log(Report());
        }

        public static string Report()
        {
            var text = new StringBuilder();
            text.AppendLine("===== UNDERWATER DIAGNOSTICS =====");

            ReportShader(text, "TheVarco/URP/Dark Rock Triplanar");
            ReportShader(text, "Varco/Underwater/FullScreen");
            ReportShader(text, "Varco/Underwater/Caustics");
            ReportShader(text, "Varco/Underwater/Motes");

            text.AppendLine($"colorSpace={QualitySettings.activeColorSpace} currentQuality={QualitySettings.GetQualityLevel()}");
            text.AppendLine($"pipeline={(GraphicsSettings.defaultRenderPipeline != null ? GraphicsSettings.defaultRenderPipeline.name : "NULL")}" +
                            $" qualityPipeline={(QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline.name : "NULL")}");
            text.AppendLine($"fog={RenderSettings.fog} mode={RenderSettings.fogMode} color={RenderSettings.fogColor} density={RenderSettings.fogDensity:0.0000}");
            text.AppendLine($"ambientMode={RenderSettings.ambientMode} sky={RenderSettings.ambientSkyColor}");

            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            text.AppendLine($"meshRenderers={renderers.Length}");
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer.name != "CaveShell")
                    continue;
                Bounds bounds = renderer.bounds;
                text.AppendLine($"CaveShell active={renderer.gameObject.activeInHierarchy} enabled={renderer.enabled} " +
                                $"shadows={renderer.shadowCastingMode} layer={renderer.gameObject.layer} " +
                                $"center={bounds.center} size={bounds.size}");
                Material material = renderer.sharedMaterial;
                text.AppendLine($"CaveShell material={(material != null ? material.name : "NULL")} " +
                                $"shader={(material != null && material.shader != null ? material.shader.name : "NULL")} " +
                                $"renderQueue={(material != null ? material.renderQueue : -1)}");
                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                text.AppendLine($"CaveShell mesh={(mesh != null ? mesh.name : "NULL")} " +
                                $"verts={(mesh != null ? mesh.vertexCount : -1)} tris={(mesh != null ? mesh.triangles.Length / 3 : -1)}");
            }

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Light light in lights)
                text.AppendLine($"light {light.name} type={light.type} color={light.color} intensity={light.intensity:0.00} shadows={light.shadows}");

            Camera main = UnderwaterEnvironmentBuilder.FindMainCamera();
            if (main != null)
            {
                var data = main.GetComponent<UniversalAdditionalCameraData>();
                text.AppendLine($"mainCamera={main.name} clear={main.clearFlags} bg={main.backgroundColor} " +
                                $"post={(data != null ? data.renderPostProcessing.ToString() : "no data")} " +
                                $"volumeMask={(data != null ? data.volumeLayerMask.value : -1)}");
            }

            var volume = Object.FindFirstObjectByType<Volume>();
            text.AppendLine(volume != null
                ? $"volume={volume.name} global={volume.isGlobal} weight={volume.weight} layer={volume.gameObject.layer} profile={(volume.profile != null ? volume.profile.name : "NULL")}"
                : "volume=NONE");

            // Are the capture viewpoints actually inside the cave mesh? Fire rays outward; an enclosed
            // point hits the shell in every direction.
            CaveRoute route = UnderwaterEnvironmentBuilder.FindMainRoute();
            if (route == null)
            {
                text.AppendLine("mainRoute=NULL");
                return text.ToString();
            }

            CaveRouteSplineDefinition definition = null;
            foreach (CaveRouteSplineDefinition candidate in route.Definitions)
            {
                if (candidate.isMainRoute && candidate.sections != null && candidate.sections.Count > 0)
                {
                    definition = candidate;
                    break;
                }
            }

            if (definition == null)
            {
                text.AppendLine("mainDefinition=NULL");
                return text.ToString();
            }

            Vector3[] directions =
            {
                Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
            };

            // What the director actually resolves and writes, so authored values can be told apart from
            // whatever the engine ends up storing (RenderSettings ambient clamps its components).
            var director = Object.FindFirstObjectByType<UnderwaterZoneDirector>();
            var zoneSet = AssetDatabase.LoadAssetAtPath<UnderwaterZoneSet>(
                "Assets/Settings/Underwater/MainMapUnderwaterZones.asset");
            if (zoneSet != null)
            {
                text.AppendLine($"zoneSet needsRegen={zoneSet.NeedsRegeneration} count={zoneSet.Zones.Count}");
                foreach (UnderwaterZoneProfile profile in zoneSet.Zones)
                {
                    text.AppendLine($"  authored {profile.zoneId} water={profile.fogColor} sky={profile.ambientSky} " +
                                    $"bg={profile.backgroundColor} ext={profile.ExtinctionRgb}");
                }
            }

            foreach (CaveRouteSection section in definition.sections)
            {
                float startT = route.ResolveSectionStartT(definition, section);
                float endT = route.ResolveSectionEndT(definition, section);
                float t = Mathf.Lerp(startT, endT, 0.5f);
                Vector3 position = route.Container.EvaluatePosition(definition.splineIndex, t);
                float height = route.EvaluateHeight(definition.splineIndex, t);
                Vector3 eye = position + Vector3.up * (height * 0.12f);

                if (director != null)
                {
                    director.EvaluateAndApplyAt(eye);
                    text.AppendLine($"  applied {section.zoneId} skyStored={RenderSettings.ambientSkyColor} " +
                                    $"eqStored={RenderSettings.ambientEquatorColor} fogOn={RenderSettings.fog}");
                }

                var hits = new StringBuilder();
                int hitCount = 0;
                foreach (Vector3 direction in directions)
                {
                    if (Physics.Raycast(eye, direction, out RaycastHit hit, 500f))
                    {
                        hitCount++;
                        hits.Append($" {direction.ToString()}={hit.distance:0.0}m");
                    }
                    else
                    {
                        hits.Append($" {direction.ToString()}=MISS");
                    }
                }

                text.AppendLine($"{section.zoneId} eye={eye} height={height:0.0} enclosedHits={hitCount}/6{hits}");
            }

            return text.ToString();
        }

        public static void ReportBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            Debug.Log(Report());
        }

        /// <summary>
        /// Two safety properties worth re-checking after any change: the screen pass is project-wide,
        /// so it must stay inert in scenes with no director; and the underwater root must survive a
        /// cave blockout regeneration, which is why it lives as a sibling of CaveBlockout.
        ///
        /// SIDE EFFECT: the blockout check calls CaveBlockoutBuilder.RegenerateCurrentScene, which runs
        /// AssetDatabase.SaveAssets internally and therefore rewrites Assets/Generated/CaveBlockout/
        /// (CaveShell.asset and friends). The regenerated mesh is not bit-identical to the committed
        /// one, so revert that folder afterwards unless a blockout rebuild was actually wanted:
        ///     git checkout -- Assets/Generated/CaveBlockout/
        /// </summary>
        public static void RegressionBatch()
        {
            var text = new StringBuilder();
            text.AppendLine("===== UNDERWATER REGRESSION =====");

            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            int before = CountUnderwaterObjects();
            CaveBlockout.Editor.CaveBlockoutBuilder.RegenerateCurrentScene(false);
            int after = CountUnderwaterObjects();
            text.AppendLine($"blockoutRegenerate underwaterObjectsBefore={before} after={after} " +
                            $"{(after == before && before > 0 ? "PASS" : "FAIL")}");
            text.AppendLine("  NOTE: this rewrote Assets/Generated/CaveBlockout/. " +
                            "Run 'git checkout -- Assets/Generated/CaveBlockout/' unless a rebuild was intended.");

            foreach (string scenePath in new[] { "Assets/01.Scenes/SampleScene.unity", "Assets/01.Scenes/Min_Test.unity" })
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                float strength = Shader.GetGlobalFloat("_Underwater_Strength");
                bool fog = RenderSettings.fog;
                text.AppendLine($"leakCheck {System.IO.Path.GetFileName(scenePath)} " +
                                $"_Underwater_Strength={strength:0.000} fog={fog} " +
                                $"{(strength <= 0.001f ? "PASS" : "FAIL")}");
            }

            Debug.Log(text.ToString());
        }

        private static int CountUnderwaterObjects()
        {
            int count = 0;
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != UnderwaterEnvironmentBuilder.RootName)
                    continue;
                count = root.GetComponentsInChildren<Transform>(true).Length;
            }
            return count;
        }

        private static void ReportShader(StringBuilder text, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                text.AppendLine($"shader \"{shaderName}\" = NOT FOUND");
                return;
            }

            bool hasError = ShaderUtil.ShaderHasError(shader);
            text.AppendLine($"shader \"{shaderName}\" hasError={hasError} passes={shader.passCount}");
            if (!hasError)
                return;

            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                text.AppendLine($"    [{message.severity}] line {message.line}: {message.message} {message.messageDetails}");
        }
    }
}
