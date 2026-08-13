using System.Collections.Generic;
using System.Text;
using CaveBlockout;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CaveItem.EditorTools
{
    /// <summary>
    /// Measures the three things the item layout depends on that no inspector or design doc states.
    ///
    /// 1. Prefab reach. Gun, Hammer and Urchin carry a MeshCollider or a nested FBX, so their size is
    ///    not readable from the prefab YAML at all. The layout needs it: an item has to clear the cave
    ///    surface by more than its own half-height, or PlayerInteractor's line-of-sight ray hits the
    ///    shell before it reaches the item and the pickup silently never registers.
    ///
    /// 2. The submarine interior. Every interior world coordinate scales with the submarine root's
    ///    lossyScale, which the scene does not override - so it comes from the prefab variant and has
    ///    to be read, not assumed. The floor height is inside the GLB hull mesh and can only be found
    ///    by casting.
    ///
    /// 3. Whether TryCast resolves against the three branch splines. Nine of the forty-two per-zone
    ///    items are branch oxygen tanks, and no decor or hazard record in this project has ever used a
    ///    routeId other than "MainRoute". That path has zero production mileage; if it misses, the
    ///    fallback is main-route placement near each portal.
    ///
    /// Opens MainScene_final rather than MainMap because MainMap has no submarine.
    ///
    /// Lives in Assembly-CSharp-Editor (no asmdef in this folder), the same way CaveHazard does, so it
    /// can see both the CaveBlockout.Runtime types and the item components in the default assembly.
    /// </summary>
    public static class CaveItemProbe
    {
        public const string PlayScenePath = "Assets/01.Scenes/MainScene_final.unity";

        /// <summary>Every prefab the layout places, in the order the report reads best.</summary>
        private static readonly string[] ProbePrefabs =
        {
            "Assets/03.Prefabs/OxygenItem.prefab",
            "Assets/03.Prefabs/RopeItem.prefab",
            "Assets/03.Prefabs/Gun.prefab",
            "Assets/03.Prefabs/Hammer.prefab",
            "Assets/03.Prefabs/Enemy/Urchin.prefab",
            "Assets/03.Prefabs/Enemy/Octopus.prefab",
            "Assets/03.Prefabs/Enemy/Shark.prefab",
            "Assets/03.Prefabs/Obstacle/Tonado.prefab"
        };

        /// <summary>
        /// Interaction layer. PlayerInteractor scans only this one (interactableLayer = 1 &lt;&lt; 7), so a
        /// pickup on any other layer is invisible to the player no matter where it sits.
        /// </summary>
        private const int InteractionLayer = 7;

        [MenuItem("Tools/Underwater Cave/Items/0 - 프리팹과 잠수함 실측")]
        public static void ProbeInteractive()
        {
            Debug.Log(Probe());
        }

        public static void ProbeBatch()
        {
            EditorSceneManager.OpenScene(PlayScenePath, OpenSceneMode.Single);
            Debug.Log(Probe());
        }

        public static string Probe()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE ITEM PROBE =====");

            MeasurePrefabs(report);
            MeasureSubmarine(report);
            MeasureRoutes(report);

            report.AppendLine("CAVE_ITEM_PROBE DONE");
            return report.ToString();
        }

        // ---------------------------------------------------------------- prefabs

        private static void MeasurePrefabs(StringBuilder report)
        {
            report.AppendLine();
            report.AppendLine("--- prefabs (instantiated at origin, identity, scale 1) ---");
            foreach (string path in ProbePrefabs)
                MeasurePrefab(report, path);
        }

        private static void MeasurePrefab(StringBuilder report, string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                report.AppendLine($"MISSING {path}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                // Identity at the origin so every bound reads as "metres from the mount point" rather
                // than as a world coordinate that depends on where the prefab happened to be saved.
                //
                // localScale is deliberately NOT reset. Four of these prefabs bake their real size into
                // the root scale (OxygenItem 0.5, Gun 0.4, Hammer 0.635, RopeItem 0.646), so forcing
                // scale 1 reports a prop twice the size the game will actually show.
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                // Collider.bounds reads the cached transform, which in edit mode still holds whatever
                // the prefab was saved with until this is forced. Renderer.bounds updates immediately,
                // so without this the two disagree and only the renderer half is right.
                Physics.SyncTransforms();

                report.AppendLine($"[{asset.name}]  layer={instance.layer}" +
                                  (instance.layer == InteractionLayer ? " (Interaction OK)" : "") +
                                  $"  prefabScale={Fmt(asset.transform.localScale)}");

                var components = new List<string>();
                foreach (Component component in instance.GetComponents<Component>())
                {
                    if (component != null)
                        components.Add(component.GetType().Name);
                }
                report.AppendLine($"  root   {string.Join(", ", components)}");

                // Solid renderers only. ParticleSystemRenderer bounds report the current simulation
                // state, which in an unplayed editor instance is an empty box on the emitter - the
                // tornado would read as 0 m across. Its real footprint is Whirlpool.outerRadius.
                var solid = new Bounds();
                bool hasSolid = false;
                int particleRenderers = 0;
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                        continue;
                    if (renderer is ParticleSystemRenderer)
                    {
                        particleRenderers++;
                        continue;
                    }
                    if (!hasSolid) { solid = renderer.bounds; hasSolid = true; }
                    else solid.Encapsulate(renderer.bounds);
                }

                if (hasSolid)
                {
                    report.AppendLine($"  render size={Fmt(solid.size)} centre={Fmt(solid.center)} " +
                                      $"min={Fmt(solid.min)} max={Fmt(solid.max)}");
                    // The number the layout actually needs: how far the visual hangs below the pivot.
                    report.AppendLine($"  >> drop below pivot = {-solid.min.y:0.###} m " +
                                      $"(surfaceOffset must exceed this to sit clear)");
                }
                else
                {
                    report.AppendLine($"  render (no solid renderer; particleRenderers={particleRenderers})");
                }

                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null)
                        continue;
                    Bounds b = collider.bounds;
                    string extra = string.Empty;
                    if (collider is CapsuleCollider capsule)
                    {
                        extra = $" capsule(r={capsule.radius:0.###} h={capsule.height:0.###} " +
                                $"axis={capsule.direction} centre={Fmt(capsule.center)})";
                    }
                    else if (collider is SphereCollider sphere)
                    {
                        extra = $" sphere(r={sphere.radius:0.###} centre={Fmt(sphere.center)})";
                    }
                    else if (collider is MeshCollider mesh)
                    {
                        extra = $" mesh(convex={mesh.convex} sharedMesh={(mesh.sharedMesh != null ? mesh.sharedMesh.name : "NULL")})";
                    }

                    report.AppendLine($"  col    {collider.GetType().Name} on '{collider.name}' " +
                                      $"trigger={collider.isTrigger} layer={collider.gameObject.layer} " +
                                      $"size={Fmt(b.size)} min.y={b.min.y:0.###}{extra}");
                }

                // The tornado's real footprint lives here, not in any renderer or collider.
                var whirlpool = instance.GetComponent<Whirlpool>();
                if (whirlpool != null)
                {
                    report.AppendLine($"  >> whirlpool outer={whirlpool.outerRadius} inner={whirlpool.innerRadius} " +
                                      $"maxPull={whirlpool.maxPullForce} falloff={whirlpool.falloffExponent}" +
                                      (whirlpool.innerRadius < whirlpool.outerRadius ? "" : "  ** inner >= outer: NaN RISK **"));
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---------------------------------------------------------------- submarine

        private static void MeasureSubmarine(StringBuilder report)
        {
            report.AppendLine();
            report.AppendLine("--- submarine interior ---");

            GameObject submarine = GameObject.Find("Submarine_final");
            if (submarine == null)
            {
                report.AppendLine("FAIL: no 'Submarine_final' in the open scene - the hammer cannot be placed");
                return;
            }

            Transform root = submarine.transform;
            report.AppendLine($"root pos={Fmt(root.position)} rot={Fmt(root.eulerAngles)} " +
                              $"localScale={Fmt(root.localScale)} lossyScale={Fmt(root.lossyScale)}");

            // Physics queries read stale transforms in edit mode unless this is forced.
            Physics.SyncTransforms();

            Transform walkZone = FindDescendant(root, "WalkZone");
            if (walkZone != null)
            {
                var capsule = walkZone.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    Bounds b = capsule.bounds;
                    report.AppendLine($"WalkZone worldCentre={Fmt(b.center)} worldSize={Fmt(b.size)} " +
                                      $"min={Fmt(b.min)} max={Fmt(b.max)} trigger={capsule.isTrigger}");
                }
                else
                {
                    report.AppendLine("WalkZone found but has no CapsuleCollider");
                }
            }
            else
            {
                report.AppendLine("WARN: no 'WalkZone' descendant");
            }

            // Spawn points define where players actually appear. The hammer has to be within
            // PlayerInteractor.interactRange (3 m) of them, and above the floor those players stand on.
            report.AppendLine("spawn points and the floor beneath them:");
            var spawnPoints = new List<Transform>();
            CollectByPrefix(root, "PlayerSpawnPoint", spawnPoints);
            if (spawnPoints.Count == 0)
                report.AppendLine("  WARN: none found");

            foreach (Transform spawn in spawnPoints)
            {
                // Start above the spawn pivot so the cast begins clear of whatever the player stands on,
                // and ignore triggers: WalkZone is itself a trigger capsule wrapping this whole space,
                // so a default query reports its lid at y=1.88 and never reaches the hull floor at all.
                Vector3 origin = spawn.position + Vector3.up * 2f;
                string floor = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 12f,
                        ~0, QueryTriggerInteraction.Ignore)
                    ? $"floor y={hit.point.y:0.###} on '{hit.collider.name}' (layer {hit.collider.gameObject.layer}) " +
                      $"= {spawn.position.y - hit.point.y:0.###} m below the spawn pivot"
                    : "NO SOLID FLOOR within 12 m below";
                report.AppendLine($"  '{spawn.name}' world={Fmt(spawn.position)}  {floor}");
            }

            // The candidate hammer spot, reported with its clearance to all four spawn points so the 3 m
            // interact range is confirmed rather than assumed.
            //
            // z is the midpoint of the spawn slots, not an eyeballed offset: the slots run from +2.264
            // to -2.265, so their centre is the only z that keeps the farthest slot inside 3 m.
            // y comes from the measured hull floor, because the Hammer's pivot sits at its own base.
            if (spawnPoints.Count > 0)
            {
                float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
                float floorY = float.PositiveInfinity;
                foreach (Transform spawn in spawnPoints)
                {
                    minZ = Mathf.Min(minZ, spawn.position.z);
                    maxZ = Mathf.Max(maxZ, spawn.position.z);
                    if (Physics.Raycast(spawn.position + Vector3.up * 2f, Vector3.down,
                            out RaycastHit floorHit, 12f, ~0, QueryTriggerInteraction.Ignore))
                        floorY = Mathf.Min(floorY, floorHit.point.y);
                }

                bool floorFound = !float.IsPositiveInfinity(floorY);
                var candidate = new Vector3(
                    root.position.x,
                    floorFound ? floorY + 0.05f : spawnPoints[0].position.y + 0.1f,
                    (minZ + maxZ) * 0.5f);

                report.AppendLine($"candidate hammer spot {Fmt(candidate)} " +
                                  $"(floor {(floorFound ? $"measured y={floorY:0.###}" : "NOT FOUND - fell back to spawn y")}):");
                foreach (Transform spawn in spawnPoints)
                    report.AppendLine($"  to '{spawn.name}' = {Vector3.Distance(candidate, spawn.position):0.###} m" +
                                      (Vector3.Distance(candidate, spawn.position) <= 3f ? " OK" : "  ** OUT OF RANGE **"));

                if (walkZone != null)
                {
                    var capsule = walkZone.GetComponent<CapsuleCollider>();
                    if (capsule != null)
                    {
                        Vector3 closest = capsule.ClosestPoint(candidate);
                        float inside = Vector3.Distance(closest, candidate);
                        report.AppendLine($"  inside WalkZone = {(inside < 0.001f ? "YES" : $"NO (nearest surface {inside:0.###} m away)")}");
                    }
                }
            }

            report.AppendLine("named interior anchors:");
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == root || child.parent != root)
                    continue;
                report.AppendLine($"  '{child.name}' local={Fmt(child.localPosition)} world={Fmt(child.position)} " +
                                  $"localScale={Fmt(child.localScale)} active={child.gameObject.activeSelf}");
            }
        }

        // ---------------------------------------------------------------- routes

        private static readonly float[] SweepAngles = { 0f, 90f, 165f, 180f, 195f, 270f };

        private static void MeasureRoutes(StringBuilder report)
        {
            report.AppendLine();
            report.AppendLine("--- routes ---");

            CaveDecorContext context = CaveDecorContext.Create();
            if (!context.IsValid)
            {
                report.AppendLine("FAIL: no CaveRoute or CaveShell collider in the scene");
                return;
            }

            report.Append("route ids:");
            foreach (string id in context.RouteIds)
                report.Append(' ').Append(id);
            report.AppendLine();

            MeasureMainRoute(report, context);
            MeasureBranches(report, context);
        }

        private static void MeasureMainRoute(StringBuilder report, CaveDecorContext context)
        {
            if (!context.TryGetRoute("MainRoute", out _, out CaveRoutePolyline polyline,
                    out CaveRouteSplineDefinition definition))
            {
                report.AppendLine("FAIL: no MainRoute");
                return;
            }

            report.AppendLine($"[MainRoute] polylineLength={polyline.Length:0.###} m");
            foreach (CaveRouteSection section in definition.sections)
            {
                report.AppendLine($"  {section.zoneId}: {section.startDistanceMeters:0.###} - " +
                                  $"{section.endDistanceMeters:0.###} guide={section.guideSize.x}x{section.guideSize.y}");
            }

            report.AppendLine("  radius sweep (centreline distance to shell, C/F/W = ceiling/floor/wall):");
            foreach (CaveRouteSection section in definition.sections)
            {
                float start = section.startDistanceMeters;
                float end = section.endDistanceMeters;
                for (float d = start + 5f; d <= end - 5f; d += 15f)
                    report.AppendLine("   " + SweepLine(context, "MainRoute", d, section.zoneId));
            }
        }

        /// <summary>
        /// The load-bearing check. Branch sections carry startDistanceMeters = -1, so the layout has to
        /// address them as 0..polyline.Length and ResolveZoneId returns the literal section id. If any
        /// of this misbehaves, the nine branch oxygen tanks move to the main route near each portal.
        /// </summary>
        private static void MeasureBranches(StringBuilder report, CaveDecorContext context)
        {
            foreach (string routeId in new[] { "Z2_Branch", "Z4_Branch", "Z5_Branch" })
            {
                report.AppendLine();
                if (!context.TryGetRoute(routeId, out _, out CaveRoutePolyline polyline,
                        out CaveRouteSplineDefinition definition))
                {
                    report.AppendLine($"[{routeId}] FAIL: route not bound in the context");
                    continue;
                }

                report.AppendLine($"[{routeId}] polylineLength={polyline.Length:0.###} m " +
                                  $"splineIndex={definition.splineIndex} isMainRoute={definition.isMainRoute}");
                foreach (CaveRouteSection section in definition.sections)
                {
                    report.AppendLine($"  section '{section.zoneId}' start={section.startDistanceMeters} " +
                                      $"end={section.endDistanceMeters} nominal={section.nominalLength} " +
                                      $"guide={section.guideSize.x}x{section.guideSize.y} capEnd={section.capEnd}");
                }

                int hits = 0;
                int casts = 0;
                for (float d = 4f; d <= polyline.Length - 2f; d += 6f)
                {
                    string zone = context.ResolveZoneId(routeId, d);
                    report.AppendLine("   " + SweepLine(context, routeId, d, zone, ref hits, ref casts));
                }

                report.AppendLine($"  >> {routeId}: {hits}/{casts} casts hit the shell" +
                                  (hits == 0 ? "  ** BRANCH CASTING UNUSABLE - fall back to main route **" : ""));
            }
        }

        private static string SweepLine(CaveDecorContext context, string routeId, float distance, string zoneId)
        {
            int hits = 0;
            int casts = 0;
            return SweepLine(context, routeId, distance, zoneId, ref hits, ref casts);
        }

        private static string SweepLine(CaveDecorContext context, string routeId, float distance,
            string zoneId, ref int hits, ref int casts)
        {
            var line = new StringBuilder();
            line.Append($"{zoneId,-10} d={distance,7:0.0}");
            foreach (float angle in SweepAngles)
            {
                casts++;
                if (CaveDecorProjector.TryCast(context, routeId, distance, angle, out CaveDecorSurface surface))
                {
                    hits++;
                    line.Append($"  {angle,3:0}={surface.CenterlineDistance,6:0.00}{KindTag(surface.kind)}");
                }
                else
                {
                    line.Append($"  {angle,3:0}=  MISS ");
                }
            }
            return line.ToString();
        }

        // ---------------------------------------------------------------- helpers

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child;
            }
            return null;
        }

        private static void CollectByPrefix(Transform root, string prefix, List<Transform> into)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith(prefix))
                    into.Add(child);
            }
        }

        private static string KindTag(CaveSurfaceKind kind)
        {
            switch (kind)
            {
                case CaveSurfaceKind.Ceiling: return "C";
                case CaveSurfaceKind.Floor: return "F";
                case CaveSurfaceKind.Wall: return "W";
                default: return "?";
            }
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";
    }
}
