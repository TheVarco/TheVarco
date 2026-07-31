using UnityEngine;
using UnityEngine.Rendering;

// 투명 유리는 Decal Projector가 안 먹어서 얇은 Quad를 앞에 붙이는 방식으로 처리
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class GlassDamageOverlay : MonoBehaviour
{
    [Tooltip("균열 오버레이에 사용할 셰이더. 비어 있으면 URP Lit 셰이더를 찾습니다.")]
    [SerializeField] private Shader overlayShader;

    [Tooltip("유리 표면을 덮는 균열 사각 메시의 가로·세로 크기")]
    [SerializeField] private Vector2 size = Vector2.one;

    [Tooltip("유리와 겹쳐 깜빡이는 현상을 피하기 위한 로컬 Z축 오프셋")]
    [SerializeField] private float surfaceOffset = -0.01f;

    [Tooltip("다른 투명 오브젝트와 겹칠 때 균열을 위에 그리기 위한 정렬 순서")]
    [SerializeField] private int sortingOrder = 10;

    // 실행 중에만 쓸 메시랑 머티리얼
    private Mesh runtimeMesh;
    private Material runtimeMaterial;

    // 계속 GetComponent 하지 않게 저장
    private MeshFilter meshFilter;
    private MeshRenderer overlayRenderer;

    public bool IsVisible => overlayRenderer != null && overlayRenderer.enabled;
    public string CurrentMaterialName => runtimeMaterial != null ? runtimeMaterial.name : "없음";

    // 현재 단계의 유리 균열 표시
    public void Show(Texture2D albedo, Texture2D normal)
    {
        if (albedo == null || !EnsureInitialized())
        {
            Hide();
            return;
        }

        runtimeMaterial.SetTexture("_BaseMap", albedo);
        runtimeMaterial.SetColor("_BaseColor", Color.white);

        if (normal != null)
        {
            // 노멀맵 있을 때만 사용
            runtimeMaterial.SetTexture("_BumpMap", normal);
            runtimeMaterial.EnableKeyword("_NORMALMAP");
        }
        else
        {
            runtimeMaterial.SetTexture("_BumpMap", null);
            runtimeMaterial.DisableKeyword("_NORMALMAP");
        }

        runtimeMaterial.name = $"Glass Damage Overlay ({albedo.name})";
        overlayRenderer.enabled = true;
    }

    // 수리 끝나면 Renderer만 끄고 나중에 다시 사용
    public void Hide()
    {
        if (overlayRenderer != null)
            overlayRenderer.enabled = false;
    }

    private bool EnsureInitialized()
    {
        // 없으면 여기서 만들어서 MissingComponent 에러 방지
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        if (meshFilter == null)
        {
            Debug.LogError("Glass damage overlay could not create its MeshFilter.", this);
            return false;
        }

        if (overlayRenderer == null)
        {
            overlayRenderer = GetComponent<MeshRenderer>();
            if (overlayRenderer == null)
                overlayRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        if (overlayRenderer == null)
        {
            Debug.LogError("Glass damage overlay could not create its MeshRenderer.", this);
            return false;
        }

        // 균열 이미지만 보여주면 돼서 그림자랑 프로브는 끔
        overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = false;
        overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
        overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        overlayRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        overlayRenderer.allowOcclusionWhenDynamic = false;
        overlayRenderer.sortingOrder = sortingOrder;

        if (runtimeMesh == null)
        {
            // Quad는 처음 한 번만 생성
            runtimeMesh = BuildQuad();
            meshFilter.sharedMesh = runtimeMesh;
        }

        if (runtimeMaterial == null)
        {
            // 셰이더 연결 안 했으면 URP Lit 사용
            Shader shader = overlayShader != null
                ? overlayShader
                : Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
            {
                Debug.LogError("Glass damage overlay requires the URP Lit shader.", this);
                overlayRenderer.enabled = false;
                return false;
            }

            runtimeMaterial = new Material(shader)
            {
                hideFlags = HideFlags.DontSave,
                name = "Glass Damage Overlay"
            };
            ConfigureTransparentMaterial(runtimeMaterial);
            overlayRenderer.sharedMaterial = runtimeMaterial;
        }

        return true;
    }

    private Mesh BuildQuad()
    {
        // 유리 앞에 붙일 Quad 생성
        float halfWidth = size.x * 0.5f;
        float halfHeight = size.y * 0.5f;

        Mesh mesh = new Mesh
        {
            hideFlags = HideFlags.DontSave,
            name = "Glass Damage Overlay Quad",
            vertices = new[]
            {
                new Vector3(-halfWidth, -halfHeight, surfaceOffset),
                new Vector3(-halfWidth, halfHeight, surfaceOffset),
                new Vector3(halfWidth, halfHeight, surfaceOffset),
                new Vector3(halfWidth, -halfHeight, surfaceOffset)
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            },
            normals = new[]
            {
                Vector3.back,
                Vector3.back,
                Vector3.back,
                Vector3.back
            },
            tangents = new[]
            {
                new Vector4(1f, 0f, 0f, -1f),
                new Vector4(1f, 0f, 0f, -1f),
                new Vector4(1f, 0f, 0f, -1f),
                new Vector4(1f, 0f, 0f, -1f)
            },
            triangles = new[] { 0, 1, 2, 0, 2, 3 }
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        // 균열 없는 부분은 유리가 그대로 보이게 투명 설정
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_BumpScale", 1f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.45f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        // 유리보다 나중에 그려야 균열이 안 사라짐
        material.renderQueue = (int)RenderQueue.Transparent + 10;
    }

    private void OnDestroy()
    {
        // 런타임에 만든 Material과 Mesh 정리
        DestroyRuntimeObject(runtimeMaterial);
        DestroyRuntimeObject(runtimeMesh);
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
