using System;
using System.Collections.Generic;
using UnityEngine;

// 잠수함 내부의 플레이어 시작 위치를 순서대로 제공
[DisallowMultipleComponent]
public sealed class SubmarinePlayerSpawnPoints : MonoBehaviour
{
    // 인스펙터에서 직접 지정할 수 있는 내부 스폰 위치 목록
    [SerializeField] private Transform[] spawnPoints = Array.Empty<Transform>();

    // 자동 탐색에 사용할 스폰 위치 이름 접두사
    private const string SpawnPointNamePrefix = "PlayerSpawnPoint";

    // 현재 사용할 수 있는 내부 스폰 위치 수
    public int Count
    {
        get
        {
            RefreshFromHierarchyIfNeeded();
            return spawnPoints.Length;
        }
    }

    // 지정된 순번의 월드 위치와 회전 반환
    public bool TryGetSpawnPose(int index, out Vector3 position, out Quaternion rotation)
    {
        RefreshFromHierarchyIfNeeded();
        if (index < 0 || index >= spawnPoints.Length || spawnPoints[index] == null)
        {
            position = default;
            rotation = default;
            return false;
        }

        Transform point = spawnPoints[index];
        position = point.position;
        rotation = point.rotation;
        return true;
    }

    // 인스펙터 목록이 없을 때 이름이 일치하는 자식 위치 자동 수집
    public void RefreshFromHierarchyIfNeeded()
    {
        if (HasValidSpawnPoints())
            return;

        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        List<Transform> discovered = new();
        foreach (Transform descendant in descendants)
        {
            if (descendant == null || descendant == transform)
                continue;
            if (!descendant.name.StartsWith(SpawnPointNamePrefix, StringComparison.Ordinal))
                continue;

            discovered.Add(descendant);
        }

        // 이름이 같거나 복제 이름인 경우에도 계층 순서를 보조 기준으로 사용
        discovered.Sort(CompareSpawnPoints);
        spawnPoints = discovered.ToArray();
    }

    // 비어 있거나 유실된 참조가 없는지 확인
    private bool HasValidSpawnPoints()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return false;

        foreach (Transform point in spawnPoints)
        {
            if (point == null)
                return false;
        }

        return true;
    }

    // 이름과 형제 순서를 이용해 항상 같은 배정 순서 유지
    private static int CompareSpawnPoints(Transform left, Transform right)
    {
        int nameComparison = string.CompareOrdinal(left.name, right.name);
        if (nameComparison != 0)
            return nameComparison;

        return left.GetSiblingIndex().CompareTo(right.GetSiblingIndex());
    }
}
