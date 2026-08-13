using System.Collections.Generic;
using UnityEngine;

namespace CaveItem.EditorTools
{
    /// <summary>
    /// The authored placement of all 52 items, as a static table.
    ///
    /// A table in code rather than a ScriptableObject, for the reason CaveHazardLayout gives for its own
    /// 34 stations: the decor set is an asset because 392 records came out of a seeded random scatter and
    /// have to reproduce byte for byte, while every entry here was reasoned about individually and wants
    /// a comment next to it explaining why it is where it is. Code diffs, comments and re-runs; a 52-row
    /// asset gives none of that and adds a second file to keep in step with this one.
    ///
    /// Route distances dodge three things, all of which the validator re-checks rather than trusting:
    ///
    ///   Z3 rockfall  - CaveHazardLayout puts stations at 157/176/195/214/233/252, but each station
    ///                  offsets its two spawners by +-2 m along the route, so the real occupied
    ///                  distances are 155/159, 174/178, 193/197, 212/216, 231/235, 250/254.
    ///   Z5 vents     - 355, 369, 414, 431, 447, no longitudinal spread.
    ///   Portals      - the branch junctions at 100.356 (Z2), 307.157 (Z4) and 403.253 (Z5). These are
    ///                  holes in the wall, so a cast aimed across one either misses or finds the far
    ///                  side of the junction; Z5's is 12 m of half-length, wide enough to swallow
    ///                  several placements if it is not respected.
    ///
    /// Every surface angle was picked against the CaveItemProbe radius sweep, not guessed. The one that
    /// most needed it is Z6: it is 85 x 50, so past its entrance the floor sits 18-27 m below the swim
    /// line. All four of its oxygen tanks are therefore in the first third, where the tunnel is still
    /// 6-18 m across - which also reads as "stock up before the gauntlet", since the sharks and
    /// whirlpools all live past 490 m.
    /// </summary>
    public static class CaveItemLayout
    {
        // ---- zone quota -------------------------------------------------------------------------

        /// <summary>
        /// Oxygen / octopus / urchin per zone. The user asked for seven of these three per zone, with
        /// the split left open and the oxygen moved into the branch cave wherever a zone has one.
        ///
        /// The split is tuned per zone rather than uniform, and the validator checks both the total of
        /// seven and this exact breakdown, so an accidental edit to the table below fails the build
        /// instead of silently shipping six items in a zone.
        /// </summary>
        public static readonly Dictionary<string, (int oxygen, int octopus, int urchin)> ZoneQuota =
            new Dictionary<string, (int, int, int)>
            {
                // Tutorial. Generous oxygen, one octopus to introduce the enemy, urchins to teach that
                // some things hurt if you brush them.
                { "Z1", (3, 1, 3) },
                // Coral basin. Its branch is the guide's "resource cave", so the tanks go in there and
                // the gun and first rope sit on the main line.
                { "Z2", (3, 2, 2) },
                // Turbulent ravine with the rockfall. Fewest tanks and most urchins on purpose: hugging
                // a wall is how you dodge rocks, so the walls are where the punishment lives.
                { "Z3", (2, 2, 3) },
                // Total blackout, 5.5 m visibility. Tanks in the branch give a reason to explore in the
                // dark; only one octopus, because an enemy you cannot see is not a fair fight in bulk.
                { "Z4", (3, 1, 3) },
                // Hydrothermal chimney. The zone is 30 x 60, so its surfaces are 20-30 m off the swim
                // line - which is exactly why its oxygen belongs in the tight branch instead.
                { "Z5", (3, 2, 2) },
                // Exit throat. Most oxygen of any zone, all of it before the shark and whirlpool run.
                { "Z6", (4, 1, 2) }
            };

        // ---- hazard and portal distances the layout was written against ----------------------------

        /// <summary>Z3 rockfall spawner distances, including the +-2 m per-station spread.</summary>
        public static readonly float[] Z3RockDistances =
            { 155f, 159f, 174f, 178f, 193f, 197f, 212f, 216f, 231f, 235f, 250f, 254f };

        /// <summary>Z5 vent distances.</summary>
        public static readonly float[] Z5VentDistances = { 355f, 369f, 414f, 431f, 447f };

        // ---- the table --------------------------------------------------------------------------

