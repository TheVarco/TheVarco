using System.Collections.Generic;

namespace CaveHazard.EditorTools
{
    /// <summary>
    /// The authored hazard layout for MainMap: falling rock in Z3, hydrothermal vents in Z5.
    ///
    /// Kept as a code table rather than a ScriptableObject because every station here is deliberate.
    /// The decor set is an asset because 425 placements came out of a seeded random scatter that has to
    /// be reproducible; twelve hand-reasoned gates are better off diffable, commentable and re-runnable
    /// after a blockout rebuild without an asset to keep in sync.
    ///
    /// MAP_GUIDE.md is what puts each hazard where it is: Z3 난류 협곡 lists 낙석 among its core rules
    /// and HAZ-01 is assigned to "Z3의 조류 경로", while Z5 열수 굴뚝 is the thermal chimney.
    ///
    /// ==== the measurements this table is built on (Tools > Underwater Cave > Hazards > 0) ====
    ///
    /// Vent prefab      hazard capsule r=1.15 h=6 centred (0,3,0), so the jet occupies 0..6 m above the
    ///                  mount at scale 1. Rock chimney mesh hangs -4.71..+0.20 m around the mount.
    /// RockSpawner      no mesh; spawn point is the spot light, which at identity rotation points
    ///                  (0,-1,0). Rocks fall on Physics.gravity, so the mount must not be tilted.
    /// FallingRock      sphere collider 0.9 m across. Small - hence more than one spawner per group.
    ///
    /// Z3 (145.6-266.9) centreline-to-ceiling runs 6.5-14.8 m and a rock dropped from the ceiling falls
    /// 12.6-27.7 m before it meets the floor, so ceiling mounts genuinely cross the swimmable channel.
    ///
    /// Z5 (347.5-458.7) is a lens: floor sits 6.3-10.7 m below the centre line at both ends and
    /// 23-28.5 m in the middle. A scale-1 vent reaches ~9.5 m. That is why the gates sit at the
    /// constrictions and the wide middle carries a single tall vent instead of a pattern - anything
    /// else would need a 3x vent to touch the player, which is a stretched asset, not a design.
    /// </summary>
    public static class CaveHazardLayout
    {
        // ---- Z3 falling rock ----

        /// <summary>
        /// Six gates across 121 m. Every one is a Cross pair, left group against right group.
        ///
        /// Cross rather than independently phased spawners because it makes "there is always a way
        /// through" a property of the pattern instead of a coincidence: ObstaclePatternBase hands the
        /// baton from group A to group B and back, so one side of the corridor is always quiet. Six
        /// staggered Alone spawners would drift into occasionally dropping on both sides at once.
        /// </summary>
        private static readonly float[] Z3StationDistances = { 157f, 176f, 195f, 214f, 233f, 252f };

        /// <summary>
        /// Two spawners per group, spread across the ceiling so a drop reads as rockfall rather than as
        /// one 0.9 m pebble. Negative angles are one side of the ceiling, positive the other.
        /// </summary>
        private static readonly float[] Z3GroupAAngles = { -34f, -19f };
        private static readonly float[] Z3GroupBAngles = { 19f, 34f };

        /// <summary>
        /// Clear of the rock face so the rock does not begin its life inside the ceiling it spawned
        /// from - the collider would resolve on frame one and the rock would never fall.
        /// </summary>
        private const float Z3SpawnClearance = 1.2f;

        /// <summary>
        /// One drop every 2.5 s alternating sides. At the 10 m/s cruise of CaveSwimController that is a
        /// drop every 25 m against a 19 m station spacing, so the player meets roughly one drop per
        /// gate: pressure without a curtain.
        /// </summary>
        private const float Z3DropInterval = 2.5f;

        // ---- Z5 vents ----

        private struct VentStationSpec
        {
            public string id;
            public float distance;
            public bool cross;
            public float scale;
            public float[] anglesA;
            public float[] anglesB;
            public float startDelay;
        }

