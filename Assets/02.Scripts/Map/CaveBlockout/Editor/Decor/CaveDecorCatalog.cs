using System.Collections.Generic;
using CaveBlockout.Decor;
using UnityEngine;

namespace CaveBlockout.Editor.Decor
{
    /// <summary>
    /// Authoring table for the CaveAsset library: which prop belongs in which zone, how big it should
    /// be, and what it is allowed to stand on.
    ///
    /// The zone assignments, per-zone counts and emissive rules are carried over from
    /// UnderwaterArtPassBuilder's AssetPlacements table, which had already been through an art review
    /// (see Docs/AI/ASSET_INVENTORY.md). What changed is the size column. That builder asked for
    /// scales of 0.8 to 3.5, but the CaveAsset FBX are exported at roughly 1/300 of playable size -
    /// the hand-placed Z1 dressing in MainMap.unity uses scales between 205 and 1427 - so those props
    /// were sub-centimetre and effectively invisible, which is the most likely explanation for that
    /// tool's "Z2-Z7 NOT VERIFIED, black frame" QA result.
    ///
    /// Sizes here are therefore metres of the prop's largest dimension. CaveDecorAssetPrep measures
    /// each mesh and bakes a normalising scale into the generated prefab so that a placement scale of
    /// 1 means one metre, whatever the source file happens to be authored in.
    /// </summary>
    public static class CaveDecorCatalog
    {
        public sealed class Entry
        {
            public string fbxName;

            /// <summary>
            /// Folder under <see cref="CaveAssetRoot"/> holding the FBX, or null for the root itself.
            /// The original library is flat; the 2026-08-11 VARCO drop arrived sorted into
            /// VARCO/Rocks, VARCO/Crystals and VARCO/Flora/*.
            /// </summary>
            public string subfolder;

            public string[] zones;
            public int countPerZone;
            public Vector2 sizeMetres;
            public CaveSurfaceKind surfaces;

            /// <summary>Where the pivot goes. Ceiling props hang, so theirs sits at the top of the bounds.</summary>
            public bool pivotAtTop;

            /// <summary>
            /// Keep the prefab, keep it out of the palette. Asset prep still generates the prefab so
            /// re-enabling is a one-line change, but the entry is not offered to the brush or the
            /// auto-scatter, and prep drops any placements that still reference it.
            /// </summary>
            public bool placementDisabled;

            [Tooltip("0 keeps the prop world-upright, 1 stands it on the surface normal.")]
            public float normalAlignment = 1f;

            public float maxTiltDegrees = 8f;

            /// <summary>
            /// Leave the generated prefab without a collider, so the player swims straight through it.
            ///
            /// Inverted from the original "addCollider", which defaulted every prop to no collision on
            /// the theory that dressing 5 m off the centre line is never touched. It is: the player
            /// hugs walls, and 30 of the 37 props were solid-looking rock you could fly through. The
            /// default is now an exact collider, and this flag marks the eleven soft species - kelp,
            /// corals, grass, plants - where swimming through is the intended behaviour.
            /// </summary>
            public bool swimThrough;

            /// <summary>Asset path of a material applied in every zone, or null to keep the imported one.</summary>
            public string defaultMaterial;

            /// <summary>zoneId to material asset path. Z4 must never appear here - blackout means zero emission.</summary>
            public Dictionary<string, string> zoneMaterials;

            /// <summary>
            /// Emissive per-zone materials for asset prep to generate, rather than hand-authored ones
            /// referenced by path. Same rule as <see cref="zoneMaterials"/>: never Z4.
            /// </summary>
            public EmissiveVariant[] emissiveVariants;
        }

        /// <summary>
        /// A zone-tinted glowing copy of an asset's own imported material.
        ///
        /// The three hand-made Z2 materials could not be shared because every CaveAsset carries its own
        /// baked atlas, so each one had to be built against its asset's textures. Generating them keeps
        /// that constraint and removes the manual step: prep copies the imported material's _BaseMap and
        /// _BumpMap, then applies the tint and emission below.
        /// </summary>
        public sealed class EmissiveVariant
        {
            public string zoneId;
            public Color baseTint;

            /// <summary>HDR - values above 1 are what make it read as a light source rather than a bright surface.</summary>
            public Color emission;

            public float smoothness = 0.3f;

            /// <summary>
            /// Modulate the emission by the asset's own albedo instead of glowing at a flat rate over
            /// the whole mesh. Costs nothing - it reuses the base map - and is the difference between
            /// a prop that has hot and cool areas and one that reads as a single cut-out colour.
            /// </summary>
            public bool emissionFollowsAlbedo;

