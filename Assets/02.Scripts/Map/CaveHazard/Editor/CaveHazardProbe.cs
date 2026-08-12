using System.Text;
using CaveBlockout;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CaveHazard.EditorTools
{
    /// <summary>
    /// Measures the two things the hazard layout depends on and neither the guide nor the prefab
    /// inspector states: how far the cave surface actually sits from the route centre line in Z3 and
    /// Z5, and how far each obstacle prefab physically reaches.
    ///
    /// This exists because the numbers turned out to disagree with the obvious reading. Z5's authored
    /// profile is 30 x 60, so its floor is ~30 m below the line the player swims, while the Vent's
    /// hazard capsule is 6 m tall. A layout written from the guide table alone would have placed six
    /// vents that no player could ever touch, and every seating check would have passed.
    ///
    /// Lives in Assembly-CSharp-Editor rather than CaveBlockout.Editor on purpose: VentController and
    /// RockSpawner are in the default assembly, and an asmdef assembly cannot reference that.
    /// </summary>
    public static class CaveHazardProbe
    {
        private const string MainMapPath = "Assets/01.Scenes/MainMap.unity";
        private const string VentPrefab = "Assets/03.Prefabs/Obstacle/Vent/Vent.prefab";
        private const string SpawnerPrefab = "Assets/03.Prefabs/Obstacle/FallingRock/FallingRockSpawner.prefab";
        private const string RockPrefab = "Assets/03.Prefabs/Obstacle/FallingRock/FallingRock.prefab";

        [MenuItem("Tools/Underwater Cave/Hazards/0 - 단면과 프리팹 실측")]
        public static void ProbeInteractive()
        {
            Debug.Log(Probe());
        }

        public static void ProbeBatch()
        {
            EditorSceneManager.OpenScene(MainMapPath, OpenSceneMode.Single);
            Debug.Log(Probe());
        }

        public static string Probe()
        {
            var report = new StringBuilder();
            report.AppendLine("===== CAVE HAZARD PROBE =====");

            MeasurePrefabs(report);
            MeasureSections(report);

            report.AppendLine("CAVE_HAZARD_PROBE DONE");
            return report.ToString();
        }

        // ---------- prefabs ----------

        private static void MeasurePrefabs(StringBuilder report)
        {
            report.AppendLine("--- prefabs ---");
            MeasurePrefab(report, VentPrefab);
            MeasurePrefab(report, SpawnerPrefab);
            MeasurePrefab(report, RockPrefab);
        }

        private static void MeasurePrefab(StringBuilder report, string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                report.AppendLine($"MISSING {path}");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                // Identity at the origin, so every bound below is readable as "metres from the mount
                // point" rather than as a world coordinate that depends on where the prefab was saved.
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                report.AppendLine($"[{asset.name}]");

                // Mesh renderers only. Particle system bounds report the current simulation state,
                // which in an unplayed editor instance is an empty box centred on the emitter and
                // would read as "this vent is 0 m tall".
                var meshBounds = new Bounds();
                bool hasMesh = false;
                foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer == null)
                        continue;
                    if (!hasMesh) { meshBounds = renderer.bounds; hasMesh = true; }
                    else meshBounds.Encapsulate(renderer.bounds);
                }
                report.AppendLine(hasMesh
                    ? $"  mesh   size={Fmt(meshBounds.size)} centre={Fmt(meshBounds.center)} " +
                      $"min={Fmt(meshBounds.min)} max={Fmt(meshBounds.max)}"
                    : "  mesh   (none)");

                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null)
                        continue;
                    Bounds b = collider.bounds;
                    string extra = collider is CapsuleCollider capsule
                        ? $" capsule(r={capsule.radius:0.##} h={capsule.height:0.##} axis={capsule.direction} centre={Fmt(capsule.center)})"
                        : string.Empty;
                    report.AppendLine($"  col    {collider.GetType().Name} on '{collider.name}' " +
                                      $"trigger={collider.isTrigger} enabled={collider.enabled} " +
                                      $"size={Fmt(b.size)} min.y={b.min.y:0.##} max.y={b.max.y:0.##}{extra}");
                }

                foreach (Light light in instance.GetComponentsInChildren<Light>(true))
                {
                    if (light == null)
                        continue;
                    // The spot light is the falling-rock warning. Its forward is what tells us whether a
                    // tilted mount would aim the warning at the wall instead of the landing zone.
                    report.AppendLine($"  light  '{light.name}' type={light.type} range={light.range} " +
                                      $"spotAngle={light.spotAngle} forward={Fmt(light.transform.forward)}");
                }

                foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (child == instance.transform)
                        continue;
                    report.AppendLine($"  child  '{child.name}' localPos={Fmt(child.localPosition)} " +
                                      $"localScale={Fmt(child.localScale)} up={Fmt(child.up)}");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---------- cave sections ----------

        private static readonly float[] ProbeAngles = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

        private static void MeasureSections(StringBuilder report)
        {
            report.AppendLine("--- cave sections ---");

            CaveDecorContext context = CaveDecorContext.Create();
            if (!context.IsValid)
            {
                report.AppendLine("FAIL: no CaveRoute or CaveShell collider in the scene");
                return;
            }

            if (!context.TryGetRoute("MainRoute", out _, out CaveRoutePolyline polyline,
                    out CaveRouteSplineDefinition definition))
            {
                report.AppendLine("FAIL: no MainRoute");
                return;
            }

            report.AppendLine($"route length = {polyline.Length:0.##} m");

            foreach (CaveRouteSection section in definition.sections)
            {
                if (section.zoneId != "Z3" && section.zoneId != "Z5")
                    continue;

                float start = section.startDistanceMeters;
                float end = section.endDistanceMeters;
                report.AppendLine($"[{section.zoneId}] {start:0.##} - {end:0.##} m " +
                                  $"(len {end - start:0.##}), guideSize={Fmt2(section.guideSize)}");

                for (float distance = start + 5f; distance <= end - 5f; distance += 10f)
                {
                    var line = new StringBuilder();
                    line.Append($"  d={distance,7:0.0}");
                    foreach (float angle in ProbeAngles)
                    {
                        if (CaveDecorProjector.TryCast(context, "MainRoute", distance, angle,
                                out CaveDecorSurface surface))
                        {
                            line.Append($"  {angle,3:0}deg={surface.CenterlineDistance,6:0.00}" +
                                        $"{KindTag(surface.kind)}");
                        }
                        else
                        {
                            line.Append($"  {angle,3:0}deg=  MISS ");
                        }
                    }
                    report.AppendLine(line.ToString());
                }

                ReportVerticalDrop(report, context, section);
            }
        }

        /// <summary>
        /// How far a rock dropped from the ceiling actually falls before the wall gets in the way.
        ///
        /// Rocks fall along world -Y under Physics.gravity regardless of how the spawner is rotated, so
        /// on a tunnel that climbs at ~29 degrees a ceiling mount does not necessarily have open water
        /// under it. A short drop here means the rock lands on the wall beside the player instead of
        /// crossing the channel, which is the difference between a hazard and a decoration.
        /// </summary>
        private static void ReportVerticalDrop(StringBuilder report, CaveDecorContext context,
            CaveRouteSection section)
        {
            report.AppendLine($"  [{section.zoneId}] vertical drop from ceiling mounts (world -Y):");
            for (float distance = section.startDistanceMeters + 5f;
                 distance <= section.endDistanceMeters - 5f;
                 distance += 20f)
            {
                foreach (float angle in new[] { -25f, 0f, 25f })
                {
                    if (!CaveDecorProjector.TryCast(context, "MainRoute", distance, angle,
                            out CaveDecorSurface surface))
                    {
                        report.AppendLine($"    d={distance:0.0} a={angle:0} MISS");
                        continue;
                    }

                    // Start just clear of the rock face so the first hit is not the surface we mounted on.
                    Vector3 origin = surface.point + surface.normal * 0.5f;
                    bool hit = context.Shell.Raycast(new Ray(origin, Vector3.down), out RaycastHit info, 400f);
                    report.AppendLine($"    d={distance,6:0.0} a={angle,4:0} mount={Fmt(surface.point)} " +
                                      $"kind={surface.kind} centreDist={surface.CenterlineDistance:0.00} " +
                                      (hit ? $"drop={info.distance:0.00} m" : "drop=NO FLOOR HIT"));
                }
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

        private static string Fmt(Vector3 v) => $"({v.x:0.##},{v.y:0.##},{v.z:0.##})";
        private static string Fmt2(Vector2 v) => $"({v.x:0.##}x{v.y:0.##})";
    }
}
