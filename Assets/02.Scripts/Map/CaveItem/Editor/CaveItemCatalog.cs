using System.Collections.Generic;
using UnityEngine;

namespace CaveItem.EditorTools
{
    /// <summary>
    /// Per-species prefab path and default placement behaviour.
    ///
    /// Every number here was measured by <see cref="CaveItemProbe"/> against the real prefabs rather than
    /// read off an inspector, because four of the eight bake their size into the root scale and three
    /// carry a MeshCollider or a nested FBX whose extents are not in the prefab YAML at all.
    ///
    /// Sizes below are at the prefab's own authored scale, which is what the scene will show.
    /// </summary>
    public static class CaveItemCatalog
    {
        public sealed class Species
        {
            public CaveItemKind kind;
            public string prefabPath;
            public CaveItemAnchor anchor;
            public CaveItemOrientation orientation;

            /// <summary>Default metres along the surface normal. Surface anchors only.</summary>
            public float surfaceOffset;

            public Vector3 scale = Vector3.one;

            /// <summary>
            /// True for anything the player picks up with E. These must be on layer 7 and must clear the
            /// shell, or PlayerInteractor never offers them.
            /// </summary>
            public bool isPickup;

            /// <summary>
            /// Radius used for mutual spacing and for the Z6 dodge-channel measurement. For creatures
            /// this is the body, not the patrol range - patrol is AI behaviour, not occupied space.
            /// </summary>
            public float clearanceRadius;

            /// <summary>
            /// SurfaceFrame orientation only. 0 stands the object world-upright, 1 tips it fully onto
            /// the surface normal, and values between blend.
            ///
            /// Borrowed from <see cref="CaveBlockout.Decor.CaveDecorPaletteEntry.normalAlignment"/>,
            /// which exists because a prop that slavishly follows a 40-degree wall normal reads as glued
            /// to the wall rather than resting against it. A cylinder lying on a slope is the case that
            /// needs it most: fully aligned it looks welded, fully upright it looks like it is levitating
            /// through the rock.
            /// </summary>
            public float normalAlignment = 1f;

            /// <summary>Measured extents at <see cref="scale"/>, for the report only.</summary>
            public Vector3 measuredSize;

            /// <summary>How far the visual hangs below the pivot at <see cref="scale"/>. Report only.</summary>
            public float measuredDropBelowPivot;
        }

        /// <summary>Layer PlayerInteractor scans. Anything else is invisible to pickup.</summary>
        public const int InteractionLayer = 7;

        private static readonly Dictionary<CaveItemKind, Species> Table = Build();

        public static Species Get(CaveItemKind kind) => Table.TryGetValue(kind, out Species s) ? s : null;

        public static IEnumerable<Species> All => Table.Values;

