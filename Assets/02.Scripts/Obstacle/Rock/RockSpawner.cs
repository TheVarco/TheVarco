using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
// 경고등과 낙석 생성 및 전용 오브젝트 풀을 관리하는 생성기
public sealed class RockSpawner : MonoBehaviour, IPatternTarget
{
    [Header("Rock")]
    [SerializeField] private FallingRock fallingRockPrefab; // 풀에서 반복 사용할 낙석 프리팹
    [SerializeField] private Transform spawnPoint; // 바위 위치와 회전의 생성 기준점
    [SerializeField, Min(0f)] private float impactDamage = 10f; // 낙하 속도와 관계없이 적용할 고정 피해량
    [SerializeField, Min(0.1f)] private float maxLifetime = 10f; // 충돌하지 않은 바위의 최대 활성시간

    [Header("Warning")]
    [SerializeField] private Light warningLight; // 낙하지점을 비추는 예고용 Light

    [Header("Impact Effect")]
    [SerializeField] private ParticleSystem impactDustPrefab; // 유효 충돌 지점에 생성할 일회성 먼지 파티클

    [Header("Pool")]
    [SerializeField, Min(1)] private int prewarmCount = 1; // Awake에서 미리 만들어 둘 바위 개수

    [Header("Automatic Alone Pattern")]
    [SerializeField, Min(0f)] private float aloneStartDelay; // 자동 Alone 시작 전 대기시간
    [SerializeField, Min(0f)] private float aloneWarningDuration = 1f; // 자동 Alone 경고등 예고시간
    [SerializeField, Min(0f)] private float aloneRecoveryDuration = 1.5f; // 바위 낙하 후 다음 예고 전 휴식시간

    private readonly List<FallingRock> pooledRocks = new List<FallingRock>(); // 이 Spawner가 생성한 전체 바위 목록
    private readonly HashSet<FallingRock> activeRocks = new HashSet<FallingRock>(); // 현재 낙하 중인 바위의 빠른 검색 집합
    private ObstaclePatternBase patternOwner; // 현재 낙석 생성기를 제어하는 외부 패턴
    private Transform poolRoot; // 대기 중인 바위를 정리할 런타임 부모
    private Coroutine aloneRoutine; // 패턴 미등록 상태의 자동 Alone 코루틴
    private bool hasStarted; // Start 호출 완료 여부
    private bool missingPrefabWarningLogged; // 누락 프리팹 경고의 반복 출력 방지값

    public bool IsPatternControlled => patternOwner != null; // 외부 패턴 제어 여부
    public int PoolCount => CountExistingPooledRocks(); // 파괴되지 않고 풀에 남은 전체 바위 개수
    public int ActiveRockCount => CountExistingActiveRocks(); // 현재 낙하 중인 바위 개수

    Object IPatternTarget.PatternTargetObject => this; // 공용 패턴의 Unity 생명주기 검사 대상

    // 공용 패턴에서 낙석 생성기 제어권을 요청
    bool IPatternTarget.ClaimPatternControl(ObstaclePatternBase owner)
    {
        return ClaimPatternControl(owner);
    }

    // 공용 패턴에서 낙석 생성기 제어권을 반환
    void IPatternTarget.ReleasePatternControl(ObstaclePatternBase owner)
    {
        ReleasePatternControl(owner);
    }

    // 공용 Warning 명령으로 낙하지점 경고등 활성화
    void IPatternTarget.EnterPatternWarning()
    {
        SetWarningVisible(true);
    }

    // 공용 Active 명령으로 경고등을 끄고 바위 한 개 낙하
    void IPatternTarget.EnterPatternActive()
    {
        SetWarningVisible(false);
        SpawnRock();
    }

    // 공용 Inactive 명령으로 다음 차례 전 경고등 정지
    void IPatternTarget.EnterPatternInactive()
    {
        SetWarningVisible(false);
    }

    // 공용 Reset 명령으로 경고등과 낙하 중인 모든 바위 초기화
    void IPatternTarget.ResetPatternTarget()
    {
        ResetRockSpawner();
    }

    // 컴포넌트 추가 시 생성 기준점과 자식 Light 자동 탐색
    private void Reset()
    {
        spawnPoint = transform;
        warningLight = GetComponentInChildren<Light>(true);
    }

    // 실행 참조를 준비하고 설정 수만큼 낙석 미리 생성
    private void Awake()
    {
        if (spawnPoint == null)
            spawnPoint = transform;

        EnsurePoolRoot();
        SetWarningVisible(false);
        PrewarmPool();
    }

    // 모든 Awake 이후 외부 패턴 미등록 여부를 확인해 자동 Alone 시작
    private void Start()
    {
        hasStarted = true;
        TryStartAlonePattern();
    }

    // 재활성화된 미등록 생성기의 자동 Alone 재시작
    private void OnEnable()
    {
        if (hasStarted)
            TryStartAlonePattern();
    }