        public static List<CaveItemPlacement> Build()
        {
            var list = new List<CaveItemPlacement>();

            // ================= Z1 - crash site, 0 to 55.1, 35 x 20 =================
            // No branch and no hazards. The submarine sits at the mouth, so nothing hostile goes near
            // the first few metres; the validator enforces that against the sub's real world position.
            Surface(list, CaveItemKind.Urchin, "Z1", 16f, 205f, "출발 직후 우현 벽 - 첫 접촉");
            Surface(list, CaveItemKind.OxygenTank, "Z1", 19f, 180f, "첫 보급. 바닥 한가운데라 놓치기 어렵다");
            Surface(list, CaveItemKind.Urchin, "Z1", 27f, 150f);
            Surface(list, CaveItemKind.OxygenTank, "Z1", 33f, 172f);
            Surface(list, CaveItemKind.Octopus, "Z1", 38f, 95f, "첫 적. 벽에서 2.5 m 떨어져 순찰 반경이 통로를 문다");
            Surface(list, CaveItemKind.Urchin, "Z1", 42f, 195f);
            Surface(list, CaveItemKind.OxygenTank, "Z1", 48f, 186f, "Z2 진입 직전 보급");

            // ================= Z2 - coral basin, 55.1 to 145.6, 65 x 30 =================
            // Portal at 100.356 with 6 m of half-length; nothing between 90.4 and 110.4.
            // Moved off 68/178: a Blue Crystal Formation stood 0.45 m between the gun and the swim line,
            // which is a hard fail rather than a cosmetic one - PlayerInteractor's obstruction raycast
            // discards any candidate it cannot see, so the gun would have been in the scene, visible,
            // and impossible to pick up.
            Surface(list, CaveItemKind.Gun, "Z2", 71f, 198f, "Z3 협곡 진입 전에 무장시킨다");
            Surface(list, CaveItemKind.Urchin, "Z2", 78f, 215f);
            Surface(list, CaveItemKind.Octopus, "Z2", 86f, 270f, "포탈 앞 좌현. 분기로 새는 길목을 지킨다");
            Surface(list, CaveItemKind.Rope, "Z2", 114f, 182f, "Z6 소용돌이에서 끌려나오려면 이게 먼저 손에 있어야 한다");
            Surface(list, CaveItemKind.Urchin, "Z2", 122f, 160f);
            Surface(list, CaveItemKind.Octopus, "Z2", 133f, 90f);

            // Z2 branch - 40.2 m dead end. Counted as part of Z2's seven, not as extra.
            Branch(list, CaveItemKind.OxygenTank, "Z2", "Z2_Branch", 14f, 180f, "분기 보급 1");
            Branch(list, CaveItemKind.OxygenTank, "Z2", "Z2_Branch", 25f, 172f, "분기 보급 2");
            Branch(list, CaveItemKind.OxygenTank, "Z2", "Z2_Branch", 35f, 188f, "막다른 끝의 보상");

            // ================= Z3 - turbulent ravine, 145.6 to 266.9, 25 x 25 =================
            // Everything sits in the gaps between rockfall stations, minimum 7 m of longitudinal
            // clearance from the nearest spawner.
            Surface(list, CaveItemKind.OxygenTank, "Z3", 148f, 180f, "첫 낙석 관문 앞 - 들어가기 전에 채운다");
            Surface(list, CaveItemKind.Urchin, "Z3", 166f, 205f, "159와 174 사이");
            Surface(list, CaveItemKind.Octopus, "Z3", 185f, 90f, "178과 193 사이");
            Surface(list, CaveItemKind.Urchin, "Z3", 204f, 155f, "197과 212 사이");
            Surface(list, CaveItemKind.Octopus, "Z3", 223f, 270f, "216과 231 사이");
            Surface(list, CaveItemKind.Urchin, "Z3", 242f, 200f, "235와 250 사이");
            Surface(list, CaveItemKind.OxygenTank, "Z3", 261f, 178f, "마지막 관문 통과 보상");

            // ================= Z4 - blackout fault, 266.9 to 347.5, 18 x 18 =================
            // Portal at 307.157, 6 m half-length; nothing between 297.2 and 317.2.
            Surface(list, CaveItemKind.Urchin, "Z4", 278f, 200f, "암흑 구간 초입 바닥");
            Surface(list, CaveItemKind.Urchin, "Z4", 293f, 160f);
            Surface(list, CaveItemKind.Octopus, "Z4", 325f, 95f, "포탈 통과 직후");
            Surface(list, CaveItemKind.Urchin, "Z4", 338f, 190f);

            Branch(list, CaveItemKind.OxygenTank, "Z4", "Z4_Branch", 14f, 180f, "어둠 속에서 분기를 뒤질 이유");
            Branch(list, CaveItemKind.OxygenTank, "Z4", "Z4_Branch", 26f, 175f);
            Branch(list, CaveItemKind.OxygenTank, "Z4", "Z4_Branch", 38f, 185f);

            // ================= Z5 - hydrothermal chimney, 347.5 to 458.7, 30 x 60 =================
            // Vents at 355/369/414/431/447 and a 12 m half-length portal at 403.253, so the usable
            // windows are narrow: 358-365, 372-387, 421-428, 434-444.
            Surface(list, CaveItemKind.Urchin, "Z5", 362f, 205f, "분출구 355와 369 사이");
            Surface(list, CaveItemKind.Octopus, "Z5", 377f, 270f);
            // Wall, not floor. Z5's floor sits 23-28 m below the swim line, and a rope down there
            // measured 22.5 m off route - past the point anyone detours for a consumable. The side wall
            // at this distance is 13 m out, which is a visible swim rather than a dive.
            Surface(list, CaveItemKind.Rope, "Z5", 383f, 265f, "두 번째 밧줄. 바닥이 아니라 측벽 - 굴뚝 바닥은 23 m 아래다");
            Surface(list, CaveItemKind.Urchin, "Z5", 424f, 158f, "분출구 414와 431 사이");
            // Moved off 440/90: the tunnel climbs at ~29 degrees here, so a wall mount that looks clear
            // in tunnel-angle terms sat 10.8 m directly above the 431 vent pair in world space, inside
            // their 13.2 m plume. Angles are not a proxy for world height in a climbing shaft.
            Surface(list, CaveItemKind.Octopus, "Z5", 438f, 300f, "분출구 431과 447 사이, 분출 기둥 반대편");

            Branch(list, CaveItemKind.OxygenTank, "Z5", "Z5_Branch", 15f, 180f, "분출구를 피해 쉬어가는 보급소");
            Branch(list, CaveItemKind.OxygenTank, "Z5", "Z5_Branch", 28f, 174f);
            Branch(list, CaveItemKind.OxygenTank, "Z5", "Z5_Branch", 40f, 186f);

            // ================= Z6 - exit throat, 458.7 to 579.6, 85 x 50 =================
            // All four tanks live in the first third. Past 490 the floor is 18-27 m below the swim line,
            // which is a long dive for a consumable; keeping them forward also means the player stocks
            // up before meeting the first whirlpool.
            Surface(list, CaveItemKind.OxygenTank, "Z6", 464f, 180f, "관문 앞 보급 1 - 여기는 아직 통로가 좁다");
            Surface(list, CaveItemKind.OxygenTank, "Z6", 473f, 190f, "관문 앞 보급 2");
            Surface(list, CaveItemKind.Urchin, "Z6", 480f, 205f);
            Surface(list, CaveItemKind.OxygenTank, "Z6", 484f, 172f, "관문 앞 보급 3");
            Surface(list, CaveItemKind.OxygenTank, "Z6", 496f, 186f, "관문 앞 보급 4 - 마지막 충전 지점");
            Surface(list, CaveItemKind.Octopus, "Z6", 515f, 90f);
            Surface(list, CaveItemKind.Urchin, "Z6", 552f, 165f);

            // The climax. Whirlpool, shark, whirlpool, shark, whirlpool, shark - interleaved so the two
            // threats alternate rather than arriving as one wall.
            //
            // Lateral 5 m for the whirlpools is deliberate and is the whole design: outerRadius is 7, so
            // the pull field covers the swim line and cannot be ignored, while the far side of an 85 m
            // chamber stays wide open. A whirlpool cannot gate a cavern this wide - it is pressure, not
            // a wall - and pretending otherwise by lining all three up would just make a curtain.
            Center(list, CaveItemKind.Tornado, "Z6", 490f, 135f, 5f, "소용돌이 1 - 우현 아래");
            Center(list, CaveItemKind.Shark, "Z6", 505f, 200f, 6f, "상어 1");
            Center(list, CaveItemKind.Tornado, "Z6", 522f, 225f, 5f, "소용돌이 2 - 좌현 아래로 교대");
            Center(list, CaveItemKind.Shark, "Z6", 535f, 60f, 8f, "상어 2 - 반대편 위에서");
            Center(list, CaveItemKind.Tornado, "Z6", 554f, 90f, 5f, "소용돌이 3 - 우현");
            Center(list, CaveItemKind.Shark, "Z6", 568f, 160f, 5f, "상어 3 - 출구 직전");

            // ================= Submarine =================
            // Measured, not derived: CaveItemProbe found the interior floor at y = -0.893 by casting
            // down from the spawn slots with triggers ignored (WalkZone is itself a trigger capsule and
            // swallows a default query at y = 1.88).
            //
            // z = 0 is the midpoint of the four spawn slots, which run +2.264 to -2.265. That is the only
            // z from which every slot is inside PlayerInteractor's 3 m range - measured at 1.74 / 2.56 m.
            //
            // Unparented, like all 39 other scene NetworkObjects. Fusion's NetworkObjectBaker builds
            // NestedObjects from transform-path ancestry, so parenting this under the submarine would
            // bake it as a nested object - and CarryableItem.OnPickedUp immediately reparents to the
            // player's hand socket, which would tear a baked nested object out of its parent at runtime.
            Interior(list, CaveItemKind.Hammer, new Vector3(0f, -0.843f, -6.1f), 90f,
                "잠수함 내부 바닥. 수리 대상인 RepairableStructure가 잠수함 루트에 있다");

            return list;
        }

