using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Varco.Exterior.EditorTools
{
    /// <summary>
    /// Steepens the submerged flank of the hand-placed island without moving anything above the
    /// waterline.
    ///
    /// The pack's TerrainData is a 50 x 50 m tile with a nominal 600 m height range but only 5.585 m of
    /// actual relief, of which just 0.845 m clears the 273 m sea surface. Measured radially, the run
    /// from the waterline contour out to the first flat (h = 0) sample is 17.3 m, so the underwater
    /// flank averages 15.3 degrees - the island reads as a pancake sitting on a plate.
    ///
    /// This remaps ONLY the submerged band, pinning both of its ends:
    ///
    ///     t  = (sea - y) / (sea - base)      0 at the waterline, 1 on the flat band
    ///     g  = 1 - (1 - t)^k
    ///     y' = sea - (sea - base) * g        and y' = y for any y >= sea
    ///
    /// Both fixed points matter. Holding y >= sea identical keeps the dry silhouette and the 22 island
    /// props that sit on it exactly where they are. Holding the flat band at its original base Y keeps
    /// the weld to <see cref="ExteriorEnvironmentBuilder"/>'s island skirt, which starts from that
    /// border height.
    ///
    /// Note what this does and does not change: the AVERAGE slope is untouched, because both ends are
    /// pinned. What changes is the tangent slope just under the shoreline - k = 2 takes it from 15.3 to
    /// 28.7 degrees, easing off towards the flat band. That concave drop-off is the shape a real reef
    /// island has, and it is what reads as "the slope got steeper".
    ///
    /// 🔴 The source heightmap is ALWAYS the pack original, never the copy. That makes re-running with a
    /// different k correct rather than compounding, and leaves the pack's own demo scenes untouched.
    /// </summary>
    public static class ExteriorIslandTerrain
    {
        private const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";
        private const string PackTerrainPath =
            "Assets/LowPolyTropicalEnvironment_LITE/Terrain/TE_Lite_Terrain.asset";
        private const string GeneratedFolder = "Assets/Generated/Exterior";
        private const string CopyTerrainPath = GeneratedFolder + "/ExteriorIsland_Terrain.asset";

        /// <summary>
        /// Exaggeration of the submerged band. 1 = the pack's own profile, 2 = 28.7 degrees at the
        /// shoreline, 4.3 = about 50. One number - retune it from the review captures.
        /// </summary>
        private const float SubmergedExaggeration = 2.0f;

        /// <summary>Name of the jetty whose shallow landing must survive the steepening.</summary>
        private const string PierName = "Pier_02";

        /// <summary>Inside this radius of the pier the original heights are kept exactly.</summary>
        private const float PierKeepRadius = 6f;

        /// <summary>Beyond this radius the remap applies in full; between the two it ramps.</summary>
        private const float PierBlendRadius = 12f;

        [MenuItem("Tools/Exterior/섬 수중 경사 리맵 (MainScene_final)")]
        public static void ApplyInteractive()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != PlayScenePath)
            {
                Debug.LogError($"open {PlayScenePath} first - the island lives in the play scene only");
                return;
            }
            Apply();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        public static void ApplyMainSceneFinalBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);
                Apply();

                UnityEngine.SceneManagement.Scene scene =
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveOpenScenes())
                    throw new InvalidOperationException("save refused");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveOpenScenes();

                Debug.Log("ISLAND_TERRAIN PASS");
            }
            catch (Exception exception)
            {
                Debug.LogError($"ISLAND_TERRAIN FAIL {exception}");
                EditorApplication.Exit(1);
            }
        }

        private static void Apply()
        {
            var pack = AssetDatabase.LoadAssetAtPath<TerrainData>(PackTerrainPath);
            if (pack == null)
                throw new InvalidOperationException($"no pack TerrainData at {PackTerrainPath}");

            Terrain terrain = UnityEngine.Object
                .FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.terrainData != null);
            if (terrain == null)
                throw new InvalidOperationException("no Terrain in the scene - the island is hand-placed");

            TerrainData copy = EnsureCopy();
            RepointScene(terrain, copy);

            Vector3 origin = terrain.transform.position;
            Vector3 size = copy.size;
            float baseY = origin.y;
            float seaLevel = ExteriorEnvironmentBuilder.SeaLevel;

            if (seaLevel <= baseY)
                throw new InvalidOperationException(
                    $"terrain base y={baseY:0.##} is at or above sea level {seaLevel:0.##} - " +
                    "the island is fully dry and there is no submerged band to remap");

            int resolution = pack.heightmapResolution;
            float[,] source = pack.GetHeights(0, 0, resolution, resolution);
            var result = new float[resolution, resolution];

            bool pierFound = TryFindPier(out Vector3 pierPosition);
            if (!pierFound)
                Debug.LogWarning($"EXTERIOR island: no '{PierName}' in the scene, so no shallow landing " +
                                 "is being protected. If the jetty still exists under another name its " +
                                 "posts will end up over the drop-off.");

            float band = seaLevel - baseY;
            int remapped = 0;
            int protectedSamples = 0;

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float normalized = source[z, x];
                    float worldY = baseY + normalized * size.y;
                    if (worldY >= seaLevel)
                    {
                        result[z, x] = normalized;
                        continue;
                    }

                    float t = Mathf.Clamp01((seaLevel - worldY) / band);
                    float g = 1f - Mathf.Pow(1f - t, SubmergedExaggeration);
                    float steepened = (seaLevel - band * g - baseY) / size.y;

                    float weight = 1f;
                    if (pierFound)
                    {
                        float worldX = origin.x + x / (float)(resolution - 1) * size.x;
                        float worldZ = origin.z + z / (float)(resolution - 1) * size.z;
                        float distance = new Vector2(worldX - pierPosition.x, worldZ - pierPosition.z).magnitude;
                        weight = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(PierKeepRadius, PierBlendRadius, distance));
                        if (weight < 0.999f)
                            protectedSamples++;
                    }

                    result[z, x] = Mathf.Clamp01(Mathf.Lerp(normalized, steepened, weight));
                    remapped++;
                }
            }

            copy.SetHeights(0, 0, result);
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssets();

            ReportProfile(source, result, baseY, size.y, seaLevel, remapped, protectedSamples, pierFound, pierPosition);
        }

        /// <summary>
        /// Copies the pack TerrainData once. Heights are rewritten from the pack original on every run,
        /// so an existing copy is reused rather than recreated - that keeps the scene's references and
        /// the asset GUID stable.
        /// </summary>
        private static TerrainData EnsureCopy()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
                AssetDatabase.CreateFolder("Assets/Generated", "Exterior");

            var existing = AssetDatabase.LoadAssetAtPath<TerrainData>(CopyTerrainPath);
            if (existing != null)
                return existing;

            if (!AssetDatabase.CopyAsset(PackTerrainPath, CopyTerrainPath))
                throw new InvalidOperationException($"could not copy {PackTerrainPath} -> {CopyTerrainPath}");
            AssetDatabase.ImportAsset(CopyTerrainPath);

            var copy = AssetDatabase.LoadAssetAtPath<TerrainData>(CopyTerrainPath);
            if (copy == null)
                throw new InvalidOperationException($"copy at {CopyTerrainPath} did not import as TerrainData");
            Debug.Log($"EXTERIOR island: created project-owned TerrainData copy at {CopyTerrainPath}. " +
                      "The pack asset is left untouched so its own demo scenes still render.");
            return copy;
        }

        /// <summary>
        /// Points BOTH the renderer and the collider at the copy. Setting only one leaves the visible
        /// island and the collision island describing different shapes.
        /// </summary>
        private static void RepointScene(Terrain terrain, TerrainData copy)
        {
            if (terrain.terrainData != copy)
            {
                terrain.terrainData = copy;
                EditorUtility.SetDirty(terrain);
            }

            var collider = terrain.GetComponent<TerrainCollider>();
            if (collider == null)
            {
                Debug.LogWarning("EXTERIOR island: the Terrain has no TerrainCollider, so only the " +
                                 "renderer was repointed.");
                return;
            }
            if (collider.terrainData == copy)
                return;
            collider.terrainData = copy;
            EditorUtility.SetDirty(collider);
        }

        private static bool TryFindPier(out Vector3 position)
        {
            GameObject pier = UnityEngine.Object
                .FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.name == PierName);
            position = pier != null ? pier.transform.position : Vector3.zero;
            return pier != null;
        }

        private static void ReportProfile(float[,] before, float[,] after, float baseY, float sizeY,
            float seaLevel, int remapped, int protectedSamples, bool pierFound, Vector3 pierPosition)
        {
            int resolution = before.GetLength(0);
            float beforeMax = float.NegativeInfinity;
            float afterMax = float.NegativeInfinity;
            int dryBefore = 0;
            int dryAfter = 0;
            float seaNormalized = (seaLevel - baseY) / sizeY;

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    beforeMax = Mathf.Max(beforeMax, before[z, x]);
                    afterMax = Mathf.Max(afterMax, after[z, x]);
                    if (before[z, x] > seaNormalized) dryBefore++;
                    if (after[z, x] > seaNormalized) dryAfter++;
                }
            }

            int total = resolution * resolution;
            Debug.Log($"EXTERIOR island terrain: k={SubmergedExaggeration:0.##}, " +
                      $"base y={baseY:0.##}, sea={seaLevel:0.##}, " +
                      $"peak {baseY + beforeMax * sizeY:0.###} -> {baseY + afterMax * sizeY:0.###} m " +
                      "(must be unchanged), " +
                      $"dry samples {dryBefore} -> {dryAfter} of {total} (must be unchanged), " +
                      $"{remapped} submerged samples remapped, " +
                      (pierFound
                          ? $"{protectedSamples} kept shallow around {PierName} at " +
                            $"({pierPosition.x:0.##}, {pierPosition.y:0.##}, {pierPosition.z:0.##})"
                          : "no pier protection"));

            if (!Mathf.Approximately(beforeMax, afterMax) || dryBefore != dryAfter)
            {
                Debug.LogError("ISLAND_TERRAIN: the above-water surface MOVED. The remap is supposed to " +
                               "be the identity for every sample at or above sea level - the dry " +
                               "silhouette and the props standing on it depend on that.");
            }
        }
    }
}