        /// <summary>
        /// Five stations. The two at the entrance teach the mechanic one vent at a time; the two at the
        /// exit throat are Cross pairs, which is the "파도타기" the obstacle author built the pattern for.
        /// The single tall vent at 414 m keeps the wide middle from going quiet for four seconds without
        /// pretending a 28 m gap can be gated.
        ///
        /// Scales are sized from the measured floor depth at each distance so the jet reaches the swim
        /// line. CaveHazardValidator measures the real jet-to-centreline distance and fails the build if
        /// any of these guesses is wrong, so the numbers below are a starting point, not an assertion.
        /// </summary>
        private static readonly VentStationSpec[] Z5Stations =
        {
            // floor ~7.5 m down here, so a small vent already reaches.
            new VentStationSpec { id = "Z5_Vent_S1", distance = 355f, cross = false, scale = 1.5f,
                anglesA = new[] { 180f }, startDelay = 0.0f },

            new VentStationSpec { id = "Z5_Vent_S2", distance = 369f, cross = false, scale = 1.9f,
                anglesA = new[] { 194f }, startDelay = 1.2f },

            // The bulge. 225 deg is the closest surface here (14.3 m) against 24.7 m straight down.
            new VentStationSpec { id = "Z5_Vent_S3", distance = 414f, cross = false, scale = 2.4f,
                anglesA = new[] { 218f }, startDelay = 0.6f },

            new VentStationSpec { id = "Z5_Vent_S4", distance = 431f, cross = true, scale = 2.2f,
                anglesA = new[] { 156f }, anglesB = new[] { 206f }, startDelay = 0.0f },

            // 152/208 rather than the 168/192 this started at. The exit throat has closed back to a
            // ~7 m radius by here, so a 24-degree separation put the two 2.2 m-wide jets 3.09 m apart
            // and they intersected. Wider angles also push both mounts further out, which buys
            // separation twice over.
            new VentStationSpec { id = "Z5_Vent_S5", distance = 447f, cross = true, scale = 1.6f,
                anglesA = new[] { 152f }, anglesB = new[] { 208f }, startDelay = 0.9f }
        };

        /// <summary>
        /// Positive, so the chimney mouth stands proud of the floor instead of being buried. The mesh
        /// spans -4.71..+0.20 m about the mount at scale 1, so this leaves roughly a third of the
        /// chimney showing and the rest embedded, which is how a vent should read.
        /// </summary>
        private const float VentSurfaceOffsetPerScale = 3.2f;

        public static List<CaveHazardStation> Build()
        {
            var stations = new List<CaveHazardStation>();
            BuildZ3(stations);
            BuildZ5(stations);
            return stations;
        }

        private static void BuildZ3(List<CaveHazardStation> stations)
        {
            for (int i = 0; i < Z3StationDistances.Length; i++)
            {
                float distance = Z3StationDistances[i];
                var station = new CaveHazardStation
                {
                    id = $"Z3_Rock_S{i + 1}",
                    zoneId = "Z3",
                    kind = CaveHazardKind.FallingRock,
                    useCrossPattern = true,
                    // Spread the gates' phases so two neighbouring stations do not fire in lockstep and
                    // read as one wall of rock.
                    startDelaySeconds = (i % 3) * 0.8f,
                    initialWarningDuration = 1f,
                    activeDuration = Z3DropInterval,
                    crossWarningLeadTime = 0.75f,
                    aloneRecoveryDuration = 1.5f
                };

                AddRockGroup(station, distance, Z3GroupAAngles, CaveHazardGroup.A);
                AddRockGroup(station, distance, Z3GroupBAngles, CaveHazardGroup.B);
                stations.Add(station);
            }
        }

        private static void AddRockGroup(CaveHazardStation station, float distance, float[] angles,
            CaveHazardGroup group)
        {
            for (int i = 0; i < angles.Length; i++)
            {
                station.instances.Add(new CaveHazardInstance
                {
                    kind = CaveHazardKind.FallingRock,
                    stationId = station.id,
                    zoneId = station.zoneId,
                    // Offset along the route as well as around it, so a group's two rocks do not land in
                    // a line across the corridor.
                    routeDistance = distance + (i - (angles.Length - 1) * 0.5f) * 4f,
                    angleDegrees = angles[i],
                    surfaceOffset = Z3SpawnClearance,
                    scale = 1f,
                    group = group
                });
            }
        }

        private static void BuildZ5(List<CaveHazardStation> stations)
        {
            foreach (VentStationSpec spec in Z5Stations)
            {
                var station = new CaveHazardStation
                {
                    id = spec.id,
                    zoneId = "Z5",
                    kind = CaveHazardKind.Vent,
                    useCrossPattern = spec.cross,
                    startDelaySeconds = spec.startDelay,
                    initialWarningDuration = 1f,
                    activeDuration = 2.5f,
                    crossWarningLeadTime = 0.75f,
                    aloneRecoveryDuration = 1.5f
                };

                AddVentGroup(station, spec, spec.anglesA, CaveHazardGroup.A);
                if (spec.cross)
                    AddVentGroup(station, spec, spec.anglesB, CaveHazardGroup.B);

                stations.Add(station);
            }
        }

        private static void AddVentGroup(CaveHazardStation station, VentStationSpec spec, float[] angles,
            CaveHazardGroup group)
        {
            if (angles == null)
                return;

            foreach (float angle in angles)
            {
                station.instances.Add(new CaveHazardInstance
                {
                    kind = CaveHazardKind.Vent,
                    stationId = station.id,
                    zoneId = station.zoneId,
                    routeDistance = spec.distance,
                    angleDegrees = angle,
                    surfaceOffset = VentSurfaceOffsetPerScale * spec.scale,
                    scale = spec.scale,
                    // Cosmetic only - the chimney is not radially symmetric and identical yaw on every
                    // vent reads as a copy-paste.
                    yawDegrees = (angle * 3f) % 360f,
                    group = group
                });
            }
        }
    }
}