    // 외부 패턴에 제어권을 넘기고 자체 Alone과 기존 낙석 정리
    internal bool ClaimPatternControl(ObstaclePatternBase owner)
    {
        if (owner == null)
            return false;

        // 다른 패턴이 먼저 확보한 생성기의 중복 제어 차단
        if (patternOwner != null && patternOwner != owner)
        {
            Debug.LogWarning($"{name} is already controlled by {patternOwner.name}", this);
            return false;
        }

        patternOwner = owner;
        StopAlonePattern();
        ResetRockSpawner();
        return true;
    }

    // 외부 패턴 제어 해제 후 생성기를 초기화하고 자동 Alone 복구
    internal void ReleasePatternControl(ObstaclePatternBase owner)
    {
        if (patternOwner != owner)
            return;

        patternOwner = null;
        ResetRockSpawner();
        TryStartAlonePattern();
    }

    // 실행 가능 조건을 만족할 때만 자동 Alone 코루틴 생성
    private void TryStartAlonePattern()
    {
        if (!hasStarted || !isActiveAndEnabled || patternOwner != null || aloneRoutine != null)
            return;

        aloneRoutine = StartCoroutine(RunAlonePattern());
    }

    // 실행 중인 자동 Alone 코루틴 안전 종료
    private void StopAlonePattern()
    {
        if (aloneRoutine == null)
            return;

        StopCoroutine(aloneRoutine);
        aloneRoutine = null;
    }

    // 시작 지연 후 경고등과 한 번의 낙하 및 휴식을 반복
    private IEnumerator RunAlonePattern()
    {
        ResetRockSpawner();
        yield return WaitForDuration(aloneStartDelay);

        while (patternOwner == null && isActiveAndEnabled)
        {
            SetWarningVisible(true);
            yield return WaitForDuration(aloneWarningDuration);
            if (patternOwner != null || !isActiveAndEnabled)
                yield break;

            SetWarningVisible(false);
            SpawnRock();
            yield return WaitForDuration(aloneRecoveryDuration);
        }

        aloneRoutine = null;
    }

    // 양수 시간만 대기해 0초 설정에서 불필요한 추가 프레임 지연 방지
    private static IEnumerator WaitForDuration(float duration)
    {
        if (duration > 0f)
            yield return new WaitForSeconds(duration);
    }

    // 풀에서 바위 하나를 대여해 생성 기준점에서 낙하 시작
    public bool SpawnRock()
    {
        FallingRock rock = GetAvailableRock(); // 비활성 바위를 우선 재사용하고 부족할 때만 새로 생성
        if (rock == null)
            return false;

        Transform point = spawnPoint != null ? spawnPoint : transform; // 참조 유실 시 Spawner 위치를 안전 기준으로 사용
        activeRocks.Add(rock);
        rock.transform.SetParent(null, true);
        rock.Launch(point.position, point.rotation, impactDamage, maxLifetime);
        return true;
    }

    // 낙석 충돌이나 수명 종료 시 바위를 비활성 풀 상태로 반환
    internal void ReturnRock(FallingRock rock)
    {
        if (rock == null)
            return;

        activeRocks.Remove(rock);
        EnsurePoolRoot();
        rock.PrepareForPool(poolRoot);
    }

    // 충돌 지점의 표면 방향에 맞춰 일회성 먼지 파티클 생성
    internal void PlayImpactDust(Vector3 point, Vector3 normal)
    {
        if (impactDustPrefab == null)
            return;

        Vector3 surfaceNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up; // 회전 계산에 사용할 안전한 표면 방향
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal); // 파티클 위쪽 축을 충돌 표면 바깥 방향으로 정렬
        ParticleSystem effect = Instantiate(impactDustPrefab, point, rotation);
        effect.Play(true);