            /// <summary>
            /// Write the generated material to this path instead of the derived one. The only reason
            /// to set it: an existing material whose GUID is already referenced by scene instances,
            /// where writing a new file would leave every one of them on the old look.
            /// </summary>
            public string materialPathOverride;

            public string MaterialPath(string fbxName)
            {
                return string.IsNullOrEmpty(materialPathOverride)
                    ? $"{ArtPassMaterialRoot}/{zoneId}_{ToPaletteId(fbxName)}_Emissive.mat"
                    : materialPathOverride;
            }
        }

        public const string CaveAssetRoot = "Assets/99.Resources/Map/CaveAsset";
        public const string ArtPassMaterialRoot = "Assets/04.Materials/CaveArtPass";
        public const string PrefabRoot = "Assets/03.Prefabs/CaveDecor";
        public const string DecorSetPath = "Assets/Settings/CaveDecor/MainMapCaveDecor.asset";
        public const string FallbackMaterialPath = ArtPassMaterialRoot + "/CaveDecor_DarkNavy.mat";

        private const string RocksFolder = "VARCO/Rocks";
        private const string CoralsFolder = "VARCO/Flora/Corals_Seaweed";
        private const string PlantsFolder = "VARCO/Flora/Plants";

        /// <summary>
        /// Present in the folder but deliberately not turned into props.
        /// "Scene to Props" is a 53 MB multi-object VARCO export that has neither an extracted texture
        /// folder nor a material; splitting it into individual props is its own job.
        /// </summary>
        public static readonly string[] Excluded =
        {
            "Scene to Props 2026-07-16 16-39"
        };

        public static IReadOnlyList<Entry> Entries => entries;

