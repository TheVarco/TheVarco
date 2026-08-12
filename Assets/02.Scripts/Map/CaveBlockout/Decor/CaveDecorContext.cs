using System.Collections.Generic;
using UnityEngine;

namespace CaveBlockout.Decor
{
    /// <summary>Everything the projector needs to turn a route-relative record into a world transform.</summary>
    public sealed class CaveDecorContext
    {
        private sealed class RouteBinding
        {
            public CaveRoute route;
            public CaveRouteSplineDefinition definition;
            public CaveRoutePolyline polyline;
        }

        private readonly Dictionary<string, RouteBinding> bindings = new Dictionary<string, RouteBinding>();

        /// <summary>
        /// The shell collider is held explicitly and queried with <see cref="Collider.Raycast"/> rather
        /// than <see cref="Physics.Raycast"/>. Scattered props that carry colliders sit on the same
        /// layer as the shell, so a layer-masked scene query would happily report a previously placed
        /// boulder as "the cave surface" and stack props on top of each other.
        /// </summary>
        public MeshCollider Shell { get; private set; }

        public IEnumerable<string> RouteIds => bindings.Keys;

        public bool IsValid => Shell != null && bindings.Count > 0;

        public static CaveDecorContext Create()
        {
            var context = new CaveDecorContext();

            var routes = Object.FindObjectsByType<CaveRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (CaveRoute route in routes)
            {
                if (route == null || route.Definitions == null)
                    continue;

                foreach (CaveRouteSplineDefinition definition in route.Definitions)
                {
                    if (definition == null || string.IsNullOrEmpty(definition.routeId))
                        continue;
                    if (context.bindings.ContainsKey(definition.routeId))
                        continue;

                    CaveRoutePolyline polyline = CaveRoutePolyline.Build(route, definition.splineIndex);
                    if (polyline == null)
                        continue;

                    context.bindings[definition.routeId] = new RouteBinding
                    {
                        route = route,
                        definition = definition,
                        polyline = polyline
                    };
                }
            }

            foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (collider != null && collider.gameObject.name == CaveDecorNames.ShellObject)
                {
                    context.Shell = collider;
                    break;
                }
            }

            // Editor-time physics queries read stale transforms unless this is forced; see
            // CaveClearanceValidator, which does the same before its BoxCasts.
            Physics.SyncTransforms();
            return context;
        }

        public bool TryGetRoute(string routeId, out CaveRoute route, out CaveRoutePolyline polyline,
            out CaveRouteSplineDefinition definition)
        {
            route = null;
            polyline = null;
            definition = null;
            if (string.IsNullOrEmpty(routeId) || !bindings.TryGetValue(routeId, out RouteBinding binding))
                return false;

            route = binding.route;
            polyline = binding.polyline;
            definition = binding.definition;
            return route != null && polyline != null && polyline.IsValid;
        }

        public CaveRoutePolyline GetPolyline(string routeId)
        {
            return bindings.TryGetValue(routeId, out RouteBinding binding) ? binding.polyline : null;
        }

        /// <summary>Route id whose centre line runs closest to <paramref name="worldPosition"/>.</summary>
        public string FindNearestRouteId(Vector3 worldPosition, out float routeDistance, out float lateralDistance)
        {
            routeDistance = 0f;
            lateralDistance = float.PositiveInfinity;
            string best = null;

            foreach (var pair in bindings)
            {
                float distance = pair.Value.polyline.Project(worldPosition, out float lateral);
                if (lateral >= lateralDistance)
                    continue;

                lateralDistance = lateral;
                routeDistance = distance;
                best = pair.Key;
            }

            return best;
        }

        /// <summary>
        /// Zone that owns <paramref name="routeDistance"/> on <paramref name="routeId"/>. Branch
        /// definitions carry a single section with unresolved distances (-1), so they fall back to
        /// that section's id.
        /// </summary>
        public string ResolveZoneId(string routeId, float routeDistance)
        {
            if (!bindings.TryGetValue(routeId, out RouteBinding binding))
                return null;

            List<CaveRouteSection> sections = binding.definition.sections;
            if (sections == null || sections.Count == 0)
                return null;

            for (int i = 0; i < sections.Count; i++)
            {
                CaveRouteSection section = sections[i];
                if (section.startDistanceMeters < 0f || section.endDistanceMeters < 0f)
                    continue;
                if (routeDistance >= section.startDistanceMeters && routeDistance <= section.endDistanceMeters)
                    return section.zoneId;
            }

            // Past the authored end (float drift at the tail) or an unresolved branch: clamp.
            return routeDistance <= 0f ? sections[0].zoneId : sections[sections.Count - 1].zoneId;
        }
    }

    /// <summary>Scene object and asset names the decor tools rely on, kept in one place.</summary>
    public static class CaveDecorNames
    {
        public const string BlockoutRoot = "CaveBlockout";
        public const string ShellObject = "CaveShell";

        /// <summary>
        /// Decor lives under CaveBlockout/Decor/&lt;Zone&gt;_Decor/. CaveBlockoutBuilder's
        /// ClearToolOwnedChildren only destroys Routes, Generated, Markers, Validation and Playtest,
        /// so this sixth group survives every rebuild path - and the scene already ships a hand-made
        /// Decor/Z1_Decor pair using exactly these names.
        /// </summary>
        public const string DecorRoot = "Decor";

        public static string ZoneGroup(string zoneId)
        {
            return string.IsNullOrEmpty(zoneId) ? "Unzoned_Decor" : zoneId + "_Decor";
        }
    }
}
