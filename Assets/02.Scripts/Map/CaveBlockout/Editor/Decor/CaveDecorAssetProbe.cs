using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CaveBlockout.Editor.Decor
{
    /// <summary>
    /// Measures every FBX under the CaveAsset root and dumps what the catalogue needs to be authored
    /// from facts rather than from the file name: triangle count, native size, root scale, resolved
    /// material, and whether the diffuse carries an alpha channel.
    ///
    /// Written for the 2026-08-11 VARCO drop. The existing library is opaque faceted rock at a known
    /// scale; a new batch is none of those things until it has been looked at. Triangle count in
    /// particular has no gate anywhere in the decor pipeline - the &lt;50,000 test covers the cave
    /// shell, not the dressing - so a dense foliage pass is the one mistake that would pass every
    /// automated check and only show up as a frame-rate cliff.
    /// </summary>
    public static class CaveDecorAssetProbe
    {
        private const string ReportPath = "Artifacts/CaveDecor/asset-probe.tsv";
        private const string MainMapPath = "Assets/01.Scenes/MainMap.unity";

        [MenuItem("Tools/Underwater Cave/Decor/0 - 에셋 실측 (신규 FBX 조사)")]
        public static void ProbeInteractive()
        {
            Debug.Log(Probe());
        }

        public static void ProbeBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            Debug.Log(Probe());
        }

        private const string ThumbnailDirectory = "Artifacts/CaveDecor/thumbnails";
        private const int TileSize = 320;
        private const int SheetColumns = 5;

        /// <summary>
        /// Renders every model under VARCO/ to a contact sheet. Bounds tell you a prop is 1.0 x 0.17 x
        /// 1.0; they do not tell you whether that is a seagrass mat lying on the floor or a kelp frond
        /// exported on its side, and those two want opposite surface rules. The catalogue should not be
        /// authored off file names.
        /// </summary>
        public static void CaptureThumbnailsBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            Debug.Log(CaptureThumbnails(null));
        }

        public static string CaptureThumbnails(string pathFilter)
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR THUMBNAILS =====");

            var paths = new List<string>();
            foreach (string path in FindModels())
            {
                if (!string.IsNullOrEmpty(pathFilter) && !path.Contains(pathFilter))
                    continue;

                // The multi-object export is half a million triangles of unrelated props in one mesh;
                // a single framed tile of it says nothing.
                string name = Path.GetFileNameWithoutExtension(path);
                if (System.Array.IndexOf(CaveDecorCatalog.Excluded, name) >= 0)
                    continue;

                paths.Add(path);
            }

            string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string outputDirectory = Path.Combine(root, ThumbnailDirectory.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(outputDirectory);

            bool usingScratchScene = true;
            Scene workspace;
            try
            {
                workspace = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }
            catch (System.InvalidOperationException)
            {
                usingScratchScene = false;
                workspace = SceneManager.GetActiveScene();
            }

            var files = new List<string>();
            GameObject rig = null;
            RenderTexture target = null;
            Texture2D readable = null;

            try
            {
                rig = new GameObject("DecorThumbnailRig") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(rig, workspace);

                Camera camera = rig.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.05f, 0.07f, 0.11f, 1f);
                camera.orthographic = true;
                camera.orthographicSize = 0.62f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 20f;

                // Neutral white key light, not the scene's blue underwater rig: the point is to read the
                // asset's own baked albedo and silhouette, not how it will look once graded.
                Light key = rig.AddComponent<Light>();
                key.type = LightType.Directional;
                key.color = Color.white;
                key.intensity = 1.5f;
                key.shadows = LightShadows.None;

                // Three-quarter view from slightly above the horizon. High enough to read a flat mat as
                // flat, low enough that an upright frond still shows its full height.
                var orientation = Quaternion.Euler(20f, -35f, 0f);
                rig.transform.SetPositionAndRotation(orientation * (Vector3.back * 4f) + Vector3.up * 0.5f, orientation);

                target = new RenderTexture(TileSize, TileSize, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 4,
                    hideFlags = HideFlags.HideAndDontSave
                };
                target.Create();
                camera.targetTexture = target;
                readable = new Texture2D(TileSize, TileSize, TextureFormat.RGB24, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                for (int i = 0; i < paths.Count; i++)
                {
                    string name = Path.GetFileNameWithoutExtension(paths[i]);
                    if (!TryStage(paths[i], workspace, out GameObject staged))
                    {
                        report.AppendLine($"THUMB FAIL {name}");
                        continue;
                    }

                    try
                    {
                        camera.Render();
                        RenderTexture previous = RenderTexture.active;
                        RenderTexture.active = target;
                        readable.ReadPixels(new Rect(0f, 0f, TileSize, TileSize), 0, 0, false);
                        readable.Apply(false, false);
                        RenderTexture.active = previous;

                        string file = $"{i + 1:D2}_{Sanitize(name)}.png";
                        File.WriteAllBytes(Path.Combine(outputDirectory, file), readable.EncodeToPNG());
                        files.Add(file);
                        report.AppendLine($"[{i + 1:D2}] {name}");
                    }
                    finally
                    {
                        Object.DestroyImmediate(staged);
                    }
                }
            }
            finally
            {
                RenderTexture.active = null;
                if (readable != null) Object.DestroyImmediate(readable);
                if (target != null)
                {
                    if (rig != null)
                    {
                        Camera camera = rig.GetComponent<Camera>();
                        if (camera != null) camera.targetTexture = null;
                    }
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                if (rig != null) Object.DestroyImmediate(rig);
                if (usingScratchScene) EditorSceneManager.CloseScene(workspace, true);
            }

            string sheetPath = Path.Combine(outputDirectory, "contact_sheet.png");
            CreateContactSheet(outputDirectory, files, sheetPath);
            report.AppendLine($"sheet: {sheetPath} ({SheetColumns} columns, numbered left to right, top row first)");
            return report.ToString();
        }

        /// <summary>Instantiates the model normalised to one unit tall and centred on the origin.</summary>
        private static bool TryStage(string path, Scene workspace, out GameObject staged)
        {
            staged = null;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
                return false;

            var root = new GameObject("Staged") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, workspace);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, workspace);
            instance.transform.SetParent(root.transform, false);

            Bounds bounds = default;
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                bounds = any ? Encapsulate(bounds, renderer.bounds) : renderer.bounds;
                any = true;
            }

            if (!any || bounds.size.sqrMagnitude <= 1e-12f)
            {
                Object.DestroyImmediate(root);
                return false;
            }

            float normalise = 1f / Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            instance.transform.localScale = Vector3.Scale(instance.transform.localScale, Vector3.one * normalise);
            instance.transform.localPosition = -bounds.center * normalise;

            staged = root;
            return true;
        }

        private static void CreateContactSheet(string directory, List<string> files, string outputPath)
        {
            if (files.Count == 0)
                return;

            int rows = Mathf.CeilToInt(files.Count / (float)SheetColumns);
            var sheet = new Texture2D(SheetColumns * TileSize, rows * TileSize, TextureFormat.RGB24, false);
            var background = new Color[sheet.width * sheet.height];
            for (int i = 0; i < background.Length; i++)
                background[i] = new Color(0.03f, 0.05f, 0.07f, 1f);
            sheet.SetPixels(background);

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var tile = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    try
                    {
                        tile.LoadImage(File.ReadAllBytes(Path.Combine(directory, files[i])), false);
                        int column = i % SheetColumns;
                        int row = rows - 1 - i / SheetColumns;
                        sheet.SetPixels(column * TileSize, row * TileSize, TileSize, TileSize, tile.GetPixels());
                    }
                    finally
                    {
                        Object.DestroyImmediate(tile);
                    }
                }

                sheet.Apply(false, false);
                File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(sheet);
            }
        }

        private static string Sanitize(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            return builder.ToString();
        }

        private readonly struct Measurement
        {
            public readonly string path;
            public readonly Vector3 size;
            public readonly float rootScale;
            public readonly int triangles;
            public readonly int lodLevels;
            public readonly int renderers;
            public readonly string materials;
            public readonly string textures;

            public Measurement(string path, Vector3 size, float rootScale, int triangles, int lodLevels,
                int renderers, string materials, string textures)
            {
                this.path = path;
                this.size = size;
                this.rootScale = rootScale;
                this.triangles = triangles;
                this.lodLevels = lodLevels;
                this.renderers = renderers;
                this.materials = materials;
                this.textures = textures;
            }
        }

        public static string Probe()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR ASSET PROBE =====");

            List<string> paths = FindModels();
            report.AppendLine($"models found: {paths.Count} under {CaveDecorCatalog.CaveAssetRoot}");

            bool usingScratchScene = true;
            Scene workspace;
            try
            {
                workspace = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }
            catch (System.InvalidOperationException)
            {
                usingScratchScene = false;
                workspace = SceneManager.GetActiveScene();
                report.AppendLine("measuring in the active scene (could not open a scratch scene)");
            }

            var rows = new List<Measurement>();
            try
            {
                foreach (string path in paths)
                {
                    if (TryMeasure(path, workspace, out Measurement measurement))
                        rows.Add(measurement);
                    else
                        report.AppendLine($"PROBE FAIL {path}: no readable renderer bounds");
                }
            }
            finally
            {
                if (usingScratchScene)
                    EditorSceneManager.CloseScene(workspace, true);
            }

            AppendTable(rows, report);
            WriteTsv(rows, report);
            return report.ToString();
        }

        private static List<string> FindModels()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CaveDecorCatalog.CaveAssetRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    paths.Add(path);
            }
            paths.Sort(System.StringComparer.Ordinal);
            return paths;
        }

        private static bool TryMeasure(string path, Scene workspace, out Measurement measurement)
        {
            measurement = default;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
                return false;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, workspace);
            if (instance == null)
                return false;

            try
            {
                // Position only. These are Blender exports and the model prefab root carries the
                // Z-up-to-Y-up rotation, so clearing the rotation would measure the raw authored
                // orientation and report every upright frond as a flat mat.
                instance.transform.position = Vector3.zero;

                int triangles = 0;
                int lodLevels = 0;
                foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                        continue;
                    triangles += filter.sharedMesh.triangles.Length / 3;
                    lodLevels = Mathf.Max(lodLevels, filter.sharedMesh.lodCount);
                }

                Bounds bounds = default;
                int renderers = 0;
                var materialPaths = new List<string>();
                var texturePaths = new List<string>();

                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    bounds = renderers == 0 ? renderer.bounds : Encapsulate(bounds, renderer.bounds);
                    renderers++;

                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null)
                        {
                            AddOnce(materialPaths, "<null>");
                            continue;
                        }

                        string materialPath = AssetDatabase.GetAssetPath(material);
                        // An embedded material has the model's own path, which is the signal that
                        // materialLocation is InPrefab rather than External.
                        AddOnce(materialPaths, string.IsNullOrEmpty(materialPath)
                            ? material.name + " (unsaved)"
                            : materialPath == path
                                ? material.name + " (embedded)"
                                : materialPath);

                        DescribeTexture(material, "_BaseMap", texturePaths);
                        DescribeTexture(material, "_BumpMap", texturePaths);
                    }
                }

                if (renderers == 0 || bounds.size.sqrMagnitude <= 1e-12f)
                    return false;

                measurement = new Measurement(path, bounds.size, instance.transform.localScale.x, triangles,
                    lodLevels, renderers,
                    materialPaths.Count > 0 ? string.Join(" + ", materialPaths) : "-",
                    texturePaths.Count > 0 ? string.Join(" + ", texturePaths) : "-");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Foliage is the reason this is here. Every asset in the library so far is opaque faceted rock,
        /// so nothing in prep sets an alpha-clip mode - and a leaf card whose cutout is ignored renders
        /// as an opaque quad, which passes every gate in the pipeline and only shows up in a capture.
        /// </summary>
        private static void DescribeTexture(Material material, string property, List<string> into)
        {
            if (!material.HasProperty(property))
                return;

            var texture = material.GetTexture(property) as Texture2D;
            if (texture == null)
                return;

            string texturePath = AssetDatabase.GetAssetPath(texture);
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;

            string alpha = importer != null && importer.DoesSourceTextureHaveAlpha() ? "alpha" : "opaque";
            string type = importer != null ? importer.textureType.ToString() : "?";
            AddOnce(into, $"{property}={Path.GetFileName(texturePath)}[{texture.width}x{texture.height} {type} {alpha}]");
        }

        private static void AddOnce(List<string> list, string value)
        {
            if (!list.Contains(value))
                list.Add(value);
        }

        private static Bounds Encapsulate(Bounds bounds, Bounds other)
        {
            bounds.Encapsulate(other);
            return bounds;
        }

        private static void AppendTable(List<Measurement> rows, StringBuilder report)
        {
            rows.Sort((a, b) => b.triangles.CompareTo(a.triangles));

            int totalTriangles = 0;
            foreach (Measurement row in rows)
                totalTriangles += row.triangles;

            report.AppendLine("--- measured, heaviest first ---");
            foreach (Measurement row in rows)
            {
                report.AppendLine(
                    $"{Path.GetFileNameWithoutExtension(row.path),-32} tris={row.triangles,7} " +
                    $"size={row.size.x:0.###}x{row.size.y:0.###}x{row.size.z:0.###} " +
                    $"rootScale={row.rootScale:0.###} lod={row.lodLevels} rend={row.renderers}");
                report.AppendLine($"{"",34}mat={row.materials}");
                report.AppendLine($"{"",34}tex={row.textures}");
                report.AppendLine($"{"",34}dir={Path.GetDirectoryName(row.path)?.Replace('\\', '/')}");
            }

            report.AppendLine($"--- total source triangles: {totalTriangles} across {rows.Count} models ---");
        }

        private static void WriteTsv(List<Measurement> rows, StringBuilder report)
        {
            var tsv = new StringBuilder();
            tsv.AppendLine("path\ttriangles\tsizeX\tsizeY\tsizeZ\trootScale\tlodLevels\trenderers\tmaterials\ttextures");
            foreach (Measurement row in rows)
            {
                tsv.AppendLine($"{row.path}\t{row.triangles}\t{row.size.x:R}\t{row.size.y:R}\t{row.size.z:R}\t" +
                               $"{row.rootScale:R}\t{row.lodLevels}\t{row.renderers}\t{row.materials}\t{row.textures}");
            }

            string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(root, ReportPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, tsv.ToString());
            report.AppendLine($"wrote {path}");
        }
    }
}