        private static readonly List<Entry> entries = new List<Entry>
        {
            // Z1 deep start - sparse dark spires and rocks
            new Entry
            {
                fbxName = "Dark Crystal Spire",
                zones = new[] { "Z1", "Z3", "Z4" },
                countPerZone = 4,
                sizeMetres = new Vector2(3f, 7f),
                surfaces = CaveSurfaceKind.Wall | CaveSurfaceKind.Ceiling,
                normalAlignment = 1f,
                maxTiltDegrees = 10f,
                // Held back on request (2026-08-10). Clear this flag and rerun asset prep to bring it
                // back; the zone, size and surface rules above are kept so nothing has to be re-derived.
                placementDisabled = true
            },
            new Entry
            {
                fbxName = "Blue Faceted Rock",
                zones = new[] { "Z1", "Z3", "Z4", "Z6" },
                countPerZone = 3,
                sizeMetres = new Vector2(2.5f, 5f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 0.75f
            },
            new Entry
            {
                fbxName = "Jagged Rock Spire",
                zones = new[] { "Z1", "Z3", "Z5" },
                countPerZone = 3,
                sizeMetres = new Vector2(3f, 6f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 0.9f
            },
            new Entry
            {
                fbxName = "Blue Faceted Rocks",
                zones = new[] { "Z1", "Z6" },
                countPerZone = 3,
                sizeMetres = new Vector2(3f, 6f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 0.7f
            },
            new Entry
            {
                fbxName = "Blue Geometric Rock",
                zones = new[] { "Z1", "Z4" },
                countPerZone = 2,
                sizeMetres = new Vector2(2.5f, 5f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 0.8f
                // Was forced to the flat navy fallback because this "imports untextured". It does not:
                // its atlas was embedded in the FBX all along and materialLocation InPrefab kept it
                // from ever being extracted. CaveDecorAssetPrep.ForceExternalMaterials fixes the import,
                // so the asset now wears its own pale blue faceted bake.
            },

            // Z2 coral basin - the only zone allowed to glow
            new Entry
            {
                fbxName = "Low Poly Blue Coral",
                swimThrough = true,
                zones = new[] { "Z2" },
                countPerZone = 8,
                sizeMetres = new Vector2(1.5f, 3.5f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                maxTiltDegrees = 6f,
                // Was a zoneMaterials pointer at a hand-made material with an empty _EmissionMap. URP/Lit
                // multiplies emission by that map, so an empty one meant every texel emitted the same
                // 2.85 and the prop rendered as a solid cyan cut-out - the same defect Z5's volcano had.
                // Reported from the scene view and then reproduced through the game camera: see
                // Artifacts/GlowVariants/A0_control__CLOSE.png, captured with ACES and Z2 grading on and
                // clipped at 0.00%, which rules out clipping and leaves shoulder compression.
                //
                // Unlike Z5 the intensity is left alone. Z5 cut to a fifth because its albedo is dark
                // volcanic rock; these corals have bright atlases, so modulation alone restores the form
                // and a cut on top made them read as ordinary dim props (A1 vs the x0.5 capture).
                emissiveVariants = new[]
                {
                    new EmissiveVariant
                    {
                        zoneId = "Z2",
                        materialPathOverride = ArtPassMaterialRoot + "/Z2_LowPolyBlueCoral_Emissive.mat",
                        baseTint = new Color(0.15f, 0.45f, 0.55f, 1f),
                        emission = new Color(0.3f, 2.7f, 2.85f, 1f),
                        emissionFollowsAlbedo = true,
                        smoothness = 0.3f
                    }
                }
            },
            new Entry
            {
                fbxName = "Violet Lowpoly Sea Fan",
                swimThrough = true,
                zones = new[] { "Z2" },
                countPerZone = 6,
                sizeMetres = new Vector2(1.5f, 3f),
                surfaces = CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                maxTiltDegrees = 6f,
                // Same conversion as Low Poly Blue Coral above; this is the magenta prop in the report.
                emissiveVariants = new[]
                {
                    new EmissiveVariant
                    {
                        zoneId = "Z2",
                        materialPathOverride = ArtPassMaterialRoot + "/Z2_VioletSeaFan_Emissive.mat",
                        baseTint = new Color(0.25f, 0.1f, 0.4f, 1f),
                        emission = new Color(1.5f, 0.375f, 2.25f, 1f),
                        emissionFollowsAlbedo = true,
                        smoothness = 0.2f
                    }
                }
            },
            new Entry
            {
                fbxName = "Blue Crystal Formation",
                zones = new[] { "Z2", "Z6" },
                countPerZone = 5,
                sizeMetres = new Vector2(2f, 4.5f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                // Z2 only. The Z6 placements keep their imported material, which does not glow.
                emissiveVariants = new[]
                {
                    new EmissiveVariant
                    {
                        zoneId = "Z2",
                        materialPathOverride = ArtPassMaterialRoot + "/Z2_BlueCrystal_Emissive.mat",
                        baseTint = new Color(0.1f, 0.35f, 0.55f, 1f),
                        emission = new Color(0.1f, 1.4f, 2f, 1f),
                        emissionFollowsAlbedo = true,
                        smoothness = 0.4f
                    }
                }
            },

            // Z3 current ravine
            new Entry
            {
                fbxName = "Blue Faceted Sheet",
                zones = new[] { "Z3", "Z6" },
                countPerZone = 3,
                sizeMetres = new Vector2(5f, 10f),
                surfaces = CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                maxTiltDegrees = 5f,
                // Removed from the map on request (2026-08-13) - it did not read well in place. Third
                // entry to take this treatment, after Dark Crystal Spire and Dark Floating Island: the
                // prefab and the rules above stay, so clearing this flag and rerunning asset prep
                // brings it back without re-deriving anything.
                placementDisabled = true
            },
            new Entry
            {
                fbxName = "Dark Floating Island",
                zones = new[] { "Z3", "Z5" },
                countPerZone = 2,
                sizeMetres = new Vector2(4f, 8f),
                surfaces = CaveSurfaceKind.Wall | CaveSurfaceKind.Ceiling,
                normalAlignment = 1f,
                // Removed from the map on request (2026-08-12). Same treatment as Dark Crystal Spire:
                // the prefab and the rules above stay, so clearing this flag and rerunning asset prep
                // brings it back without re-deriving anything.
                placementDisabled = true
            },

            // Z4 blackout fault - dark formations only, no zoneMaterials entry anywhere for Z4
            new Entry
            {
                fbxName = "Dark Blue Crystal Formation",
                zones = new[] { "Z4" },
                countPerZone = 4,
                sizeMetres = new Vector2(2f, 4f),
                surfaces = CaveSurfaceKind.Wall | CaveSurfaceKind.Ceiling,
                normalAlignment = 1f
            },
            new Entry
            {
                fbxName = "Dark Faceted Icicles",
                zones = new[] { "Z3", "Z4" },
                countPerZone = 3,
                sizeMetres = new Vector2(2.5f, 5f),
                surfaces = CaveSurfaceKind.Ceiling,
                pivotAtTop = true,
                normalAlignment = 1f,
                maxTiltDegrees = 5f,
                // The VARCO bake is a bright icy blue-white that reads as lit in a zone with no light.
                defaultMaterial = ArtPassMaterialRoot + "/Z3Z4_DarkStalactite.mat"
            },

            // Z5 thermal chimney - restrained red, never lava
            new Entry
            {
                fbxName = "Lowpoly Volcano",
                zones = new[] { "Z5" },
                countPerZone = 3,
                sizeMetres = new Vector2(4f, 9f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 0.6f,
                maxTiltDegrees = 4f,
                // Was a zoneMaterials pointer at a hand-made Z5_ThermalCrack_Emissive with no _BaseMap,
                // a near-black base colour and emission at 1.2 red. With no texture and nothing to
                // modulate it, every texel emitted the same HDR orange and the prop rendered as a flat
                // orange cut-out - reported from the scene view on 2026-08-13.
                //
                // Generated instead, which is the mechanism that exists for exactly this ("every
                // CaveAsset carries its own baked atlas"). materialPathOverride keeps the old asset
                // path so the GUID already baked into MainMap and MainScene_final instances still
                // resolves and both scenes pick the fix up without a decor rebuild.
                emissiveVariants = new[]
                {
                    new EmissiveVariant
                    {
                        zoneId = "Z5",
                        materialPathOverride = ArtPassMaterialRoot + "/Z5_ThermalCrack_Emissive.mat",
                        // Dark warm rock, not black: the atlas now carries the detail, so the tint only
                        // has to pull it towards MAP_GUIDE's "검은 화산암" without erasing it.
                        baseTint = new Color(0.42f, 0.3f, 0.26f, 1f),
                        // A fifth of the old intensity, and multiplied by the albedo, so the glow sits
                        // in the dark crevices instead of washing the whole silhouette. Z5's brief is
                        // "restrained red, never lava".
                        emission = new Color(0.55f, 0.11f, 0.02f, 1f),
                        emissionFollowsAlbedo = true,
                        smoothness = 0.15f
                    }
                }
            },
            new Entry
            {
                fbxName = "Blue Stone Platform",
                zones = new[] { "Z5", "Z6" },
                countPerZone = 3,
                sizeMetres = new Vector2(5f, 9f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 0.5f,
                maxTiltDegrees = 4f
            },
            new Entry
            {
                fbxName = "Faceted Blue Ramp",
                zones = new[] { "Z5" },
                countPerZone = 3,
                sizeMetres = new Vector2(4f, 8f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 0.6f,
                maxTiltDegrees = 4f
            },
            new Entry
            {
                fbxName = "Faceted Blue Rock",
                zones = new[] { "Z3", "Z5", "Z6" },
                countPerZone = 3,
                sizeMetres = new Vector2(2.5f, 5f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 0.75f
            },

            // Z6 exit throat used to be dressed with "Blue Faceted Arch" and "Sci-Fi Storage Tank".
            // Both were retired on 2026-08-13 at the map owner's request. The entries are deleted
            // rather than zeroed: asset prep rebuilds the palette from this list and then drops any
            // placement whose palette entry has gone, so removing them here is what actually retires
            // the species. A countPerZone = 0 entry would keep the palette id alive and re-open the
            // door for the next auto-scatter run.
            //
            // The generated prefabs are deliberately left in Assets/03.Prefabs/CaveDecor/ - nothing
            // references them any more, but deleting an asset is not what "delete these objects"
            // asked for, and keeping them makes the decision reversible.

            // ── 2026-08-11 VARCO drop: VARCO/Rocks ────────────────────────────
            //
            // Zone assignments follow ASSET_MANIFEST.csv's BIO/GEO rows and MAP_GUIDE.md line 45:
            // "Z2만 발광 산호 밀도가 높고 Z4에는 발광 생물·광물·환경광이 없어야 한다." Nothing that
            // glows, and nothing pale enough to read as lit under a torch, is allowed into Z4.
            new Entry
            {
                fbxName = "Blue Layered Rock",
                subfolder = RocksFolder,
                zones = new[] { "Z1", "Z3", "Z6" },
                countPerZone = 2,
                sizeMetres = new Vector2(2.5f, 5f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 0.7f,
                maxTiltDegrees = 6f
            },
            new Entry
            {
                fbxName = "Blue Geometric Rocks",
                subfolder = RocksFolder,
                zones = new[] { "Z1", "Z3", "Z4" },
                countPerZone = 2,
                sizeMetres = new Vector2(2f, 4f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 0.8f
            },
            new Entry
            {
                fbxName = "Stylized Veined Rock",
                subfolder = RocksFolder,
                zones = new[] { "Z3", "Z5" },
                countPerZone = 2,
                sizeMetres = new Vector2(2.5f, 5f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 0.75f
            },
            new Entry
            {
                fbxName = "Dark Rock Spike",
                subfolder = RocksFolder,
                zones = new[] { "Z1", "Z3", "Z4" },
                countPerZone = 2,
                sizeMetres = new Vector2(2f, 4.5f),
                surfaces = CaveSurfaceKind.Wall | CaveSurfaceKind.Ceiling,
                normalAlignment = 1f,
                maxTiltDegrees = 10f
            },
            new Entry
            {
                fbxName = "Faceted Ice Stone",
                subfolder = RocksFolder,
                // Its bake carries lit blue cracks. That is a light source in everything but name, so
                // it goes where glow is allowed and nowhere near the blackout fault.
                zones = new[] { "Z2", "Z6" },
                countPerZone = 2,
                sizeMetres = new Vector2(2f, 4f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 0.8f
            },
            new Entry
            {
                fbxName = "Glowing Stone Stack",
                subfolder = RocksFolder,
                zones = new[] { "Z2", "Z5" },
                countPerZone = 1,
                sizeMetres = new Vector2(1.5f, 3f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 0.6f,
                maxTiltDegrees = 5f
            },
            new Entry
            {
                fbxName = "Blue Faceted Stone",
                subfolder = RocksFolder,
                // A carved monolith, not a boulder - it reads as placed rather than fallen. Kept to the
                // two zones with a story for that: the crash site and the salvage-strewn exit.
                zones = new[] { "Z1", "Z6" },
                countPerZone = 1,
                sizeMetres = new Vector2(3f, 6f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 0.3f,
                maxTiltDegrees = 6f
            },
            new Entry
            {
                fbxName = "Blue Banded Pedestal",
                subfolder = RocksFolder,
                zones = new[] { "Z5", "Z6" },
                countPerZone = 1,
                sizeMetres = new Vector2(3f, 6f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 0.4f,
                maxTiltDegrees = 4f
            },

            // The two 50k-triangle meshes in the drop, five times everything around them. Set pieces at
            // countPerZone 1, not filler.
            new Entry
            {
                fbxName = "Stylized Rock Arch",
                subfolder = RocksFolder,
                zones = new[] { "Z2", "Z6" },
                countPerZone = 1,
                sizeMetres = new Vector2(8f, 14f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 0.4f,
                maxTiltDegrees = 4f
            },
            new Entry
            {
                fbxName = "Blue Stone Cavern",
                subfolder = RocksFolder,
                zones = new[] { "Z2", "Z6" },
                countPerZone = 1,
                sizeMetres = new Vector2(6f, 12f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 0.5f,
                maxTiltDegrees = 5f
            },

            // ── VARCO/Crystals ────────────────────────────────────────────────
            new Entry
            {
                fbxName = "Dark Crystal Cluster",
                subfolder = "VARCO/Crystals",
                // A different mesh from the held-back Dark Crystal Spire: a low dark rock base with a
                // few short shards rather than a tall pale spike, and dark enough for the blackout zone.
                zones = new[] { "Z1", "Z4" },
                countPerZone = 2,
                sizeMetres = new Vector2(2f, 4f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 0.9f
            },

            // ── VARCO/Flora/Corals_Seaweed - Z2 is the only zone that glows ───
            new Entry
            {
                fbxName = "Stylized Teal Coral",
                swimThrough = true,
                subfolder = CoralsFolder,
                zones = new[] { "Z2" },
                countPerZone = 4,
                sizeMetres = new Vector2(1.5f, 3f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                maxTiltDegrees = 6f,
                emissiveVariants = new[]
                {
                    new EmissiveVariant
                    {
                        zoneId = "Z2",
                        baseTint = new Color(0.15f, 0.45f, 0.5f, 1f),
                        emission = new Color(0.25f, 2.4f, 2.6f, 1f),
                        emissionFollowsAlbedo = true,
                        smoothness = 0.3f
                    }
                }
            },
            new Entry
            {
                fbxName = "Blue Crystal Seaweed",
                swimThrough = true,
                subfolder = CoralsFolder,
                zones = new[] { "Z2", "Z6" },
                countPerZone = 3,
                sizeMetres = new Vector2(1.5f, 3.5f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                maxTiltDegrees = 8f,
                emissiveVariants = new[]
                {
                    new EmissiveVariant
                    {
                        zoneId = "Z2",
                        baseTint = new Color(0.12f, 0.38f, 0.58f, 1f),
                        emission = new Color(0.15f, 1.6f, 2.2f, 1f),
                        emissionFollowsAlbedo = true,
                        smoothness = 0.35f
                    }
                }
            },
            new Entry
            {
                fbxName = "Colorful Stylized Coral",
                swimThrough = true,
                subfolder = CoralsFolder,
                zones = new[] { "Z2", "Z6" },
                countPerZone = 3,
                sizeMetres = new Vector2(1.5f, 3f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                maxTiltDegrees = 6f,
                emissiveVariants = new[]
                {
                    new EmissiveVariant
                    {
                        zoneId = "Z2",
                        baseTint = new Color(0.2f, 0.42f, 0.42f, 1f),
                        // Warmer than the others so the basin is not one flat cyan wash.
                        emission = new Color(1.1f, 1.9f, 1.5f, 1f),
                        emissionFollowsAlbedo = true,
                        smoothness = 0.25f
                    }
                }
            },
            new Entry
            {
                fbxName = "Stylized Green Seaweed",
                swimThrough = true,
                subfolder = CoralsFolder,
                // The plain greens carry the zones that are not allowed to glow. No emissive variant.
                zones = new[] { "Z1", "Z2", "Z3", "Z6" },
                countPerZone = 2,
                sizeMetres = new Vector2(1.5f, 3.5f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                maxTiltDegrees = 10f
            },
            new Entry
            {
                fbxName = "Stylized Red Seaweed",
                swimThrough = true,
                subfolder = CoralsFolder,
                // Z5 gets exactly one living thing. Red growth clustered at a hydrothermal vent is the
                // real-world read, and it is the only palette entry whose colour belongs to that zone
                // without being a mineral vein.
                zones = new[] { "Z3", "Z5" },
                countPerZone = 2,
                sizeMetres = new Vector2(1.2f, 2.8f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                maxTiltDegrees = 10f
            },

            // ── VARCO/Flora/Plants ────────────────────────────────────────────
            new Entry
            {
                fbxName = "Stylized Grass Patch",
                swimThrough = true,
                subfolder = PlantsFolder,
                zones = new[] { "Z1", "Z2", "Z3", "Z6" },
                countPerZone = 3,
                sizeMetres = new Vector2(2f, 4f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 1f,
                maxTiltDegrees = 12f
            },
            new Entry
            {
                fbxName = "Low Poly Green Plant",
                swimThrough = true,
                subfolder = PlantsFolder,
                zones = new[] { "Z1", "Z3", "Z6" },
                countPerZone = 2,
                sizeMetres = new Vector2(1.2f, 2.5f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                maxTiltDegrees = 12f
            },
            new Entry
            {
                fbxName = "Green Stylized Plant",
                swimThrough = true,
                subfolder = PlantsFolder,
                zones = new[] { "Z1", "Z3", "Z6" },
                countPerZone = 2,
                sizeMetres = new Vector2(1.5f, 3f),
                surfaces = CaveSurfaceKind.Floor | CaveSurfaceKind.Wall,
                normalAlignment = 1f,
                maxTiltDegrees = 12f
            },
            new Entry
            {
                fbxName = "Low Poly Yellow Plant",
                swimThrough = true,
                subfolder = PlantsFolder,
                // The only warm-yellow growth in the library, so it stays where warm light has a source:
                // the glowing basin and the daylight-fed exit throat.
                zones = new[] { "Z2", "Z6" },
                countPerZone = 2,
                sizeMetres = new Vector2(1.2f, 2.5f),
                surfaces = CaveSurfaceKind.Floor,
                normalAlignment = 1f,
                maxTiltDegrees = 10f
            }
        };

        /// <summary>Asset path of an entry's source FBX.</summary>
        public static string FbxPath(Entry entry)
        {
            return string.IsNullOrEmpty(entry.subfolder)
                ? $"{CaveAssetRoot}/{entry.fbxName}.fbx"
                : $"{CaveAssetRoot}/{entry.subfolder}/{entry.fbxName}.fbx";
        }

        /// <summary>Palette id for an FBX name: spaces and punctuation stripped, so "Sci-Fi Storage Tank" becomes "SciFiStorageTank".</summary>
        public static string ToPaletteId(string fbxName)
        {
            var builder = new System.Text.StringBuilder(fbxName.Length);
            foreach (char c in fbxName)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(c);
            }
            return builder.ToString();
        }
    }
}
