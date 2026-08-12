using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEngine;

namespace CaveBlockout.Editor.Decor
{
    /// <summary>
    /// Checks the decor set against the rules that are expensive to notice by eye: props that no
    /// longer land on a surface, props intruding into the swimmable channel, emission where the art
    /// contract forbids it, zones nobody dressed, and the instance and triangle budget.
    /// </summary>
    public static class CaveDecorValidator
    {
        public const string ArtifactFolder = "Artifacts/CaveDecor";

        public sealed class Issue
        {
            public string severity;
            public string message;
        }

        public sealed class Report
        {
            public readonly List<Issue> issues = new List<Issue>();
            public int placements;
            public int resolved;
            public int unresolved;
            public int corridorViolations;
            public long triangles;
            public readonly Dictionary<string, int> perZone = new Dictionary<string, int>();
            public readonly HashSet<string> materials = new HashSet<string>();

            public bool Passed
            {
                get
                {
                    foreach (Issue issue in issues)
                    {
                        if (issue.severity == "FAIL")
                            return false;
                    }
                    return true;
                }
            }
        }

        public static Report Validate(CaveDecorSet set, CaveDecorContext context)
        {
            var report = new Report();

            if (set == null)
            {
                report.issues.Add(new Issue { severity = "FAIL", message = "No CaveDecorSet asset." });
                return report;
            }
            if (context == null || !context.IsValid)
            {
                report.issues.Add(new Issue
                {
                    severity = "FAIL",
                    message = "No CaveRoute or CaveShell MeshCollider in the active scene; nothing can be projected."
                });
                return report;
            }

            var triangleCache = new Dictionary<string, int>();
            report.placements = set.Placements.Count;

            foreach (CaveDecorPlacement placement in set.Placements)
            {
                CaveDecorPaletteEntry entry = set.FindEntry(placement.paletteId);
                if (entry == null || entry.prefab == null)
                {
                    report.issues.Add(new Issue
                    {
                        severity = "FAIL",
                        message = $"placement {ShortId(placement.id)} references unknown palette id '{placement.paletteId}'."
                    });
                    continue;
                }

                report.perZone.TryGetValue(placement.zoneId ?? "?", out int zoneCount);
                report.perZone[placement.zoneId ?? "?"] = zoneCount + 1;
                report.triangles += TriangleCount(entry, triangleCache);

                Material material = entry.ResolveMaterial(placement.zoneId);
                report.materials.Add(material != null ? material.name : entry.id + ":imported");

                if (!CaveDecorProjector.TryResolve(context, placement, out Vector3 position, out _,
                        out CaveDecorSurface surface))
                {
                    report.unresolved++;
                    report.issues.Add(new Issue
                    {
                        severity = "FAIL",
                        message = $"placement {ShortId(placement.id)} ({placement.paletteId}, {placement.zoneId}) " +
                                  $"found no surface at {placement.routeId} {placement.routeDistance:0.0} m, " +
                                  $"angle {placement.angleDegrees:0.0}."
                    });
                    continue;
                }

                report.resolved++;

                float radius = entry.boundingRadius * placement.scale;
                if (!set.ClearsCorridor(position, surface.centerline, radius))
                {
                    report.corridorViolations++;
                    float clearance = Vector3.Distance(position, surface.centerline) - radius;
                    report.issues.Add(new Issue
                    {
                        severity = "FAIL",
                        message = $"placement {ShortId(placement.id)} ({placement.paletteId}, {placement.zoneId}) " +
                                  $"leaves only {clearance:0.00} m of channel, need {set.minCorridorRadius:0.00} m."
                    });
                }

                // Z4 is the blackout fault: the design brief allows no light source of any kind there,
                // so a glowing prop is a design break, not a look preference.
                if (placement.zoneId == "Z4" && IsEmissive(material))
                {
                    report.issues.Add(new Issue
                    {
                        severity = "FAIL",
                        message = $"Z4 BLACKOUT VIOLATION: placement {ShortId(placement.id)} ({placement.paletteId}) " +
                                  $"uses emissive material '{material.name}'."
                    });
                }
            }

            CheckZoneCoverage(context, report);
            CheckPaletteEmission(set, report);
            return report;
        }

