using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>사선 해칭 패턴을 스프라이트 없이 그리는 공용 UI 그래픽.</summary>
public sealed class DiagonalStripeGraphic : MaskableGraphic
{
    [SerializeField, Min(4f)] private float spacing = 19f;
    [SerializeField, Min(1f)] private float thickness = 6f;
    [SerializeField, Range(0.1f, 1.5f)] private float horizontalShiftPerHeight = 0.72f;

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        float shift = rect.height * horizontalShiftPerHeight;
        float start = rect.xMin - shift - thickness;
        for (float x = start; x < rect.xMax + spacing; x += spacing)
        {
            List<Vector2> stripe = new List<Vector2>(4)
            {
                new Vector2(x, rect.yMin),
                new Vector2(x + thickness, rect.yMin),
                new Vector2(x + thickness + shift, rect.yMax),
                new Vector2(x + shift, rect.yMax)
            };

            stripe = ClipVertical(stripe, rect.xMin, true);
            stripe = ClipVertical(stripe, rect.xMax, false);
            if (stripe.Count < 3)
                continue;

            int firstVertex = vertexHelper.currentVertCount;
            for (int i = 0; i < stripe.Count; i++)
            {
                Vector2 point = stripe[i];
                Vector2 uv = new Vector2(
                    Mathf.InverseLerp(rect.xMin, rect.xMax, point.x),
                    Mathf.InverseLerp(rect.yMin, rect.yMax, point.y));
                vertexHelper.AddVert(point, color, uv);
            }

            for (int i = 1; i < stripe.Count - 1; i++)
                vertexHelper.AddTriangle(firstVertex, firstVertex + i, firstVertex + i + 1);
        }
    }

    private static List<Vector2> ClipVertical(List<Vector2> input, float edge, bool keepGreater)
    {
        List<Vector2> output = new List<Vector2>(input.Count + 2);
        if (input.Count == 0)
            return output;

        Vector2 previous = input[input.Count - 1];
        bool previousInside = keepGreater ? previous.x >= edge : previous.x <= edge;
        for (int i = 0; i < input.Count; i++)
        {
            Vector2 current = input[i];
            bool currentInside = keepGreater ? current.x >= edge : current.x <= edge;
            if (currentInside != previousInside)
            {
                float amount = Mathf.Approximately(current.x, previous.x)
                    ? 0f
                    : (edge - previous.x) / (current.x - previous.x);
                output.Add(Vector2.Lerp(previous, current, amount));
            }

            if (currentInside)
                output.Add(current);

            previous = current;
            previousInside = currentInside;
        }

        return output;
    }
}

/// <summary>스프라이트 없이 해상도 독립적인 둥근 UI 바를 그리는 공용 그래픽.</summary>
public sealed class RoundedRectGraphic : MaskableGraphic
{
    [SerializeField, Min(0f)] private float radius = 12f;
    [SerializeField, Range(2, 10)] private int cornerSegments = 5;

    public float Radius
    {
        get => radius;
        set
        {
            radius = Mathf.Max(0f, value);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        float resolvedRadius = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);
        int centerIndex = vertexHelper.currentVertCount;
        vertexHelper.AddVert(rect.center, color, new Vector2(0.5f, 0.5f));

        List<Vector2> perimeter = new List<Vector2>((cornerSegments + 1) * 4);
        AddCorner(perimeter, new Vector2(rect.xMax - resolvedRadius, rect.yMin + resolvedRadius), -90f, 0f, resolvedRadius);
        AddCorner(perimeter, new Vector2(rect.xMax - resolvedRadius, rect.yMax - resolvedRadius), 0f, 90f, resolvedRadius);
        AddCorner(perimeter, new Vector2(rect.xMin + resolvedRadius, rect.yMax - resolvedRadius), 90f, 180f, resolvedRadius);
        AddCorner(perimeter, new Vector2(rect.xMin + resolvedRadius, rect.yMin + resolvedRadius), 180f, 270f, resolvedRadius);

        for (int i = 0; i < perimeter.Count; i++)
        {
            Vector2 point = perimeter[i];
            Vector2 uv = new Vector2(
                Mathf.InverseLerp(rect.xMin, rect.xMax, point.x),
                Mathf.InverseLerp(rect.yMin, rect.yMax, point.y));
            vertexHelper.AddVert(point, color, uv);
        }

        for (int i = 0; i < perimeter.Count; i++)
        {
            int current = centerIndex + 1 + i;
            int next = centerIndex + 1 + ((i + 1) % perimeter.Count);
            vertexHelper.AddTriangle(centerIndex, current, next);
        }
    }

    private void AddCorner(List<Vector2> points, Vector2 center, float from, float to, float resolvedRadius)
    {
        for (int i = 0; i <= cornerSegments; i++)
        {
            float angle = Mathf.Lerp(from, to, i / (float)cornerSegments) * Mathf.Deg2Rad;
            points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * resolvedRadius);
        }
    }
}
