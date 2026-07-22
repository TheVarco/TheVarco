using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace CaveBlockout.Editor
{
    [CustomEditor(typeof(CaveRoute))]
    public sealed class CaveRouteEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            CaveRoute route = (CaveRoute)target;
            SplineContainer container = route.Container;
            if (container == null)
                return;

            for (int splineIndex = 0; splineIndex < container.Splines.Count; splineIndex++)
                DrawSplineHandles(route, splineIndex);
        }

        private static void DrawSplineHandles(CaveRoute route, int splineIndex)
        {
            Spline spline = route.Container[splineIndex];
            if (!spline.TryGetFloatData(CaveRoute.WidthDataKey, out SplineData<float> widthData) ||
                !spline.TryGetFloatData(CaveRoute.HeightDataKey, out SplineData<float> heightData) ||
                !spline.TryGetFloatData(CaveRoute.RollDataKey, out SplineData<float> rollData))
                return;

            for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
            {
                float t = spline.ConvertIndexUnit(knotIndex, PathIndexUnit.Knot, PathIndexUnit.Normalized);
                Vector3 center = route.Container.EvaluatePosition(splineIndex, t);
                Vector3 tangent = ((Vector3)route.Container.EvaluateTangent(splineIndex, t)).normalized;
                Vector3 up = ((Vector3)route.Container.EvaluateUpVector(splineIndex, t)).normalized;
                Vector3 right = Vector3.Cross(up, tangent).normalized;
                up = Vector3.Cross(tangent, right).normalized;
                float width = route.EvaluateWidth(splineIndex, t);
                float height = route.EvaluateHeight(splineIndex, t);
                float roll = route.EvaluateRoll(splineIndex, t);
                float handleSize = HandleUtility.GetHandleSize(center) * 0.08f;

                DrawEllipse(center, right, up, width * 0.5f, height * 0.5f);
                Handles.Label(center + up * (height * 0.5f + handleSize * 2f), $"K{knotIndex}  {width:F1} x {height:F1}m  roll {roll:F0}°");

                EditorGUI.BeginChangeCheck();
                float widthRadius = Handles.ScaleSlider(width * 0.5f, center, right, Quaternion.identity, handleSize, 0.1f);
                if (EditorGUI.EndChangeCheck())
                    SetDataValue(route, widthData, knotIndex, Mathf.Max(5f, widthRadius) * 2f, "Change cave width");

                EditorGUI.BeginChangeCheck();
                float heightRadius = Handles.ScaleSlider(height * 0.5f, center, up, Quaternion.identity, handleSize, 0.1f);
                if (EditorGUI.EndChangeCheck())
                    SetDataValue(route, heightData, knotIndex, Mathf.Max(4f, heightRadius) * 2f, "Change cave height");

                EditorGUI.BeginChangeCheck();
                float newRoll = Handles.ScaleValueHandle(roll, center - up * (height * 0.5f + handleSize * 2f), Quaternion.LookRotation(tangent, up), handleSize * 1.4f, Handles.ConeHandleCap, 1f);
                if (EditorGUI.EndChangeCheck())
                    SetDataValue(route, rollData, knotIndex, Mathf.Repeat(newRoll + 180f, 360f) - 180f, "Change cave roll");
            }
        }

        private static void DrawEllipse(Vector3 center, Vector3 right, Vector3 up, float widthRadius, float heightRadius)
        {
            const int segments = 32;
            Vector3[] points = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                points[i] = center + right * Mathf.Cos(angle) * widthRadius + up * Mathf.Sin(angle) * heightRadius;
            }
            Handles.color = new Color(0.1f, 0.8f, 1f, 0.8f);
            Handles.DrawAAPolyLine(2f, points);
        }

        private static void SetDataValue(CaveRoute route, SplineData<float> data, int knotIndex, float value, string undoName)
        {
            Undo.RecordObject(route.Container, undoName);
            for (int i = 0; i < data.Count; i++)
            {
                DataPoint<float> point = data[i];
                if (!Mathf.Approximately(point.Index, knotIndex))
                    continue;
                data[i] = new DataPoint<float>(knotIndex, value);
                EditorUtility.SetDirty(route.Container);
                SceneView.RepaintAll();
                return;
            }
            data.Add(knotIndex, value);
            EditorUtility.SetDirty(route.Container);
        }
    }
}
