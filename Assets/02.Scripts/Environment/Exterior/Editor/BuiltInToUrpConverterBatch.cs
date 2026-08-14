using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;

namespace Varco.Exterior.EditorTools
{
    /// <summary>
    /// Upgrades the imported exterior packs' Built-In materials to URP, folder-scoped.
    ///
    /// The official batch entry (Converters.RunInBatchMode) is unusable in URP 17.3: enumerating the
    /// BuiltInToURP container instantiates every converter in it, and Base2DMaterialUpgrader has no
    /// default constructor - MissingMethodException before any work happens. This calls the same
    /// StandardUpgrader the converter would have used, directly, on exactly the materials that need
    /// it - which is also strictly safer than the project-wide converter this replaced.
    /// </summary>
    public static class BuiltInToUrpConverterBatch
    {
        private static readonly string[] Folders =
        {
            "Assets/Low-Poly Style Nature",
            "Assets/LowPolyTropicalEnvironment_LITE"
        };

        public static void ConvertMaterials()
        {
            int upgraded = 0, skipped = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:material", Folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null)
                    continue;

                string shaderName = material.shader.name;
                // "Standard" and "Standard (Specular setup)" - StandardUpgrader branches on the name.
                if (!shaderName.StartsWith("Standard"))
                {
                    skipped++;
                    continue;
                }

                new StandardUpgrader(shaderName).Upgrade(material, MaterialUpgrader.UpgradeFlags.None);
                EditorUtility.SetDirty(material);
                upgraded++;
                Debug.Log($"URP_CONVERT: {path} ({shaderName} -> {material.shader.name})");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"URP_CONVERT_MATERIALS DONE upgraded={upgraded} skipped={skipped}");
        }
    }
}