        // ---- builders ---------------------------------------------------------------------------

        private static void Surface(List<CaveItemPlacement> list, CaveItemKind kind, string zoneId,
            float distance, float angle, string note = null)
        {
            CaveItemCatalog.Species species = CaveItemCatalog.Get(kind);
            list.Add(new CaveItemPlacement
            {
                kind = kind,
                zoneId = zoneId,
                anchor = CaveItemAnchor.Surface,
                orientation = species.orientation,
                routeId = "MainRoute",
                routeDistance = distance,
                angleDegrees = angle,
                surfaceOffset = species.surfaceOffset,
                scale = species.scale,
                // Yaw derived from the distance so props do not all face the same way, but stable across
                // runs - a random spin would rewrite the scene on every rebuild for no visual gain.
                yawDegrees = (distance * 37f) % 360f,
                id = Id(list, kind, zoneId),
                note = note
            });
        }

        /// <summary>
        /// A placement physically on a branch spline but counted towards the parent zone's seven.
        /// zoneId stays "Z2" while routeId becomes "Z2_Branch": the user asked for the tanks to move
        /// into the branch, not for the branch to become a seventh zone with its own quota.
        /// </summary>
        private static void Branch(List<CaveItemPlacement> list, CaveItemKind kind, string zoneId,
            string routeId, float distance, float angle, string note = null)
        {
            Surface(list, kind, zoneId, distance, angle, note);
            CaveItemPlacement placement = list[list.Count - 1];
            placement.routeId = routeId;
            placement.id = Id(list, kind, routeId);
        }

