using UnityEngine;

namespace CaveItem.EditorTools
{
    /// <summary>Every species the item layout places.</summary>
    public enum CaveItemKind
    {
        OxygenTank,
        Octopus,
        Urchin,
        Rope,
        Gun,
        Hammer,
        Shark,
        Tornado
    }

    /// <summary>
    /// How a placement turns into a world transform.
    ///
    /// Decor and hazards only ever needed one mode, because everything they place rests on rock. Items
    /// do not: a shark is 9 m long and a whirlpool pulls from 7 m away, and in Z6 the floor sits 18-27 m
    /// below the line the player swims. Casting those onto a wall that far away would put the climax
    /// somewhere nobody goes.
    /// </summary>
    public enum CaveItemAnchor
    {
        /// <summary>Cast from the centreline to the shell, then step back along the surface normal.</summary>
        Surface,

        /// <summary>Sample the centreline and offset inside the axis frame. No cast, no rock contact.</summary>
        Centerline,

        /// <summary>Offset from the submarine root, in world axes. Used by the hammer alone.</summary>
        SubmarineInterior
    }

    /// <summary>
    /// What the object's rotation is built from.
    ///
    /// CaveHazardInstance makes the same split with a single flag and explains why: the tunnel climbs at
    /// roughly 29 degrees, so anything that answers to gravity or swims under its own power must not
    /// inherit the tilt of the wall it was mounted against. Items need a third case, because a shark
    /// anchored in open water has no surface to take a frame from but still ought to face down the cave.
    /// </summary>
    public enum CaveItemOrientation
    {
        /// <summary>Lie against the rock: the surface frame, then yaw. Right for anything resting on a surface.</summary>
        SurfaceFrame,

        /// <summary>Face along the tunnel tangent, world-up as the up vector, then yaw. Right for swimmers.</summary>
        AlongTunnel,

        /// <summary>World upright, yaw only. Right for the tornado, whose particles are authored about world Y.</summary>
        WorldUpright
    }

    /// <summary>
    /// One placed object.
    ///
    /// Route-relative for the same reason decor is (<see cref="CaveBlockout.Decor.CaveDecorPlacement"/>):
    /// regenerating the blockout moves every surface in the map, and a world coordinate authored against
    /// the old shell ends up inside rock or hanging in open water. Re-running the layout re-projects.
    /// </summary>
    public sealed class CaveItemPlacement
    {
        public CaveItemKind kind;

        /// <summary>
        /// The zone this counts towards, which is not always where it physically sits. Branch oxygen
        /// tanks carry zoneId "Z2" and routeId "Z2_Branch": the user asked for seven per zone with the
        /// tanks moved into the branch cave, so the branch tanks are part of Z2's seven, not extra.
        /// </summary>
        public string zoneId;

        public CaveItemAnchor anchor = CaveItemAnchor.Surface;

        // ---- Surface and Centerline ----

        public string routeId = "MainRoute";
        public float routeDistance;

        /// <summary>
        /// Direction about the tunnel axis: 0 = ceiling, 180 = floor. Deliberately the same convention
        /// decor and hazards use, so a distance/angle pair means the same thing in all three tools.
        ///
        /// Surface anchors cast along it. Centerline anchors step along it by <see cref="lateralOffset"/>.
        /// </summary>
        public float angleDegrees = 180f;

        /// <summary>
        /// Surface only. Metres along the surface normal, and unlike decor this is POSITIVE.
        /// Decor embeds props into the rock so they read as part of it; a pickup that does the same is
        /// unobtainable, because PlayerInteractor's line-of-sight ray hits the shell before it reaches
        /// the item and drops the candidate without any feedback.
        /// </summary>
        public float surfaceOffset;

        /// <summary>
        /// Centerline only. Metres from the centre line along <see cref="angleDegrees"/>.
        /// Zero puts the object exactly on the swim line.
        /// </summary>
        public float lateralOffset;

        // ---- SubmarineInterior ----

        /// <summary>Metres from the submarine root in world axes. The sub sits unrotated, so no basis change is needed.</summary>
        public Vector3 interiorOffset;

        // ---- common ----

        /// <summary>Spin about world up. For creatures this only sets the first frame; their AI takes over immediately.</summary>
        public float yawDegrees;

        public Vector3 scale = Vector3.one;

        public CaveItemOrientation orientation = CaveItemOrientation.SurfaceFrame;

        /// <summary>Human-readable id, also used as the scene object name.</summary>
        public string id;

        /// <summary>
        /// Why this one is here. Carried into the build report so a failure names the intent as well as
        /// the coordinate, and so the reasoning survives next to the number instead of only in review.
        /// </summary>
        public string note;
    }
}
