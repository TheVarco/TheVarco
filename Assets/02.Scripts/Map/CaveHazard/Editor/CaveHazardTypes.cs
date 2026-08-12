using System.Collections.Generic;
using UnityEngine;

namespace CaveHazard.EditorTools
{
    public enum CaveHazardKind
    {
        FallingRock,
        Vent
    }

    /// <summary>Which half of a Cross pattern an instance belongs to. Ignored by Alone stations.</summary>
    public enum CaveHazardGroup
    {
        A,
        B
    }

    /// <summary>
    /// One obstacle, stored route-relative for the same reason decor is: regenerating the blockout
    /// rebuilds the shell from the spline plus noise, so a world position would end up floating in
    /// open water or buried in rock. See CaveDecorPlacement for the full argument.
    ///
    /// Deliberately missing a surfaceRotation, unlike CaveDecorPlacement. Both obstacle prefabs are
    /// gravity-coupled - the rock falls along world -Y under Physics.gravity and its warning spot light
    /// points down the spawner's own -Y, while VentController erupts along transform.up - so mounting
    /// either in the surface frame of a tunnel that climbs ~29 degrees would aim it into the wall.
    /// The only rotation an obstacle gets is yaw about world up, which cannot break that coupling.
    /// </summary>
    public sealed class CaveHazardInstance
    {
        public CaveHazardKind kind;
        public string stationId;
        public string zoneId;
        public string routeId = "MainRoute";
        public float routeDistance;

        /// <summary>Rotation about the tunnel axis picking the cast direction. 0 is ceiling, 180 floor.</summary>
        public float angleDegrees;

        /// <summary>Along the surface normal from the cast hit. Negative embeds the mount into the rock.</summary>
        public float surfaceOffset;

        public float scale = 1f;

        /// <summary>Cosmetic spin about world up. Safe for both prefabs; see the class remark.</summary>
        public float yawDegrees;

        public CaveHazardGroup group = CaveHazardGroup.A;
    }

    /// <summary>
    /// A gate the player passes through: one or more obstacles that share a timetable.
    ///
    /// Grouping matters beyond tidiness. A Cross station guarantees that exactly one of its two groups
    /// is dangerous at a time, which is what makes "there is always a way through" a property of the
    /// pattern rather than a hope about how independently phased timers happen to line up.
    /// </summary>
    public sealed class CaveHazardStation
    {
        public string id;
        public string zoneId;
        public CaveHazardKind kind;

        /// <summary>Cross alternates group A and B forever. Alone runs one group with a rest between.</summary>
        public bool useCrossPattern;

        public float startDelaySeconds;
        public float initialWarningDuration = 1f;

        /// <summary>VentPattern.activeDuration, or RockPattern.dropInterval - the same slot in ObstaclePatternBase.</summary>
        public float activeDuration = 2.5f;

        public float crossWarningLeadTime = 0.75f;
        public float aloneRecoveryDuration = 1.5f;

        public readonly List<CaveHazardInstance> instances = new List<CaveHazardInstance>();

        /// <summary>
        /// Seconds from scene start to the first moment this station is dangerous, and the period it
        /// repeats on. Both come straight from ObstaclePatternBase's coroutines, so the numbers here
        /// are the numbers that run - they are not a model of the state machine.
        /// </summary>
        public float FirstActiveTime => startDelaySeconds + initialWarningDuration;

        public float CyclePeriod => useCrossPattern
            // RunCrossPattern hands the baton over every activeDuration, so a group's own turn comes
            // round every second handover. There is no rest: some group is always active.
            ? activeDuration * 2f
            // RunAlonePattern: warning, active, recovery.
            : initialWarningDuration + activeDuration + aloneRecoveryDuration;

        /// <summary>
        /// Seconds per cycle during which at least one instance in this station is dangerous.
        ///
        /// Vents only. A rock station has no dangerous span at all: RockSpawner's active phase spawns a
        /// single rock and returns immediately, so the hazard is the rock's flight, not the phase.
        /// Reading the pattern's active duration as dangerous time reported Z3 at a 100% duty cycle -
        /// a corridor the player supposedly cannot ever be safe in - when what actually happens is a
        /// 0.9 m boulder crossing their depth for a fraction of a second. Rock exposure is counted as
        /// drop events instead; see CaveHazardValidator.CheckTimingCoverage.
        /// </summary>
        public float DangerousSecondsPerCycle => kind == CaveHazardKind.FallingRock
            ? 0f
            : useCrossPattern ? CyclePeriod : activeDuration;

        /// <summary>Rock releases per second across both groups, once the station is running.</summary>
        public float DropsPerSecond => kind == CaveHazardKind.FallingRock
            ? 1f / Mathf.Max(0.01f, useCrossPattern ? activeDuration : activeDuration + aloneRecoveryDuration)
            : 0f;
    }
}
