using System.Collections.Generic;
using System.IO;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CaveBlockout.Editor.Decor
{
    /// <summary>
    /// Turns the raw CaveAsset FBX library into something that can be scattered by the hundred:
    /// corrected import settings, one prefab per prop with a normalised pivot and scale, and a
    /// populated decor palette.
    ///
    /// Idempotent. Prefabs are overwritten in place so their GUIDs - and therefore every palette
    /// reference to them - survive a rerun.
    /// </summary>
    public static class CaveDecorAssetPrep
    {
        /// <summary>URP/Lit. Looked up by GUID because Shader.Find depends on the shader being loaded.</summary>
        private const string UrpLitShaderGuid = "933532a4fcc9baf4fa0491de14d08ed7";

        private const int TargetTextureSize = 1024;

        [MenuItem("Tools/Underwater Cave/Decor/1 - 에셋 준비 (임포트 설정 + 프리팹)")]
        public static void PrepareInteractive()
        {
            Debug.Log(Prepare());
        }

        public static string Prepare()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE DECOR ASSET PREP =====");

            // Deliberately not wrapped in StartAssetEditing/StopAssetEditing. SaveAndReimport queues an
            // import, and inside that block the importer settings are not guaranteed to have been
            // written before the queued import runs. This is a one-off setup pass, so paying for ~39
            // individual reimports is the right trade against silently importing with stale settings.
            //
            // Models first, then textures. ConfigureModels is what makes the embedded textures exist as
            // assets at all; running ConfigureTextures ahead of it would walk a library that is still
            // missing most of its normal maps.
            ConfigureModels(report);
            AssetDatabase.Refresh();
            ConfigureTextures(report);
            AssetDatabase.Refresh();

            ConfigureMaterials(report);
            EnsureFallbackMaterial(report);
            BuildEmissiveVariants(report);
            Dictionary<string, MeasuredPrefab> built = BuildPrefabs(report);
            BuildDecorSet(built, report);

            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        // ── import settings ───────────────────────────────────────────────

        private static void ConfigureTextures(StringBuilder report)
        {
            int normalsFixed = 0;
            int resized = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { CaveDecorCatalog.CaveAssetRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                bool dirty = false;
                bool isNormal = Path.GetFileNameWithoutExtension(path)
                    .IndexOf("normal", System.StringComparison.OrdinalIgnoreCase) >= 0;

                // Every extracted normal map imports as textureType Default with sRGB on, while the
                // material has _NORMALMAP enabled and samples it through _BumpMap. That means the
                // whole library has been lit off gamma-decoded, unpacked normals since import.
                if (isNormal && importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.sRGBTexture = false;
                    normalsFixed++;
                    dirty = true;
                }

                // These are per-asset baked atlases on faceted low-poly meshes, not tiling detail
                // maps. 2K each across the library costs far more than it shows.
                if (importer.maxTextureSize > TargetTextureSize)
                {
                    importer.maxTextureSize = TargetTextureSize;
                    resized++;
                    dirty = true;
                }

                if (dirty)
                    importer.SaveAndReimport();
            }

            report.AppendLine($"textures: normalMapTypeFixed={normalsFixed} downscaledTo{TargetTextureSize}={resized}");
        }

        private static void ConfigureModels(StringBuilder report)
        {
            int lodEnabled = 0;
            int materialsExtracted = 0;
            int total = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CaveDecorCatalog.CaveAssetRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                    continue;

                total++;
                bool dirty = false;

                if (!importer.generateMeshLods)
                {
                    // Unity 6.3 mesh LOD: levels live inside the mesh asset, so no LODGroup component
                    // and no extra scene objects per instance. If the generated levels turn out to
                    // wreck the faceted silhouettes, the fallback is authored LODGroup prefabs instead.
                    importer.generateMeshLods = true;
                    lodEnabled++;
                    dirty = true;
                }

                if (ForceExternalMaterials(importer))
                {
                    materialsExtracted++;
                    dirty = true;
                }

                if (dirty)
                    importer.SaveAndReimport();
            }

            report.AppendLine($"models: meshLodEnabled={lodEnabled}/{total} materialLocationFixed={materialsExtracted}/{total}");
        }

        /// <summary>
        /// Forces embedded FBX media out into a real .fbm folder, and fixes what the importer does when
        /// it cannot find a texture.
        ///
        /// Every VARCO export references its atlas as the relative path "model.fbm/diffuse.png" - the
        /// same string in all 39 files. With materialLocation InPrefab the embedded PNG is never
        /// extracted, so that reference resolves to nothing; materialSearch RecursiveUp then walks up
        /// the folder tree looking for any file called diffuse.png and finds a *different* asset's
        /// baked atlas, because the library has fifteen of them. The prop then renders wearing another
        /// prop's texture stretched across unrelated UVs.
        ///
        /// That is not a hypothetical: it is the state the whole VARCO/ drop imported in, and it also
        /// explains "Blue Geometric Rock" and "Blue Faceted Arch", the two entries this catalogue
        /// documents as shipping "without a .fbm folder or an extracted material". Their textures were
        /// inside the file the entire time. External + BasedOnModelNameAndMaterialName + Local makes
        /// each model extract its own atlas next to itself and write its own material, which is exactly
        /// the layout the fifteen correctly-imported assets already have.
        /// </summary>
        private static bool ForceExternalMaterials(ModelImporter importer)
        {
            if (importer.materialLocation == ModelImporterMaterialLocation.External &&
                importer.materialName == ModelImporterMaterialName.BasedOnModelNameAndMaterialName &&
                importer.materialSearch == ModelImporterMaterialSearch.Local)
                return false;

            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.materialName = ModelImporterMaterialName.BasedOnModelNameAndMaterialName;
            importer.materialSearch = ModelImporterMaterialSearch.Local;
            return true;
        }

        private static void ConfigureMaterials(StringBuilder report)
        {
            int instancingEnabled = 0;
            string[] roots = { CaveDecorCatalog.CaveAssetRoot, CaveDecorCatalog.ArtPassMaterialRoot };

            foreach (string guid in AssetDatabase.FindAssets("t:Material", roots))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material.enableInstancing)
                    continue;

                // Hundreds of copies of one mesh and one material is the exact case GPU instancing
                // exists for. Static batching would be the wrong answer here - it would duplicate the
                // combined mesh data per batch for meshes this heavy.
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                instancingEnabled++;
            }

            report.AppendLine($"materials: gpuInstancingEnabled={instancingEnabled}");
        }

        private static void EnsureFallbackMaterial(StringBuilder report)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(CaveDecorCatalog.FallbackMaterialPath) != null)
            {
                report.AppendLine("fallbackMaterial: already present");
                return;
            }

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(UrpLitShaderGuid))
                            ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                report.AppendLine("fallbackMaterial: FAILED - URP/Lit shader not found");
                return;
            }

            EnsureFolder(CaveDecorCatalog.ArtPassMaterialRoot);

            // Blue Faceted Arch and Blue Geometric Rock ship without a .fbm folder or an extracted
            // material. Borrowing another asset's material is not an option: each CaveAsset carries
            // its own baked atlas, so a foreign texture would smear across unrelated UVs. A flat dark
            // navy matches the style contract's "60% dark desaturated blue-gray rock".
            var material = new Material(shader) { name = "CaveDecor_DarkNavy" };
            material.SetColor("_BaseColor", new Color(0.06f, 0.08f, 0.14f, 1f));
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;
            AssetDatabase.CreateAsset(material, CaveDecorCatalog.FallbackMaterialPath);
            report.AppendLine($"fallbackMaterial: created {CaveDecorCatalog.FallbackMaterialPath}");
        }

        /// <summary>
        /// Generates the zone-tinted glowing materials declared in the catalogue.
        ///
        /// Each one is built against its own asset's imported textures, because every CaveAsset carries
        /// a separately baked atlas - one shared glow material would smear a foreign bake across
        /// unrelated UVs. Overwritten in place on rerun so palette references survive.
        /// </summary>
        private static void BuildEmissiveVariants(StringBuilder report)
        {
            int created = 0;
            int updated = 0;
            int skipped = 0;

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(UrpLitShaderGuid))
                            ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                report.AppendLine("emissiveVariants: FAILED - URP/Lit shader not found");
                return;
            }

            EnsureFolder(CaveDecorCatalog.ArtPassMaterialRoot);

            foreach (CaveDecorCatalog.Entry entry in CaveDecorCatalog.Entries)
            {
                if (entry.emissiveVariants == null)
                    continue;

                Material source = FindImportedMaterial(CaveDecorCatalog.FbxPath(entry));

                foreach (CaveDecorCatalog.EmissiveVariant variant in entry.emissiveVariants)
                {
                    // The one rule the art guide states outright: Z4 has no glowing life, minerals or
                    // ambient light at all. Enforced here rather than trusted to review.
                    if (variant.zoneId == "Z4")
                    {
                        report.AppendLine($"emissiveVariants: REFUSED {entry.fbxName} - Z4 must have zero emission");
                        skipped++;
                        continue;
                    }

                    if (source == null)
                    {
                        report.AppendLine($"emissiveVariants: SKIP {entry.fbxName} - no imported material to copy textures from");
                        skipped++;
                        continue;
                    }

                    string path = variant.MaterialPath(entry.fbxName);
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    bool isNew = material == null;
                    if (isNew)
                    {
                        material = new Material(shader);
                        AssetDatabase.CreateAsset(material, path);
                    }

                    material.shader = shader;
                    CopyTexture(source, material, "_BaseMap");
                    CopyTexture(source, material, "_BumpMap");
                    if (material.GetTexture("_BumpMap") != null)
                        material.EnableKeyword("_NORMALMAP");

                    material.SetColor("_BaseColor", variant.baseTint);
                    material.SetFloat("_Smoothness", variant.smoothness);
                    material.SetFloat("_Metallic", 0f);
                    material.SetColor("_EmissionColor", variant.emission);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    material.enableInstancing = true;

                    EditorUtility.SetDirty(material);
                    if (isNew) created++;
                    else updated++;
                }
            }

            report.AppendLine($"emissiveVariants: created={created} updated={updated} skipped={skipped}");
        }

        /// <summary>The material the model importer wrote out for an FBX, or null if it has none.</summary>
        private static Material FindImportedMaterial(string modelPath)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is Material embedded)
                    return embedded;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
                return null;

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null)
                        return material;
                }
            }

            return null;
        }

        private static void CopyTexture(Material source, Material destination, string property)
        {
            if (source.HasProperty(property) && destination.HasProperty(property))
                destination.SetTexture(property, source.GetTexture(property));
        }

        // ── prefabs ───────────────────────────────────────────────────────

        public readonly struct MeasuredPrefab
        {
            public readonly GameObject prefab;
            public readonly float sourceMaxDimension;
            public readonly float normalisedRadius;
            public readonly int triangles;

            public MeasuredPrefab(GameObject prefab, float sourceMaxDimension, float normalisedRadius, int triangles)
            {
                this.prefab = prefab;
                this.sourceMaxDimension = sourceMaxDimension;
                this.normalisedRadius = normalisedRadius;
                this.triangles = triangles;
            }
        }

        private static Dictionary<string, MeasuredPrefab> BuildPrefabs(StringBuilder report)
        {
            var built = new Dictionary<string, MeasuredPrefab>();
            EnsureFolder(CaveDecorCatalog.PrefabRoot);

            // An empty additive scene keeps the temporary rigs out of whatever the user has open. It is
            // a real scene rather than EditorSceneManager.NewPreviewScene because measurement reads
            // Renderer.bounds, which wants a loaded scene.
            //
            // Unity refuses to add a scene while an untitled unsaved one is open, which is exactly the
            // state batch mode starts in, so fall back to the active scene there.
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
                report.AppendLine("prefabs: measuring in the active scene (could not open a scratch scene)");
            }

            try
            {
                foreach (CaveDecorCatalog.Entry entry in CaveDecorCatalog.Entries)
                {
                    string fbxPath = CaveDecorCatalog.FbxPath(entry);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                    if (model == null)
                    {
                        report.AppendLine($"prefab SKIP {entry.fbxName}: fbx not found at {fbxPath}");
                        continue;
                    }

                    if (TryBuildPrefab(entry, model, workspace, out MeasuredPrefab measured, out string failure))
                    {
                        built[CaveDecorCatalog.ToPaletteId(entry.fbxName)] = measured;
                        report.AppendLine(
                            $"prefab OK   {entry.fbxName}: sourceMaxDim={measured.sourceMaxDimension:0.####} " +
                            $"normaliseScale={1f / measured.sourceMaxDimension:0.##} " +
                            $"radius@1m={measured.normalisedRadius:0.###} tris={measured.triangles}");
                    }
                    else
                    {
                        report.AppendLine($"prefab FAIL {entry.fbxName}: {failure}");
                    }
                }
            }
            finally
            {
                if (usingScratchScene)
                    EditorSceneManager.CloseScene(workspace, true);
            }

            foreach (string excluded in CaveDecorCatalog.Excluded)
                report.AppendLine($"prefab EXCLUDED {excluded}: multi-object export, needs manual split");

            return built;
        }

        private static bool TryBuildPrefab(CaveDecorCatalog.Entry entry, GameObject model, Scene workspace,
            out MeasuredPrefab measured, out string failure)
        {
            measured = default;
            failure = null;

            var root = new GameObject(CaveDecorCatalog.ToPaletteId(entry.fbxName));
            SceneManager.MoveGameObjectToScene(root, workspace);

            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, workspace);
                instance.transform.SetParent(root.transform, false);

                if (!TryMeasure(root, out Bounds bounds, out int triangles))
                {
                    failure = "no readable mesh bounds";
                    return false;
                }

                float maxDimension = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (maxDimension <= 1e-6f)
                {
                    failure = "degenerate bounds";
                    return false;
                }

                // Normalise so the largest dimension is exactly one unit. Placement scale then reads
                // as metres, which is what makes the catalogue's size column meaningful.
                float normalise = 1f / maxDimension;

                // Pivot at the base for anything that stands on a surface, at the top for anything
                // that hangs from one. Without this a prop's centre lands on the rock and half of it
                // disappears into the wall.
                float pivotY = entry.pivotAtTop ? bounds.max.y : bounds.min.y;
                Vector3 pivot = new Vector3(bounds.center.x, pivotY, bounds.center.z);

                // Multiply the model prefab's own root scale, never replace it. These FBX import with
                // a root scale of 100 against ~0.01-unit geometry; assigning Vector3.one * normalise
                // throws that factor away and leaves every prop a hundredth of its intended size.
                instance.transform.localScale = Vector3.Scale(instance.transform.localScale, Vector3.one * normalise);
                instance.transform.localPosition = -pivot * normalise;

                ApplyDefaultMaterial(entry, instance);
                if (entry.addCollider)
                    AddConvexColliders(instance);

                string prefabPath = $"{CaveDecorCatalog.PrefabRoot}/{root.name}.prefab";
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (saved == null)
                {
                    failure = "SaveAsPrefabAsset returned null";
                    return false;
                }

                float radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)) * normalise;
                measured = new MeasuredPrefab(saved, maxDimension, radius, triangles);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Bounds in <paramref name="root"/>'s space. <paramref name="root"/> sits at the origin
        /// unrotated and unscaled, so the renderer world bounds are already root-local.
        /// </summary>
        private static bool TryMeasure(GameObject root, out Bounds bounds, out int triangles)
        {
            bounds = default;
            triangles = 0;
            bool any = false;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                    triangles += filter.sharedMesh.triangles.Length / 3;
            }

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any && bounds.size.sqrMagnitude > 1e-12f;
        }

        private static void ApplyDefaultMaterial(CaveDecorCatalog.Entry entry, GameObject instance)
        {
            if (string.IsNullOrEmpty(entry.defaultMaterial))
                return;

            var material = AssetDatabase.LoadAssetAtPath<Material>(entry.defaultMaterial);
            if (material == null)
                return;

            CaveDecorSpawner.ApplyMaterial(instance, material);
        }

        private static void AddConvexColliders(GameObject instance)
        {
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
                    continue;

                MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = true;
            }
        }

        // ── decor set ─────────────────────────────────────────────────────

        private static void BuildDecorSet(Dictionary<string, MeasuredPrefab> built, StringBuilder report)
        {
            EnsureFolder(Path.GetDirectoryName(CaveDecorCatalog.DecorSetPath).Replace('\\', '/'));

            var set = AssetDatabase.LoadAssetAtPath<CaveDecorSet>(CaveDecorCatalog.DecorSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<CaveDecorSet>();
                AssetDatabase.CreateAsset(set, CaveDecorCatalog.DecorSetPath);
                report.AppendLine($"decorSet: created {CaveDecorCatalog.DecorSetPath}");
            }

            // Rebuild the palette but never touch placements - a rerun of asset prep must not throw
            // away authored dressing.
            List<CaveDecorPaletteEntry> palette = set.EditablePalette;
            palette.Clear();

            foreach (CaveDecorCatalog.Entry entry in CaveDecorCatalog.Entries)
            {
                string id = CaveDecorCatalog.ToPaletteId(entry.fbxName);
                if (!built.TryGetValue(id, out MeasuredPrefab measured))
                    continue;

                if (entry.placementDisabled)
                {
                    report.AppendLine($"decorSet: '{id}' held back - prefab kept, not offered for placement");
                    continue;
                }

                var paletteEntry = new CaveDecorPaletteEntry
                {
                    id = id,
                    prefab = measured.prefab,
                    allowedSurfaces = entry.surfaces,
                    zones = new List<string>(entry.zones),
                    weight = Mathf.Max(1f, entry.countPerZone),
                    scaleRange = entry.sizeMetres,
                    boundingRadius = measured.normalisedRadius,
                    minSpacing = entry.sizeMetres.y * 1.5f,
                    normalAlignment = entry.normalAlignment,
                    maxTiltDegrees = entry.maxTiltDegrees,
                    autoScatterCountPerZone = entry.countPerZone,
                    defaultMaterialOverride = null,
                    zoneMaterialOverrides = new List<CaveDecorZoneMaterial>()
                };

                // The default material is already baked into the prefab; only the per-zone swaps have
                // to be applied at spawn time.
                if (entry.zoneMaterials != null)
                {
                    foreach (var pair in entry.zoneMaterials)
                        AddZoneMaterial(paletteEntry, id, pair.Key, pair.Value, report);
                }

                if (entry.emissiveVariants != null)
                {
                    foreach (CaveDecorCatalog.EmissiveVariant variant in entry.emissiveVariants)
                    {
                        if (variant.zoneId == "Z4")
                            continue;
                        AddZoneMaterial(paletteEntry, id, variant.zoneId, variant.MaterialPath(entry.fbxName), report);
                    }
                }

                palette.Add(paletteEntry);
            }

            // A placement whose palette entry has gone would make the validator fail on every run and
            // could never be rebuilt, so dropping it is the only consistent outcome. Logged loudly
            // because it is the one part of asset prep that destroys authored data.
            int orphaned = set.Placements.RemoveAll(p => p == null || set.FindEntry(p.paletteId) == null);
            if (orphaned > 0)
                report.AppendLine($"decorSet: dropped {orphaned} placements with no palette entry");

            EditorUtility.SetDirty(set);
            report.AppendLine($"decorSet: palette={palette.Count} entries, placements kept={set.Placements.Count}");
        }

        private static void AddZoneMaterial(CaveDecorPaletteEntry paletteEntry, string id, string zoneId,
            string materialPath, StringBuilder report)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                report.AppendLine($"decorSet WARN {id}: missing zone material {materialPath}");
                return;
            }

            paletteEntry.zoneMaterialOverrides.Add(new CaveDecorZoneMaterial
            {
                zoneId = zoneId,
                material = material
            });
        }

        internal static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