        private static void Center(List<CaveItemPlacement> list, CaveItemKind kind, string zoneId,
            float distance, float angle, float lateral, string note = null)
        {
            CaveItemCatalog.Species species = CaveItemCatalog.Get(kind);
            list.Add(new CaveItemPlacement
            {
                kind = kind,
                zoneId = zoneId,
                anchor = CaveItemAnchor.Centerline,
                orientation = species.orientation,
                routeId = "MainRoute",
                routeDistance = distance,
                angleDegrees = angle,
                lateralOffset = lateral,
                scale = species.scale,
                yawDegrees = 0f,
                id = Id(list, kind, zoneId),
                note = note
            });
        }

        private static void Interior(List<CaveItemPlacement> list, CaveItemKind kind,
            Vector3 offsetFromSubmarine, float yaw, string note = null)
        {
            CaveItemCatalog.Species species = CaveItemCatalog.Get(kind);
            list.Add(new CaveItemPlacement
            {
                kind = kind,
                zoneId = "Submarine",
                anchor = CaveItemAnchor.SubmarineInterior,
                orientation = species.orientation,
                interiorOffset = offsetFromSubmarine,
                scale = species.scale,
                yawDegrees = yaw,
                id = Id(list, kind, "Submarine"),
                note = note
            });
        }

        private static string Id(List<CaveItemPlacement> list, CaveItemKind kind, string prefix)
        {
            int n = 1;
            foreach (CaveItemPlacement existing in list)
            {
                if (existing.kind == kind && existing.id != null && existing.id.StartsWith(prefix + "_"))
                    n++;
            }
            return $"{prefix}_{kind}_{n}";
        }
    }
}