        float destroyDelay = CalculateParticleLifetime(effect); // 모든 자식 파티클이 끝나는 가장 긴 시간 계산
        Destroy(effect.gameObject, destroyDelay);
    }

    // 경고용 Light의 활성 상태를 한 곳에서 변경
    public void SetWarningVisible(bool visible)
    {
        if (warningLight != null)
            warningLight.enabled = visible;
    }

    // 경고등을 끄고 현재 낙하 중인 바위를 전부 풀로 회수
    public void ResetRockSpawner()
    {
        SetWarningVisible(false);
        ReturnAllActiveRocks();
    }

    // 설정된 사전 생성 수만큼 비활성 바위를 풀에 준비
    private void PrewarmPool()
    {
        if (fallingRockPrefab == null)
            return;

        int targetCount = Mathf.Max(1, prewarmCount); // 잘못된 직렬화 값에서도 최소 한 개 보장
        while (CountExistingPooledRocks() < targetCount)
        {
            if (CreatePooledRock() == null)
                break;
        }
    }

    // 사용 가능한 비활성 바위를 찾고 없으면 풀을 한 개 확장
    private FallingRock GetAvailableRock()
    {
        for (int index = pooledRocks.Count - 1; index >= 0; index--)
        {
            FallingRock rock = pooledRocks[index]; // 현재 검사할 풀 항목
            if (rock == null)
            {
                pooledRocks.RemoveAt(index);
                continue;
            }

            if (!rock.gameObject.activeSelf && !activeRocks.Contains(rock))
                return rock;
        }

        return CreatePooledRock();
    }

    // 낙석 프리팹을 한 번 생성해 이 Spawner 전용 풀에 등록
    private FallingRock CreatePooledRock()
    {
        if (fallingRockPrefab == null)
        {
            if (!missingPrefabWarningLogged)
            {
                Debug.LogWarning($"{nameof(RockSpawner)} on {name} has no {nameof(FallingRock)} prefab", this);
                missingPrefabWarningLogged = true;
            }

            return null;
        }

        missingPrefabWarningLogged = false;
        EnsurePoolRoot();

        FallingRock rock = Instantiate(fallingRockPrefab, poolRoot);
        rock.name = $"{fallingRockPrefab.name}_Pooled_{pooledRocks.Count}";
        rock.Initialize(this);
        pooledRocks.Add(rock);
        rock.PrepareForPool(poolRoot);
        return rock;
    }

    // 활성 바위 목록 복사 후 열거 중 컬렉션 변경 없이 모두 반환
    private void ReturnAllActiveRocks()
    {
        if (activeRocks.Count == 0)
            return;

        List<FallingRock> rocksToReturn = new List<FallingRock>(activeRocks); // 반환 도중 원본 집합 변경을 피하는 복사본
        foreach (FallingRock rock in rocksToReturn)
        {
            if (rock != null)
                ReturnRock(rock);
        }

        activeRocks.Clear();
    }

    // 대기 중 바위를 모아 둘 런타임 부모가 없으면 생성
    private void EnsurePoolRoot()
    {
        if (poolRoot != null)
            return;

        GameObject rootObject = new GameObject("RockPool");
        poolRoot = rootObject.transform;
        poolRoot.SetParent(transform, false);
    }

    // 자식까지 포함한 모든 파티클의 재생 종료 예상시간 계산
    private static float CalculateParticleLifetime(ParticleSystem rootEffect)
    {
        float longestLifetime = 0.1f; // 즉시 제거로 첫 프레임이 사라지지 않게 하는 최소 유지시간
        ParticleSystem[] systems = rootEffect.GetComponentsInChildren<ParticleSystem>(true); // 함께 재생되는 전체 파티클 목록

        foreach (ParticleSystem system in systems)
        {
            ParticleSystem.MainModule main = system.main; // 현재 파티클의 재생시간 설정
            float systemLifetime = main.duration + main.startLifetime.constantMax; // 마지막 생성 입자가 사라질 때까지의 최대시간
            longestLifetime = Mathf.Max(longestLifetime, systemLifetime);
        }

        return longestLifetime;
    }

    // 파괴되지 않은 풀 항목 수 계산과 유실 참조 정리
    private int CountExistingPooledRocks()
    {
        for (int index = pooledRocks.Count - 1; index >= 0; index--)
        {
            if (pooledRocks[index] == null)
                pooledRocks.RemoveAt(index);
        }

        return pooledRocks.Count;
    }

    // 파괴되지 않고 실제 활성 상태인 낙석 수 계산
    private int CountExistingActiveRocks()
    {
        activeRocks.RemoveWhere(rock => rock == null || !rock.gameObject.activeSelf);
        return activeRocks.Count;
    }

    // 비활성화 시 코루틴과 경고등 및 활성 낙석 정리
    private void OnDisable()
    {
        StopAlonePattern();
        ResetRockSpawner();
    }

    // Inspector 입력값을 실행 가능한 범위로 제한
    private void OnValidate()
    {
        impactDamage = Mathf.Max(0f, impactDamage);
        maxLifetime = Mathf.Max(0.1f, maxLifetime);
        prewarmCount = Mathf.Max(1, prewarmCount);
        aloneStartDelay = Mathf.Max(0f, aloneStartDelay);
        aloneWarningDuration = Mathf.Max(0f, aloneWarningDuration);
        aloneRecoveryDuration = Mathf.Max(0f, aloneRecoveryDuration);
    }

    // 선택한 생성 위치와 중력 낙하 방향을 Scene 화면에 표시
    private void OnDrawGizmosSelected()
    {
        Transform point = spawnPoint != null ? spawnPoint : transform; // Gizmo를 그릴 안전한 생성 기준점
        Vector3 gravityDirection = Physics.gravity.sqrMagnitude > 0.0001f
            ? Physics.gravity.normalized
            : Vector3.down;

        Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(point.position, 0.2f);
        Gizmos.DrawLine(point.position, point.position + gravityDirection * 3f);
    }
}