        private static Dictionary<CaveItemKind, Species> Build()
        {
            var table = new Dictionary<CaveItemKind, Species>();

            void Add(Species species) => table[species.kind] = species;

            Add(new Species
            {
                kind = CaveItemKind.OxygenTank,
                prefabPath = "Assets/03.Prefabs/OxygenItem.prefab",
                anchor = CaveItemAnchor.Surface,
                orientation = CaveItemOrientation.SurfaceFrame,
                // Pivot is at the base and the tank is only half a metre tall, so a small lift is enough
                // to keep it off a noisy shell face without it visibly hovering.
                surfaceOffset = 0.20f,
                // a cylinder should read as resting, mostly upright
                normalAlignment = 0.35f,
                isPickup = true,
                clearanceRadius = 0.35f,
                measuredSize = new Vector3(0.237f, 0.493f, 0.192f),
                measuredDropBelowPivot = 0f
            });

            Add(new Species
            {
                kind = CaveItemKind.Rope,
                prefabPath = "Assets/03.Prefabs/RopeItem.prefab",
                anchor = CaveItemAnchor.Surface,
                orientation = CaveItemOrientation.SurfaceFrame,
                // The one species where the collider and the mesh disagree: the coil renders 0.2 m tall
                // with its pivot at the base, but the CapsuleCollider is centred on the pivot and reaches
                // 0.646 m BELOW it. Offsetting by the visual would bury half the collider in rock and the
                // pickup ray would be blocked by the shell every time.
                surfaceOffset = 0.75f,
                // same as the tank: a coil settles, it does not cling
                normalAlignment = 0.30f,
                isPickup = true,
                clearanceRadius = 0.70f,
                measuredSize = new Vector3(0.647f, 0.199f, 0.649f),
                measuredDropBelowPivot = 0.646f
            });

            Add(new Species
            {
                kind = CaveItemKind.Gun,
                prefabPath = "Assets/03.Prefabs/Gun.prefab",
                anchor = CaveItemAnchor.Surface,
                orientation = CaveItemOrientation.SurfaceFrame,
                surfaceOffset = 0.20f,
                // wedged into rock, half leaning
                normalAlignment = 0.50f,
                isPickup = true,
                clearanceRadius = 0.35f,
                measuredSize = new Vector3(0.159f, 0.354f, 0.401f),
                measuredDropBelowPivot = 0f
            });

            Add(new Species
            {
                kind = CaveItemKind.Hammer,
                prefabPath = "Assets/03.Prefabs/Hammer.prefab",
                anchor = CaveItemAnchor.SubmarineInterior,
                orientation = CaveItemOrientation.WorldUpright,
                isPickup = true,
                clearanceRadius = 0.30f,
                measuredSize = new Vector3(0.256f, 0.630f, 0.159f),
                measuredDropBelowPivot = 0f
            });

            Add(new Species
            {
                kind = CaveItemKind.Urchin,
                prefabPath = "Assets/03.Prefabs/Enemy/Urchin.prefab",
                anchor = CaveItemAnchor.Surface,
                orientation = CaveItemOrientation.SurfaceFrame,
                // Slightly embedded on purpose: an urchin reads as growing out of the rock, not resting
                // on it. Its trigger sphere still protrudes, which is all the damage tick needs.
                surfaceOffset = 0.15f,
                // The mesh is 10 cm across at scale 1. The scene's old instance used scale 3, giving a
                // life-sized ~32 cm urchin - and the review capture showed that at that size it reads as
                // a speck of dirt on the rock, in clear water, before any of the zone fog is applied.
                //
                // An invisible contact hazard that ticks damage is an unfair one, so this is oversized
                // on purpose: 6 gives a ~64 cm urchin that is legible at swimming distance.
                scale = Vector3.one * 6f,
                // a spiky ball growing out of the rock - full alignment is what sells it
                normalAlignment = 1.0f,
                isPickup = false,
                clearanceRadius = 0.50f,
                measuredSize = new Vector3(0.642f, 0.594f, 0.678f),
                measuredDropBelowPivot = 0.294f
            });

            Add(new Species
            {
                kind = CaveItemKind.Octopus,
                prefabPath = "Assets/03.Prefabs/Enemy/Octopus.prefab",
                anchor = CaveItemAnchor.Surface,
                // World-upright, not surface-aligned: EnemyNavigator drives this thing and would fight a
                // rotation borrowed from a 29-degree wall.
                orientation = CaveItemOrientation.WorldUpright,
                // The body hangs 0.74 m below the pivot, and OctopusSO.patrolWallBuffer is 1.5 m, so
                // anything less than about 2.3 m starts it already inside its own wall-avoidance margin.
                surfaceOffset = 2.5f,
                isPickup = false,
                clearanceRadius = 1.0f,
                measuredSize = new Vector3(1.185f, 1.341f, 1.418f),
                measuredDropBelowPivot = 0.743f
            });

            Add(new Species
            {
                kind = CaveItemKind.Shark,
                prefabPath = "Assets/03.Prefabs/Enemy/Shark.prefab",
                anchor = CaveItemAnchor.Centerline,
                orientation = CaveItemOrientation.AlongTunnel,
                // MAP_GUIDE calls Z6's shark the rescaled final threat, so it is deliberately larger than
                // the default. Uniform 1.5 gives a ~14 m animal in a chamber with a ~25 m radius.
                //
                // The scene's existing instance used a z-only 2.388, which stretches length without width
                // and reads as a dragged handle rather than a decision; a non-uniform scale also makes the
                // CapsuleCollider behave unintuitively, since Unity takes radius from the largest
                // perpendicular axis and height from the direction axis.
                scale = Vector3.one * 1.5f,
                isPickup = false,
                // Half the measured VISUAL width (8.418 m at this scale), not the collider capsule.
                //
                // The capsule is r 0.75 h 10.5 here, and using it was wrong: containment passed for a
                // shark whose 8.4 m body was buried in a wall, because the check only ever asked whether
                // a 0.75 m cylinder fitted. What occupies space is the animal you can see.
                //
                // The 25 m patrolRadius is AI reach rather than occupied volume, and is still not used.
                clearanceRadius = 4.2f,
                measuredSize = new Vector3(8.418f, 6.089f, 13.973f),
                measuredDropBelowPivot = 2.799f
            });

            Add(new Species
            {
                kind = CaveItemKind.Tornado,
                prefabPath = "Assets/03.Prefabs/Obstacle/Tonado.prefab",
                anchor = CaveItemAnchor.Centerline,
                // Upright with no tunnel alignment: the particle systems are authored about world Y, and
                // tipping the emitter over turns a water column into a sideways smear.
                orientation = CaveItemOrientation.WorldUpright,
                isPickup = false,
                // Whirlpool.outerRadius. This has no renderer or collider - the pull field IS the object,
                // so this is the only number that describes its footprint.
                clearanceRadius = 7f,
                measuredSize = Vector3.zero,
                measuredDropBelowPivot = 0f
            });

            return table;
        }
    }
}
