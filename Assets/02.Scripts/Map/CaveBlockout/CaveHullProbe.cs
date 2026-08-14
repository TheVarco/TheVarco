using UnityEngine;

namespace CaveBlockout
{
    /// <summary>
    /// The shape that has to fit through the cave, described as a world-scaled capsule.
    ///
    /// This exists because the clearance validator used to carry its own idea of the submarine - a
    /// 3 x 3 x 6 m box centred on the centreline, copied out of MAP_GUIDE.md's "기준 잠수정 6x3x3 m"
    /// line. The prefab's actual movement capsule is 3.28 m across, 8.11 m long, and sits 3 m behind
    /// the pivot, so every clearance PASS was a pass for a submarine that does not exist.
    ///
    /// The cave assembly deliberately does not know what a submarine is. It is handed a shape. The
    /// shape is resolved from the prefab by SubmarineHullProbeProvider, which lives in
    /// Assembly-CSharp-Editor because an assembly definition cannot reference the predefined
    /// assemblies - CaveBlockout.Editor can never see SubmarineController directly.
    /// </summary>
    public readonly struct CaveHullProbe
    {
        /// <summary>World-space capsule radius.</summary>
        public readonly float radius;

        /// <summary>World-space capsule length along local +Z, tip to tip.</summary>
        public readonly float height;

        /// <summary>
        /// World-scaled capsule centre in the hull's local frame. Non-zero Z means the hull is not
        /// centred on its own pivot, which is what makes yaw swing the tail through a wide arc.
        /// </summary>
        public readonly Vector3 localCenter;

        /// <summary>
        /// Yaw the driver can command in one simulation tick, in degrees. Used by the wedge check to
        /// test the same rotation increment the runtime gate refuses.
        /// </summary>
        public readonly float yawStepDegrees;

        /// <summary>
        /// Which layers count as blocking. Carried on the probe because it is part of "what stops this
        /// shape" - the validator used to test layer 0 alone while the submarine collides against
        /// Default plus Obstacle, so anything authored on Obstacle was invisible to the check.
        /// </summary>
        public readonly int layerMask;

        /// <summary>
        /// Metres of altitude the hull can trade for a metre travelled forward, i.e. vertical speed over
        /// forward speed. The hull cannot pitch, but it can climb and sink bodily, so a clearance check
        /// that pins it to the centreline tests a path the driver would never fly. This is the budget for
        /// how far off the centreline it may sit, and how fast that offset may change.
        /// </summary>
        public readonly float verticalPerForwardMetre;

        /// <summary>Where these numbers came from, so a failure report can name its own source.</summary>
        public readonly string source;

        public CaveHullProbe(
            float radius,
            float height,
            Vector3 localCenter,
            float yawStepDegrees,
            int layerMask,
            float verticalPerForwardMetre,
            string source)
        {
            this.verticalPerForwardMetre = Mathf.Max(0f, verticalPerForwardMetre);
            this.radius = Mathf.Max(0.01f, radius);
            this.height = Mathf.Max(0.02f, height);
            this.localCenter = localCenter;
            this.yawStepDegrees = Mathf.Max(0f, yawStepDegrees);
            this.layerMask = layerMask == 0 ? 1 : layerMask;
            this.source = string.IsNullOrEmpty(source) ? "unspecified" : source;
        }

        /// <summary>
        /// Builds a probe from values authored in a hull's local space plus that hull's lossy scale,
        /// matching how the runtime resolves its own collision capsule.
        /// </summary>
        public static CaveHullProbe FromLocal(
            float localRadius,
            float localHeight,
            Vector3 localCenter,
            Vector3 lossyScale,
            float yawStepDegrees,
            int layerMask,
            float verticalPerForwardMetre,
            string source)
        {
            float radialScale = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y));
            float lengthScale = Mathf.Abs(lossyScale.z);
            return new CaveHullProbe(
                localRadius * radialScale,
                localHeight * lengthScale,
                Vector3.Scale(localCenter, lossyScale),
                yawStepDegrees,
                layerMask,
                verticalPerForwardMetre,
                source);
        }

        /// <summary>
        /// Same shape, but told to follow the tunnel's pitch instead of staying level. Only used by the
        /// diagnostic report, to separate "the hull is too big" from "the hull cannot tilt".
        /// </summary>
        public CaveHullProbe WithSource(string newSource)
        {
            return new CaveHullProbe(radius, height, localCenter, yawStepDegrees, layerMask,
                verticalPerForwardMetre, newSource);
        }

        /// <summary>
        /// The same hull at a different uniform scale.
        ///
        /// Used to measure margin instead of only pass or fail. "A path exists" is a weak answer when the
        /// path is a knife edge - the hull threads the Z2/Z3 throat with well under a metre to spare, which
        /// reads in play as wedging solid. Asking how far the hull can grow, or must shrink, before the
        /// answer flips turns that into a number worth deciding on.
        /// </summary>
        public CaveHullProbe Scaled(float factor)
        {
            float safe = Mathf.Max(0.01f, factor);
            return new CaveHullProbe(radius * safe, height * safe, localCenter * safe, yawStepDegrees,
                layerMask, verticalPerForwardMetre, $"{source} x{safe:F3}");
        }

        /// <summary>
        /// Distance from the pivot to the far tip of the hull. This is the lever that decides how much
        /// lateral room a turn needs: the tail sweeps <c>AftReach * sin(turn)</c> sideways, so a hull
        /// whose pivot sits at its nose needs roughly twice the corridor of one that pivots amidships.
        /// </summary>
        public float AftReach => Mathf.Abs(localCenter.z) + height * 0.5f;

        /// <summary>
        /// The one implementation of the capsule geometry. SubmarineController delegates to it so the
        /// validator sweeps exactly the shape the runtime collides with - two implementations is how
        /// the original mismatch survived so long.
        /// </summary>
        public void GetWorldCapsule(
            Vector3 position,
            Quaternion rotation,
            out Vector3 pointA,
            out Vector3 pointB,
            out float worldRadius)
        {
            worldRadius = radius;
            float halfLineLength = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 center = position + rotation * localCenter;
            Vector3 axisOffset = rotation * Vector3.forward * halfLineLength;

            pointA = center + axisOffset;
            pointB = center - axisOffset;
        }

        public override string ToString()
        {
            return $"{source} (r={radius:F3} len={height:F3} center={localCenter} " +
                   $"aftReach={AftReach:F2} yawStep={yawStepDegrees:F3}deg mask={layerMask} " +
                   $"climbBudget={verticalPerForwardMetre:F3}m/m)";
        }
    }
}
