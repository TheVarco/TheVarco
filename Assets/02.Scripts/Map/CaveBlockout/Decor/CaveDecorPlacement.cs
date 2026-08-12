using System;
using UnityEngine;

namespace CaveBlockout.Decor
{
    /// <summary>Which part of the cave tube a surface belongs to, decided from the hit normal.</summary>
    [Flags]
    public enum CaveSurfaceKind
    {
        None = 0,
        Floor = 1 << 0,
        Wall = 1 << 1,
        Ceiling = 1 << 2,
        Any = Floor | Wall | Ceiling
    }

    /// <summary>
    /// One dressed prop, stored relative to the cave route rather than in world space.
    ///
    /// World coordinates would be wrong the moment the blockout is regenerated: CaveMeshGenerator
    /// rebuilds the shell from the spline plus noise, so every hand-placed rock would end up floating
    /// in open water or buried inside the wall. Storing (distance along the route, angle around the
    /// tube, offset from the surface) instead means a rebuild re-projects each prop onto whatever
    /// surface now exists at that point.
    /// </summary>
    [Serializable]
    public sealed class CaveDecorPlacement
    {
        [Tooltip("Stable identity, mirrored onto the spawned CaveDecorInstance so erase and rebuild " +
                 "can pair a scene object with its record without relying on names or ordering.")]
        public string id;

        [Tooltip("Key into CaveDecorSet.palette. Deliberately not a direct prefab reference so the " +
                 "palette can be re-pointed at new art without touching thousands of records.")]
        public string paletteId;

        [Tooltip("CaveRouteSplineDefinition.routeId - \"MainRoute\", or \"Z2_Branch\" and friends. " +
                 "A bare spline index would be ambiguous because the branches live on a second " +
                 "CaveRoute component with their own indices starting at 0.")]
        public string routeId = "MainRoute";

        [Tooltip("Metres travelled along that spline.")]
        public float routeDistance;

        [Tooltip("Rotation about the spline tangent that picks the direction to cast in. " +
                 "0 points at the ceiling, 180 at the floor.")]
        public float angleDegrees;

        [Tooltip("Distance along the surface normal from the cast hit. Negative embeds the prop into " +
                 "the rock, which is what stops scattered boulders reading as glued-on decals.")]
        public float surfaceOffset;

        [Tooltip("Rotation in the surface frame (forward = tangent projected onto the surface, " +
                 "up = surface normal). Storing the offset rather than a world rotation is what lets " +
                 "a prop keep its authored lean after the wall underneath it moves.")]
        public Quaternion surfaceRotation = Quaternion.identity;

        [Min(0.01f)] public float scale = 1f;

        [Tooltip("Resolved when the placement is authored. Drives the per-zone material rules and the " +
                 "validator's Z4 blackout check.")]
        public string zoneId;

        public CaveDecorPlacement Clone()
        {
            return (CaveDecorPlacement)MemberwiseClone();
        }
    }
}
