using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>대상이 잠수함보다 위/아래에 있는지 나타내는 화면 표시 상태</summary>
public enum SonarVerticalDirection
{
    Level,
    Above,
    Below
}

/// <summary>
/// 한 번의 핑에서 저장한 접촉점의 화면 표시 데이터
/// Transform을 보관하지 않으므로 핑 이후 대상이 움직여도 스냅샷 위치 유지
/// </summary>
public readonly struct SonarEchoVisual
{
    public SonarEchoVisual(
        Vector2 normalizedPosition,
        SonarTargetCategory category,
        SonarVerticalDirection verticalDirection,
        float revealTime,
        float expireTime)
    {
        NormalizedPosition = normalizedPosition;
        Category = category;
        VerticalDirection = verticalDirection;
        RevealTime = revealTime;
        ExpireTime = expireTime;
    }

    public Vector2 NormalizedPosition { get; }
    public SonarTargetCategory Category { get; }
    public SonarVerticalDirection VerticalDirection { get; }
    public float RevealTime { get; }
    public float ExpireTime { get; }
}

/// <summary>
/// Unity UI의 정점 메시를 직접 만들어 소나 배경, 거리 원, 파동과 접촉점을 한 Graphic에 그리기
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SubmarineSonarGraphic : MaskableGraphic
{
    private const int CircleSegments = 64;

    // 컨트롤러가 매 프레임 전달하는 현재 표시 상태
    private IReadOnlyList<SonarEchoVisual> echoes;
    private float currentTime;
    private float pulseProgress = -1f;

    private Color backgroundColor = new Color(0.004f, 0.035f, 0.025f, 0.98f);
    private Color gridColor = new Color(0.12f, 0.55f, 0.32f, 0.45f);
    private Color pulseColor = new Color(0.36f, 1f, 0.62f, 0.95f);
    private Color creatureColor = new Color(1f, 0.28f, 0.08f, 1f);
    private Color itemColor = new Color(0.35f, 1f, 0.32f, 1f);
    private Color pointOfInterestColor = new Color(0.12f, 0.95f, 0.9f, 1f);

    [Header("Readability")]
    [Tooltip("높이 화살표의 크기")]
    [SerializeField, Range(0.5f, 3f)] private float iconScale = 1.8f;
    [Tooltip("선, 파동 굵기")]
    [SerializeField, Range(0.5f, 3f)] private float lineThicknessScale = 1.5f;
    [Tooltip("삼각형 크기")]
    [SerializeField, Range(0.5f, 3f)] private float centerMarkerScale = 1.4f;

    /// <summary>컨트롤러의 종류별 색상 설정을 그래픽에 적용</summary>
    public void ConfigureColors(
        Color background,
        Color grid,
        Color pulse,
        Color creature,
        Color item,
        Color pointOfInterest)
    {
        backgroundColor = background;
        gridColor = grid;
        pulseColor = pulse;
        creatureColor = creature;
        itemColor = item;
        pointOfInterestColor = pointOfInterest;
        SetVerticesDirty();
    }

    /// <summary>현재 파동 진행률과 접촉점 목록을 받고 UI 메시 재생성 요청</summary>
    public void SetFrame(float normalizedPulseProgress, float now, IReadOnlyList<SonarEchoVisual> activeEchoes)
    {
        pulseProgress = normalizedPulseProgress;
        currentTime = now;
        echoes = activeEchoes;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        // SetVerticesDirty가 호출되면 Unity UI가 이 메서드를 실행해 화면 메시를 다시 만듦
        vertexHelper.Clear();

        Rect drawingRect = rectTransform.rect;
        AddRect(vertexHelper, drawingRect, backgroundColor);

        Vector2 center = drawingRect.center;
        float radius = Mathf.Min(drawingRect.width, drawingRect.height) * 0.44f;
        float lineThickness = Mathf.Max(1.25f, radius * 0.008f) * lineThicknessScale;

        // 고정 배경: 탐지 원, 4단계 거리 링, 기준선
        AddFilledCircle(vertexHelper, center, radius, MultiplyAlpha(backgroundColor, 0.4f), CircleSegments);
        for (int ring = 1; ring <= 4; ring++)
            AddCircleLine(vertexHelper, center, radius * ring / 4f, lineThickness, gridColor, CircleSegments);

        AddLine(vertexHelper, center + Vector2.left * radius, center + Vector2.right * radius, lineThickness, gridColor);
        AddLine(vertexHelper, center + Vector2.down * radius, center + Vector2.up * radius, lineThickness, gridColor);

        if (pulseProgress >= 0f && pulseProgress <= 1f)
        {
            // 중심에서 가장자리로 진행하며 조금씩 흐려지는 탐지 파동
            Color animatedPulse = MultiplyAlpha(pulseColor, 1f - pulseProgress * 0.45f);
            AddCircleLine(
                vertexHelper,
                center,
                radius * pulseProgress,
                lineThickness * 2.2f,
                animatedPulse,
                CircleSegments);
        }

        AddSubmarineMarker(vertexHelper, center, radius * 0.075f * centerMarkerScale, pulseColor);
        AddEchoes(vertexHelper, center, radius, lineThickness);
    }

    private void AddEchoes(VertexHelper vertexHelper, Vector2 center, float radius, float lineThickness)
    {
        if (echoes == null)
            return;

        float markerRadius = Mathf.Max(3.5f, radius * 0.035f) * iconScale;
        for (int i = 0; i < echoes.Count; i++)
        {
            SonarEchoVisual echo = echoes[i];
            float alpha = SubmarineSonarController.EvaluateEchoAlpha(
                currentTime,
                echo.RevealTime,
                echo.ExpireTime);
            if (alpha <= 0f || echo.NormalizedPosition.sqrMagnitude > 1.0001f)
                continue;

            // 정규화된 소나 좌표를 현재 RectTransform의 실제 픽셀 좌표로 바꿈
            Vector2 position = center + echo.NormalizedPosition * radius;
            Color contactColor = MultiplyAlpha(GetCategoryColor(echo.Category), alpha);
            // 작은 모니터에서도 식별되도록 희미한 큰 마름모 뒤에 선명한 접촉점을 겹침
            AddDiamond(
                vertexHelper,
                position,
                markerRadius * 1.55f,
                MultiplyAlpha(contactColor, 0.2f));
            AddDiamond(vertexHelper, position, markerRadius, contactColor);

            if (echo.VerticalDirection == SonarVerticalDirection.Level)
                continue;

            // 위쪽 대상은 ▲, 아래쪽 대상은 ▼ 모양의 화살표로 표시
            float direction = echo.VerticalDirection == SonarVerticalDirection.Above ? 1f : -1f;
            Vector2 arrowCenter = position + Vector2.up * direction * markerRadius * 2.1f;
            AddChevron(vertexHelper, arrowCenter, markerRadius * 0.8f, direction, lineThickness * 1.5f, contactColor);
        }
    }

    private Color GetCategoryColor(SonarTargetCategory category)
    {
        switch (category)
        {
            case SonarTargetCategory.Creature:
                return creatureColor;
            case SonarTargetCategory.Item:
                return itemColor;
            default:
                return pointOfInterestColor;
        }
    }

    private static void AddSubmarineMarker(VertexHelper vertexHelper, Vector2 center, float size, Color color)
    {
        AddTriangle(
            vertexHelper,
            center + Vector2.up * size,
            center + new Vector2(-size * 0.72f, -size),
            center + new Vector2(size * 0.72f, -size),
            color);
    }

    private static void AddChevron(
        VertexHelper vertexHelper,
        Vector2 center,
        float size,
        float direction,
        float thickness,
        Color color)
    {
        Vector2 tip = center + Vector2.up * direction * size * 0.55f;
        Vector2 left = center + new Vector2(-size, -direction * size * 0.45f);
        Vector2 right = center + new Vector2(size, -direction * size * 0.45f);
        AddLine(vertexHelper, left, tip, thickness, color);
        AddLine(vertexHelper, tip, right, thickness, color);
    }

    private static void AddDiamond(VertexHelper vertexHelper, Vector2 center, float radius, Color color)
    {
        int start = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, center + Vector2.up * radius, color);
        AddVertex(vertexHelper, center + Vector2.right * radius, color);
        AddVertex(vertexHelper, center + Vector2.down * radius, color);
        AddVertex(vertexHelper, center + Vector2.left * radius, color);
        vertexHelper.AddTriangle(start, start + 1, start + 2);
        vertexHelper.AddTriangle(start, start + 2, start + 3);
    }

    private static void AddFilledCircle(
        VertexHelper vertexHelper,
        Vector2 center,
        float radius,
        Color color,
        int segments)
    {
        int centerIndex = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, center, color);
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            AddVertex(vertexHelper, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color);
            if (i > 0)
                vertexHelper.AddTriangle(centerIndex, centerIndex + i, centerIndex + i + 1);
        }
    }

    private static void AddCircleLine(
        VertexHelper vertexHelper,
        Vector2 center,
        float radius,
        float thickness,
        Color color,
        int segments)
    {
        if (radius <= 0f)
            return;

        Vector2 previous = center + Vector2.right * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            AddLine(vertexHelper, previous, next, thickness, color);
            previous = next;
        }
    }

    private static void AddLine(
        VertexHelper vertexHelper,
        Vector2 start,
        Vector2 end,
        float thickness,
        Color color)
    {
        Vector2 direction = end - start;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
            return;

        Vector2 perpendicular = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);
        int firstVertex = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, start - perpendicular, color);
        AddVertex(vertexHelper, start + perpendicular, color);
        AddVertex(vertexHelper, end + perpendicular, color);
        AddVertex(vertexHelper, end - perpendicular, color);
        vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
        vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
    }

    private static void AddRect(VertexHelper vertexHelper, Rect rect, Color color)
    {
        int firstVertex = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, new Vector2(rect.xMin, rect.yMin), color);
        AddVertex(vertexHelper, new Vector2(rect.xMin, rect.yMax), color);
        AddVertex(vertexHelper, new Vector2(rect.xMax, rect.yMax), color);
        AddVertex(vertexHelper, new Vector2(rect.xMax, rect.yMin), color);
        vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
        vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
    }

    private static void AddTriangle(
        VertexHelper vertexHelper,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Color color)
    {
        int firstVertex = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, a, color);
        AddVertex(vertexHelper, b, color);
        AddVertex(vertexHelper, c, color);
        vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
    }

    private static void AddVertex(VertexHelper vertexHelper, Vector2 position, Color color)
    {
        // 모든 도형 헬퍼가 공통으로 사용하는 최소 UI 정점 생성 함수
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vertex.uv0 = Vector2.zero;
        vertexHelper.AddVert(vertex);
    }

    private static Color MultiplyAlpha(Color color, float multiplier)
    {
        // 원본 RGB는 유지하고 잔상/파동의 투명도만 안전하게 0~1 범위에서 조절
        color.a *= Mathf.Clamp01(multiplier);
        return color;
    }
}