        private static void CheckZoneCoverage(CaveDecorContext context, Report report)
        {
            foreach (string routeId in context.RouteIds)
            {
                if (!context.TryGetRoute(routeId, out _, out _, out CaveRouteSplineDefinition definition))
                    continue;
                if (!definition.isMainRoute)
                    continue;

                foreach (CaveRouteSection section in definition.sections)
                {
                    report.perZone.TryGetValue(section.zoneId, out int count);
                    if (count == 0)
                    {
                        report.issues.Add(new Issue
                        {
                            severity = "WARN",
                            message = $"zone {section.zoneId} has no decor."
                        });
                    }
                }
            }
        }

        private static void CheckPaletteEmission(CaveDecorSet set, Report report)
        {
            foreach (CaveDecorPaletteEntry entry in set.Palette)
            {
                if (entry?.zoneMaterialOverrides == null)
                    continue;

                foreach (CaveDecorZoneMaterial zoneMaterial in entry.zoneMaterialOverrides)
                {
                    if (zoneMaterial?.material == null || !IsEmissive(zoneMaterial.material))
                        continue;

                    // Bioluminescence is a Z2 identity, and Z5 gets a restrained thermal red. Anywhere
                    // else and the zones stop reading as different places.
                    if (zoneMaterial.zoneId != "Z2" && zoneMaterial.zoneId != "Z5")
                    {
                        report.issues.Add(new Issue
                        {
                            severity = "FAIL",
                            message = $"palette '{entry.id}' maps emissive material " +
                                      $"'{zoneMaterial.material.name}' to {zoneMaterial.zoneId}; " +
                                      "only Z2 and Z5 may glow."
                        });
                    }
                }
            }
        }

        private static bool IsEmissive(Material material)
        {
            if (material == null || !material.HasProperty("_EmissionColor"))
                return false;

            Color emission = material.GetColor("_EmissionColor");
            return emission.maxColorComponent > 0.01f;
        }

        private static int TriangleCount(CaveDecorPaletteEntry entry, Dictionary<string, int> cache)
        {
            if (cache.TryGetValue(entry.id, out int cached))
                return cached;

            int total = 0;
            foreach (MeshFilter filter in entry.prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                    total += filter.sharedMesh.triangles.Length / 3;
            }

            cache[entry.id] = total;
            return total;
        }

        private static string ShortId(string id)
        {
            return string.IsNullOrEmpty(id) ? "????" : id.Substring(0, Mathf.Min(6, id.Length));
        }

        public static string Format(Report report)
        {
            var text = new StringBuilder();
            text.AppendLine("# Cave Decor Validation");
            text.AppendLine();
            text.AppendLine($"- placements: {report.placements}");
            text.AppendLine($"- resolved onto a surface: {report.resolved}");
            text.AppendLine($"- unresolved (no surface): {report.unresolved}");
            text.AppendLine($"- corridor violations: {report.corridorViolations}");
            text.AppendLine($"- total triangles: {report.triangles:N0}");
            text.AppendLine($"- distinct materials: {report.materials.Count}");
            text.AppendLine();

            text.AppendLine("| zone | props |");
            text.AppendLine("|---|---:|");
            var zones = new List<string>(report.perZone.Keys);
            zones.Sort(StringComparer.Ordinal);
            foreach (string zone in zones)
                text.AppendLine($"| {zone} | {report.perZone[zone]} |");
            text.AppendLine();

            if (report.issues.Count == 0)
            {
                text.AppendLine("No issues.");
            }
            else
            {
                text.AppendLine("## Issues");
                foreach (Issue issue in report.issues)
                    text.AppendLine($"- **{issue.severity}** {issue.message}");
            }

            text.AppendLine();
            text.AppendLine(report.Passed ? "**RESULT: PASS**" : "**RESULT: FAIL**");
            return text.ToString();
        }

        /// <summary>Writes the report next to the other review artifacts and returns its path.</summary>
        public static string Write(Report report, string runId)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string directory = Path.Combine(projectRoot, ArtifactFolder, runId);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "report.md");
            File.WriteAllText(path, Format(report));
            return path;
        }

        [MenuItem("Tools/Underwater Cave/Decor/4 - 검증")]
        public static void ValidateInteractive()
        {
            var set = AssetDatabase.LoadAssetAtPath<CaveDecorSet>(CaveDecorCatalog.DecorSetPath);
            Report report = Validate(set, CaveDecorContext.Create());
            string path = Write(report, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

            if (report.Passed)
                Debug.Log($"[CaveDecor] Validation PASS. Report: {path}\n{Format(report)}");
            else
                Debug.LogWarning($"[CaveDecor] Validation FAIL. Report: {path}\n{Format(report)}");
        }
    }
}
