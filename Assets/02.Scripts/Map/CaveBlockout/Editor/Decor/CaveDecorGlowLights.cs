using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CaveBlockout.Editor.Decor
{
    /// <summary>
    /// Gives the Z2 glowing props an actual light, because emission alone lights nothing.
    ///
    /// Why this is needed at all: URP emission is a shading term on the surface it belongs to, not a
    /// light source. Nothing in this project bridges that gap - the scene has never been baked (no
    /// LightingDataAsset), the decor is not marked ContributeGI, RealtimeEmissive has been a no-op since
    /// Enlighten was removed in Unity 6, and there are no light probes. So the coral glowed while the
    /// rock beside it stayed exactly as dark as if the coral were not there.
    ///
    /// Measured: at Z2 ambient x0.5, adding these lights took the lit-pixel count in the close-up from
    /// 23,625 to 43,351. See Artifacts/GlowVariants/FINDINGS.md.
    ///
    /// Two decisions worth keeping:
    ///
    /// Shadows off, always. The additional-light shadow atlas is 2048 with a 256 tier and a point light
    /// costs six faces, so roughly ten shadowed point lights fit in the whole frame. Z2 places 58 glowing
    /// props. Shadowless is also the correct look - bioluminescence is a soft area source, not a lamp.
    ///
    /// Lights live in the prefab, not in the scenes. The material fix reaches all three scenes for free
    /// because the .mat GUIDs are unchanged; per-scene light placement would have to be re-run per scene
    /// and would drift between MainMap, MainScene_final and the Intro scene. Prefab-embedded costs one
    /// light per prop with no clustering control, which deferred rendering makes affordable
    /// (additionalLightsPerObjectLimit is a forward-path limit and does not apply).
    /// </summary>
    public static class CaveDecorGlowLights
    {
        private const string PrefabRoot = "Assets/03.Prefabs/CaveDecor";
        private const string MaterialRoot = "Assets/04.Materials/CaveArtPass";
        private const string LightName = "GlowLight";

        /// <summary>Tuned against Z2 ambientIntensity 0.58. Raising ambient again would need these raised too.</summary>
        private const float Intensity = 100f;
        private const float Range = 12f;

        /// <summary>prefab name to the emissive material its Z2 instances use.</summary>
        private static readonly (string prefab, string material)[] Targets =
        {
            ("LowPolyBlueCoral", "Z2_LowPolyBlueCoral_Emissive"),
            ("VioletLowpolySeaFan", "Z2_VioletSeaFan_Emissive"),
            ("BlueCrystalFormation", "Z2_BlueCrystal_Emissive"),
            ("StylizedTealCoral", "Z2_StylizedTealCoral_Emissive"),
            ("BlueCrystalSeaweed", "Z2_BlueCrystalSeaweed_Emissive"),
            ("ColorfulStylizedCoral", "Z2_ColorfulStylizedCoral_Emissive")
        };

        [MenuItem("Tools/Underwater Cave/Decor/발광 프롭에 Point Light 주입")]
        public static void ApplyInteractive()
        {
            Debug.Log(Apply());
        }

        public static void ApplyBatch()
        {
            Debug.Log(Apply());
        }

        /// <summary>Removes every injected light again, so the change is reversible without git.</summary>
        public static void RemoveBatch()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR GLOW LIGHTS: REMOVE =====");

            foreach ((string prefabName, string _) in Targets)
            {
                string path = $"{PrefabRoot}/{prefabName}.prefab";
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null)
                {
                    report.AppendLine($"SKIP {prefabName} - not found");
                    continue;
                }

                Transform existing = root.transform.Find(LightName);
                if (existing != null)
                {
                    Object.DestroyImmediate(existing.gameObject);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.AppendLine($"removed {prefabName}");
                }

                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        public static string Apply()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR GLOW LIGHTS =====");
            report.AppendLine($"intensity={Intensity} range={Range} shadows=None");

            int applied = 0;
            var missing = new List<string>();

            foreach ((string prefabName, string materialName) in Targets)
            {
                string prefabPath = $"{PrefabRoot}/{prefabName}.prefab";
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialRoot}/{materialName}.mat");
                if (material == null)
                {
                    missing.Add($"{materialName}.mat");
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                {
                    missing.Add($"{prefabName}.prefab");
                    continue;
                }

                try
                {
                    // Reused rather than duplicated, so re-running retunes instead of stacking lights.
                    Transform existing = root.transform.Find(LightName);
                    GameObject holder = existing != null
                        ? existing.gameObject
                        : new GameObject(LightName);

                    if (existing == null)
                        holder.transform.SetParent(root.transform, false);

                    // Centre of the mesh, not the pivot: these props are wall-mounted with the pivot at
                    // the base, so a pivot-placed light would sit inside the rock and half of it would
                    // be wasted lighting the wall's back face.
                    holder.transform.localPosition = LocalCentre(root);

                    // Not ?? - Unity overloads == so a missing component compares equal to null without
                    // being C# null, and ?? would hand back the fake-null instead of adding one.
                    Light light = holder.GetComponent<Light>();
                    if (light == null)
                        light = holder.AddComponent<Light>();

                    light.type = LightType.Point;
                    light.color = HueOf(material.GetColor("_EmissionColor"));
                    light.intensity = Intensity;
                    light.range = Range;
                    light.shadows = LightShadows.None;
                    light.renderMode = LightRenderMode.ForcePixel;
                    light.lightmapBakeType = LightmapBakeType.Realtime;

                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    report.AppendLine($"  {prefabName,-24} colour={light.color} pos={holder.transform.localPosition}");
                    applied++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();

            report.AppendLine($"applied={applied} missing={missing.Count}");
            foreach (string name in missing)
                report.AppendLine($"  MISSING {name}");

            return report.ToString();
        }

        /// <summary>
        /// Emission is HDR, so its magnitude is a brightness that has already been authored into the
        /// material. Feeding that straight into Light.intensity would make one prop 20x another for
        /// reasons that have nothing to do with how bright its light should be. Only the hue is taken.
        /// </summary>
        private static Color HueOf(Color emission)
        {
            float peak = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
            return peak <= 0.001f
                ? Color.white
                : new Color(emission.r / peak, emission.g / peak, emission.b / peak, 1f);
        }

        private static Vector3 LocalCentre(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
                return Vector3.zero;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return root.transform.InverseTransformPoint(bounds.center);
        }
    }
}
