using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout.Editor
{
    public static class CaveClearanceValidator
    {
        private static readonly Vector3 SubmarineHalfExtents = new Vector3(1.5f, 1.5f, 3f);

        public static bool ValidateAll(CaveRoute mainRoute, CaveRoute branches, out string details)
        {
            Physics.SyncTransforms();
            List<string> failures = new List<string>();
            ValidateRoute(mainRoute, failures);
            ValidateRoute(branches, failures);
            ValidatePortals(mainRoute, branches, failures);
            details = failures.Count == 0 ? "All main and branch paths clear a 6x3x3m proxy in both directions." : string.Join("\n", failures);
            return failures.Count == 0;
        }

        private static void ValidatePortals(CaveRoute mainRoute, CaveRoute branches, List<string> failures)
        {
            if (mainRoute == null || branches == null)
                return;

            foreach (CavePortalDefinition portal in mainRoute.Portals)
            {
                CaveRouteSplineDefinition branchDefinition = null;
                foreach (CaveRouteSplineDefinition definition in branches.Definitions)
                {
                    if (definition.splineIndex == portal.branchSplineIndex)
                    {
                        branchDefinition = definition;
                        break;
                    }
                }
                if (branchDefinition == null)
                {
                    failures.Add(portal.zoneId + " portal: matching branch definition is missing.");
                    continue;
                }

                Spline branchSpline = branches.Container[portal.branchSplineIndex];
                float connectionT = CaveMeshGenerator.FindTAtDistance(
                    branches.Container,
                    portal.branchSplineIndex,
                    0f,
                    1f,
                    branchDefinition.startTrimMeters + CaveMeshGenerator.BranchCollarDepth + 8f);
                if (!ValidateDirection(branches, portal.branchSplineIndex, 0f, connectionT, out string outwardIssue))
                    failures.Add(portal.zoneId + " portal outward: " + outwardIssue);
                if (!ValidateDirection(branches, portal.branchSplineIndex, connectionT, 0f, out string inwardIssue))
                    failures.Add(portal.zoneId + " portal inward: " + inwardIssue);
            }
        }

        private static void ValidateRoute(CaveRoute route, List<string> failures)
        {
            if (route == null)
                return;

            foreach (CaveRouteSplineDefinition definition in route.Definitions)
            {
                foreach (CaveRouteSection section in definition.sections)
                {
                    float startT = route.ResolveSectionStartT(definition, section);
                    float endT = route.ResolveSectionEndT(definition, section);
                if (definition.startTrimMeters > 0f)
                        startT = CaveMeshGenerator.FindTAtDistance(route.Container, definition.splineIndex, startT, endT,
                            definition.startTrimMeters + CaveMeshGenerator.BranchCollarDepth + 3.5f);

                    if (section.capStart)
                        startT = CaveMeshGenerator.FindTAtDistance(route.Container, definition.splineIndex, startT, endT, 4.5f);
                    if (section.capEnd)
                        endT = CaveMeshGenerator.FindTAtDistance(route.Container, definition.splineIndex, endT, startT, 4.5f);

                    float safeStart = Mathf.Lerp(startT, endT, 0.005f);
                    float safeEnd = Mathf.Lerp(startT, endT, 0.995f);
                    startT = safeStart;
                    endT = safeEnd;
                    if (!ValidateDirection(route, definition.splineIndex, startT, endT, out string forwardIssue))
                        failures.Add(definition.routeId + " forward: " + forwardIssue);
                    if (!ValidateDirection(route, definition.splineIndex, endT, startT, out string reverseIssue))
                        failures.Add(definition.routeId + " reverse: " + reverseIssue);
                }
            }
        }

        private static bool ValidateDirection(CaveRoute route, int splineIndex, float startT, float endT, out string issue)
        {
            const int samples = 80;
            Vector3 previous = route.Container.EvaluatePosition(splineIndex, startT);
            for (int i = 1; i <= samples; i++)
            {
                float t = Mathf.Lerp(startT, endT, i / (float)samples);
                Vector3 current = route.Container.EvaluatePosition(splineIndex, t);
                Vector3 delta = current - previous;
                if (delta.sqrMagnitude < 0.0001f)
                    continue;

                Vector3 up = route.Container.EvaluateUpVector(splineIndex, t);
                Quaternion orientation = Quaternion.LookRotation(delta.normalized, up);
                if (Physics.BoxCast(previous, SubmarineHalfExtents, delta.normalized, out RaycastHit hit, orientation, delta.magnitude, 1 << 0, QueryTriggerInteraction.Ignore))
                {
                    issue = $"blocked by {hit.collider.name} near t={t:F3}, hit={hit.point}, from={previous}, toward={current}.";
                    return false;
                }
                previous = current;
            }

            issue = string.Empty;
            return true;
        }
    }
}
