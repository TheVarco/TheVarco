using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout.Editor
{
    public readonly struct CaveRouteKnotInsertionResult
    {
        public readonly int splineIndex;
        public readonly int segmentIndex;
        public readonly int knotIndex;
        public readonly float curveT;
        public readonly Vector3 worldPosition;

        public CaveRouteKnotInsertionResult(int splineIndex, int segmentIndex, int knotIndex, float curveT, Vector3 worldPosition)
        {
            this.splineIndex = splineIndex;
            this.segmentIndex = segmentIndex;
            this.knotIndex = knotIndex;
            this.curveT = curveT;
            this.worldPosition = worldPosition;
        }
    }

    /// <summary>
    /// Editor-only, undoable spline operations that preserve the authored cave metadata.
    /// </summary>
    public static class CaveRouteEditingUtility
    {
        private const string UndoName = "Insert cave route midpoint";

        public static CaveRouteKnotInsertionResult InsertKnotAtSegmentMidpoint(
            CaveRoute route, int splineIndex, int segmentIndex, bool selectInsertedKnot = true)
        {
            if (route == null)
                throw new ArgumentNullException(nameof(route));

            SplineContainer container = route.Container;
            if (container == null || splineIndex < 0 || splineIndex >= container.Splines.Count)
                throw new ArgumentOutOfRangeException(nameof(splineIndex));

            Spline spline = container[splineIndex];
            if (spline.Closed)
                throw new InvalidOperationException("Cave route midpoint insertion currently supports open splines only.");
            if (segmentIndex < 0 || segmentIndex >= spline.Count - 1)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));

            float curveLength = spline.GetCurveLength(segmentIndex);
            if (curveLength <= 0.0001f)
                throw new InvalidOperationException("Cannot split a zero-length spline segment.");

            float curveT = spline.GetCurveInterpolation(segmentIndex, curveLength * 0.5f);
            float knotT = segmentIndex + curveT;
            float width = EvaluateFloat(spline, CaveRoute.WidthDataKey, knotT, 12f);
            float height = EvaluateFloat(spline, CaveRoute.HeightDataKey, knotT, 10f);
            float roll = EvaluateAngle(spline, CaveRoute.RollDataKey, segmentIndex, curveT);
            int zone = GetValueAtOrBefore(spline, CaveRoute.ZoneDataKey, segmentIndex, 0);

            BezierCurve sourceCurve = spline.GetCurve(segmentIndex);
            CurveUtility.Split(sourceCurve, curveT, out BezierCurve leftCurve, out BezierCurve rightCurve);
            float3 up = math.normalizesafe(spline.GetCurveUpVector(segmentIndex, curveT), math.up());
            float3 forward = math.normalizesafe(rightCurve.Tangent0, leftCurve.Tangent1);
            quaternion rotation = quaternion.LookRotationSafe(forward, up);
            quaternion inverseRotation = math.inverse(rotation);
            int insertIndex = segmentIndex + 1;

            Undo.RecordObjects(new UnityEngine.Object[] { container, route }, UndoName);

            // Freeze the two affected knots before splitting. Auto-smooth or linked tangent modes would otherwise
            // recalculate adjacent curves when the new knot is inserted and unexpectedly move the authored route.
            if (spline.GetTangentMode(segmentIndex) != TangentMode.Broken)
                spline.SetTangentMode(segmentIndex, TangentMode.Broken);
            if (spline.GetTangentMode(insertIndex) != TangentMode.Broken)
                spline.SetTangentMode(insertIndex, TangentMode.Broken);

            BezierKnot previous = spline[segmentIndex];
            BezierKnot next = spline[insertIndex];
            previous.TangentOut = math.mul(math.inverse(previous.Rotation), leftCurve.Tangent0);
            next.TangentIn = math.mul(math.inverse(next.Rotation), rightCurve.Tangent1);
            spline[segmentIndex] = previous;
            spline[insertIndex] = next;

            BezierKnot inserted = new BezierKnot(
                leftCurve.P3,
                math.mul(inverseRotation, leftCurve.Tangent1),
                math.mul(inverseRotation, rightCurve.Tangent0),
                rotation);
            spline.Insert(insertIndex, inserted, TangentMode.Broken);

            AddFloatDataPoint(spline, CaveRoute.WidthDataKey, insertIndex, width);
            AddFloatDataPoint(spline, CaveRoute.HeightDataKey, insertIndex, height);
            AddFloatDataPoint(spline, CaveRoute.RollDataKey, insertIndex, roll);
            AddIntDataPoint(spline, CaveRoute.ZoneDataKey, insertIndex, zone);
            AddIntDataPoint(spline, CaveRoute.PortalDataKey, insertIndex, 0);
            RemapLegacyMetadata(route, splineIndex, segmentIndex, curveT);

            PrefabUtility.RecordPrefabInstancePropertyModifications(container);
            PrefabUtility.RecordPrefabInstancePropertyModifications(route);
            EditorUtility.SetDirty(container);
            EditorUtility.SetDirty(route);

            if (selectInsertedKnot)
            {
                Selection.activeGameObject = route.gameObject;
                SplineSelection.Set(new SelectableKnot(new SplineInfo(container, splineIndex), insertIndex));
            }

            SceneView.RepaintAll();
            Vector3 worldPosition = container.transform.TransformPoint((Vector3)inserted.Position);
            return new CaveRouteKnotInsertionResult(splineIndex, segmentIndex, insertIndex, curveT, worldPosition);
        }

        private static float EvaluateFloat(Spline spline, string key, float knotT, float fallback)
        {
            if (!spline.TryGetFloatData(key, out SplineData<float> data) || data.Count == 0)
                return fallback;
            return data.Evaluate(spline, knotT, PathIndexUnit.Knot,
                new UnityEngine.Splines.Interpolators.SmoothStepFloat());
        }

        private static float EvaluateAngle(Spline spline, string key, int segmentIndex, float curveT)
        {
            if (!spline.TryGetFloatData(key, out SplineData<float> data) || data.Count == 0)
                return 0f;
            float left = GetValueAtOrBefore(data, segmentIndex, 0f);
            float right = GetValueAtOrBefore(data, segmentIndex + 1, left);
            return Mathf.LerpAngle(left, right, curveT);
        }

        private static int GetValueAtOrBefore(Spline spline, string key, int knotIndex, int fallback)
        {
            return spline.TryGetIntData(key, out SplineData<int> data)
                ? GetValueAtOrBefore(data, knotIndex, fallback)
                : fallback;
        }

        private static T GetValueAtOrBefore<T>(SplineData<T> data, int knotIndex, T fallback)
        {
            T value = fallback;
            for (int i = 0; i < data.Count; i++)
            {
                if (data[i].Index > knotIndex + 0.0001f)
                    break;
                value = data[i].Value;
            }
            return value;
        }

        private static void AddFloatDataPoint(Spline spline, string key, int knotIndex, float value)
        {
            if (spline.TryGetFloatData(key, out SplineData<float> data))
                data.Add(knotIndex, value);
        }

        private static void AddIntDataPoint(Spline spline, string key, int knotIndex, int value)
        {
            if (spline.TryGetIntData(key, out SplineData<int> data))
                data.Add(knotIndex, value);
        }

        private static void RemapLegacyMetadata(CaveRoute route, int splineIndex, int segmentIndex, float curveT)
        {
            foreach (CaveRouteSplineDefinition definition in route.Definitions)
            {
                if (definition.splineIndex != splineIndex)
                    continue;

                foreach (CaveRouteSection section in definition.sections)
                {
                    if (section.startDistanceMeters < 0f && section.startKnot > segmentIndex)
                        section.startKnot++;
                    if (section.endDistanceMeters < 0f && section.endKnot > segmentIndex)
                        section.endKnot++;
                }
            }

            if (splineIndex != 0)
                return;

            foreach (CavePortalDefinition portal in route.Portals)
            {
                if (portal.mainDistanceMeters < 0f)
                    portal.mainKnot = RemapKnotIndex(portal.mainKnot, segmentIndex, curveT);
            }
        }

        private static float RemapKnotIndex(float oldIndex, int segmentIndex, float curveT)
        {
            if (oldIndex <= segmentIndex)
                return oldIndex;
            if (oldIndex >= segmentIndex + 1f)
                return oldIndex + 1f;

            float fraction = oldIndex - segmentIndex;
            if (fraction <= curveT)
                return segmentIndex + fraction / Mathf.Max(curveT, 0.0001f);
            return segmentIndex + 1f + (fraction - curveT) / Mathf.Max(1f - curveT, 0.0001f);
        }
    }
}
