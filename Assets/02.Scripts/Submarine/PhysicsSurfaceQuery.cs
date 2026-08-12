using UnityEngine;

/// <summary>
/// Collider 종류별 안전한 표면 조회 방식 선택
/// Unity ClosestPoint는 primitive와 convex MeshCollider에서만 호출
/// </summary>
internal static class PhysicsSurfaceQuery
{
    private const float MinimumDirectionSqrMagnitude = 0.000001f;
    private const float RaycastPadding = 0.05f;

    /// <summary>
    /// ClosestPoint 지원 Collider 여부 반환
    /// </summary>
    internal static bool SupportsClosestPoint(Collider collider)
    {
        return collider is BoxCollider ||
               collider is SphereCollider ||
               collider is CapsuleCollider ||
               collider is MeshCollider meshCollider && meshCollider.convex;
    }

    /// <summary>
    /// queryPoint 기준 최근접 Collider 지점 조회
    /// 비볼록 MeshCollider는 중심 방향 Collider Raycast 성공 시에만 지점 반환
    /// </summary>
    internal static bool TryClosestPoint(Vector3 queryPoint, Collider collider, out Vector3 point)
    {
        point = default;

        if (!IsUsable(collider))
            return false;

        if (SupportsClosestPoint(collider))
        {
            point = collider.ClosestPoint(queryPoint);
            return IsFinite(point);
        }

        if (!TryRaycastTowards(collider, queryPoint, collider.bounds.center, out RaycastHit hit))
            return false;

        point = hit.point;
        return IsFinite(point);
    }

    /// <summary>
    /// sourcePosition에서 aimPosition 방향의 특정 Collider Raycast
    /// LayerMask와 전역 Trigger 설정 영향 제외
    /// </summary>
    internal static bool TryRaycastTowards(
        Collider collider,
        Vector3 sourcePosition,
        Vector3 aimPosition,
        out RaycastHit hit)
    {
        hit = default;

        if (!IsUsable(collider))
            return false;

        Vector3 direction = aimPosition - sourcePosition;
        float distance = direction.magnitude;
        float maxDistance = distance + collider.bounds.extents.magnitude + RaycastPadding;

        return TryRaycast(collider, new Ray(sourcePosition, direction), maxDistance, out hit);
    }

    /// <summary>
    /// 지정 Ray 기반 특정 Collider 조회
    /// </summary>
    internal static bool TryRaycast(Collider collider, Ray ray, float maxDistance, out RaycastHit hit)
    {
        hit = default;

        if (!IsUsable(collider) ||
            !IsFinite(ray.origin) ||
            !IsFinite(ray.direction) ||
            ray.direction.sqrMagnitude <= MinimumDirectionSqrMagnitude ||
            float.IsNaN(maxDistance) ||
            float.IsInfinity(maxDistance) ||
            maxDistance <= 0f)
        {
            return false;
        }

        Ray normalizedRay = new Ray(ray.origin, ray.direction.normalized);
        return collider.Raycast(normalizedRay, out hit, maxDistance) &&
               IsFinite(hit.point) &&
               IsFinite(hit.normal);
    }

    /// <summary>
    /// 피해 위치 Fallback용 primitive와 convex Collider 외부 표면 조회
    /// sourcePosition이 Trigger 내부일 때 ClosestPoint 입력 좌표 반환 보정
    /// targetCenter 반대편 외부 지점 재조회 기반 경계 좌표 확보
    /// </summary>
    internal static bool TryDirectionalClosestPoint(
        Collider collider,
        Vector3 sourcePosition,
        Vector3 targetCenter,
        out Vector3 point,
        out Vector3 normal)
    {
        point = default;
        normal = default;

        if (!IsUsable(collider) || !SupportsClosestPoint(collider))
            return false;

        point = collider.ClosestPoint(sourcePosition);
        Vector3 surfaceToSource = sourcePosition - point;

        if (surfaceToSource.sqrMagnitude <= MinimumDirectionSqrMagnitude)
        {
            Vector3 outward = sourcePosition - targetCenter;
            if (outward.sqrMagnitude <= MinimumDirectionSqrMagnitude)
                outward = sourcePosition - collider.bounds.center;
            if (outward.sqrMagnitude <= MinimumDirectionSqrMagnitude)
                outward = Vector3.up;

            float outsideDistance =
                collider.bounds.extents.magnitude +
                Vector3.Distance(sourcePosition, collider.bounds.center) +
                RaycastPadding;
            Vector3 outsidePoint = collider.bounds.center + outward.normalized * outsideDistance;

            point = collider.ClosestPoint(outsidePoint);
            surfaceToSource = outsidePoint - point;
            if (surfaceToSource.sqrMagnitude <= MinimumDirectionSqrMagnitude)
                surfaceToSource = outward;
        }

        if (!IsFinite(point) || !IsFinite(surfaceToSource))
            return false;

        normal = surfaceToSource.normalized;
        return true;
    }

    private static bool IsUsable(Collider collider)
    {
        return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
