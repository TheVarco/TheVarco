using System;
using System.Collections.Generic;
using CaveBlockout.Decor;
using UnityEditor;
using UnityEngine;

namespace CaveBlockout.Editor.Decor
{
    /// <summary>
    /// Scene-view brush for cave dressing, plus the buttons that drive the rest of the decor pipeline.
    ///
    /// Unity's own terrain detail and tree brushes cannot be used here: they are bound to a heightmap,
    /// and this cave is a procedurally generated tube with walls and a ceiling. Nothing in the project
    /// paints scene objects either - there is no ProBuilder, Polybrush or scattering package installed,
    /// and no SceneView.duringSceneGui handler anywhere - so this is the first.
    ///
    /// Strokes are recorded route-relative rather than as world positions, so the art survives a cave
    /// blockout regeneration.
    /// </summary>
    public sealed class CaveDecorBrushWindow : EditorWindow
    {
        private enum BrushMode
        {
            Paint,
            Erase,
            Single
        }

        private CaveDecorSet decorSet;
        private CaveDecorContext context;

        private bool brushActive;
        private BrushMode mode = BrushMode.Paint;
        private float brushRadius = 12f;
        private int propsPerStroke = 3;
        private CaveSurfaceKind surfaceFilter = CaveSurfaceKind.Any;
        private readonly HashSet<string> disabledEntries = new HashSet<string>();

        private Vector2 scroll;
        private bool paletteFoldout = true;
        private string status = "";

        private bool hasCursor;
        private Vector3 cursorPoint;
        private Vector3 cursorNormal;
        private string cursorZone = "-";
        private float cursorRouteDistance;
        private Vector3 lastPaintPoint;
        private bool hasPainted;

        /// <summary>
        /// Cached world positions of every placement, so a stroke can enforce spacing without
        /// re-raycasting the whole set on each candidate.
        /// </summary>
        private readonly List<(Vector3 position, string paletteId, float radius)> occupancy =
            new List<(Vector3, string, float)>();

        private System.Random random = new System.Random();

        [MenuItem("Tools/Underwater Cave/Decor/3 - 데코 브러쉬")]
        public static void Open()
        {
            GetWindow<CaveDecorBrushWindow>("Cave Decor").minSize = new Vector2(320f, 460f);
        }

        private void OnEnable()
        {
            if (decorSet == null)
                decorSet = AssetDatabase.LoadAssetAtPath<CaveDecorSet>(CaveDecorCatalog.DecorSetPath);
            SceneView.duringSceneGui += OnSceneGUI;
            RefreshContext();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        // ── window ────────────────────────────────────────────────────────

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUI.BeginChangeCheck();
            decorSet = (CaveDecorSet)EditorGUILayout.ObjectField("데코 셋", decorSet, typeof(CaveDecorSet), false);
            if (EditorGUI.EndChangeCheck())
                RefreshOccupancy();

            if (decorSet == null)
            {
                EditorGUILayout.HelpBox(
                    "데코 셋이 없다. 먼저 Tools > Underwater Cave > Decor > 1 - 에셋 준비를 실행해라.",
                    MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawSceneStatus();
            EditorGUILayout.Space();
            DrawBrushSettings();
            EditorGUILayout.Space();
            DrawPalette();
            EditorGUILayout.Space();
            DrawActions();

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(status, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSceneStatus()
        {
            EditorGUILayout.LabelField("씬 상태", EditorStyles.boldLabel);

            bool ready = context != null && context.IsValid;
            EditorGUILayout.HelpBox(
                ready
                    ? $"루트 {CountRoutes()}개, CaveShell 콜라이더 확인됨. 배치 레코드 {decorSet.Placements.Count}개."
                    : "CaveRoute 또는 CaveShell MeshCollider를 찾지 못했다. MainMap.unity를 열고 새로고침해라.",
                ready ? MessageType.Info : MessageType.Error);

            if (GUILayout.Button("씬 다시 읽기"))
                RefreshContext();
        }

        private void DrawBrushSettings()
        {
            EditorGUILayout.LabelField("브러쉬", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            brushActive = EditorGUILayout.ToggleLeft("씬 브러쉬 활성 (Scene 뷰에서 드래그)", brushActive);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            using (new EditorGUI.DisabledScope(!brushActive))
            {
                mode = (BrushMode)EditorGUILayout.EnumPopup("모드", mode);
                brushRadius = EditorGUILayout.Slider("반경 (m)", brushRadius, 1f, 60f);
                propsPerStroke = EditorGUILayout.IntSlider("스트로크당 개수", propsPerStroke, 1, 20);
                surfaceFilter = (CaveSurfaceKind)EditorGUILayout.EnumFlagsField("표면 필터", surfaceFilter);
            }

            if (brushActive)
            {
                EditorGUILayout.LabelField(hasCursor
                    ? $"커서: {cursorZone} / {cursorRouteDistance:0.0} m"
                    : "커서: 동굴 표면 밖");
            }
        }

        private void DrawPalette()
        {
            paletteFoldout = EditorGUILayout.Foldout(paletteFoldout, $"팔레트 ({decorSet.Palette.Count})", true);
            if (!paletteFoldout)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (CaveDecorPaletteEntry entry in decorSet.Palette)
                {
                    if (entry == null)
                        continue;

                    EditorGUILayout.BeginHorizontal();
                    bool enabled = !disabledEntries.Contains(entry.id);
                    bool nowEnabled = EditorGUILayout.Toggle(enabled, GUILayout.Width(18f));
                    if (nowEnabled != enabled)
                    {
                        if (nowEnabled)
                            disabledEntries.Remove(entry.id);
                        else
                            disabledEntries.Add(entry.id);
                    }

                    EditorGUILayout.LabelField(entry.id, GUILayout.MinWidth(120f));
                    EditorGUILayout.LabelField(
                        $"{entry.scaleRange.x:0.#}-{entry.scaleRange.y:0.#} m",
                        GUILayout.Width(70f));
                    EditorGUILayout.LabelField(DescribeSurfaces(entry.allowedSurfaces), GUILayout.Width(64f));
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("전체 켜기"))
                    disabledEntries.Clear();
                if (GUILayout.Button("전체 끄기"))
                {
                    foreach (CaveDecorPaletteEntry entry in decorSet.Palette)
                    {
                        if (entry != null)
                            disabledEntries.Add(entry.id);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField("작업", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            decorSet.autoScatterDensity = EditorGUILayout.Slider("자동 산포 밀도", decorSet.autoScatterDensity, 0.1f, 8f);
            decorSet.minCorridorRadius = EditorGUILayout.Slider("통로 여유 반경 (m)", decorSet.minCorridorRadius, 0f, 20f);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(decorSet);

            if (GUILayout.Button("기존 인스턴스 흡수"))
                RunAdopt();

            if (GUILayout.Button("1차 자동 산포 (기존 레코드 유지)"))
                RunAutoScatter(false);

            if (GUILayout.Button("1차 자동 산포 (전부 다시)"))
            {
                if (EditorUtility.DisplayDialog("자동 산포",
                        $"배치 레코드 {decorSet.Placements.Count}개를 전부 버리고 다시 만든다. 계속할까?",
                        "다시 만들기", "취소"))
                    RunAutoScatter(true);
            }

            if (GUILayout.Button("데이터에서 재생성"))
                RunRebuild();

            if (GUILayout.Button("씬 인스턴스만 지우기"))
            {
                Undo.SetCurrentGroupName("Clear cave decor instances");
                int removed = CaveDecorSpawner.ClearInstances();
                CaveDecorSpawner.MarkSceneDirty();
                SetStatus($"씬 인스턴스 {removed}개 제거. 레코드는 그대로다 - 재생성으로 되돌릴 수 있다.");
            }

            GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
            if (GUILayout.Button("레코드 전체 삭제"))
            {
                if (EditorUtility.DisplayDialog("레코드 삭제",
                        $"배치 레코드 {decorSet.Placements.Count}개를 전부 삭제한다. 되돌릴 수 없다.",
                        "삭제", "취소"))
                {
                    CaveDecorSpawner.ClearInstances();
                    decorSet.Placements.Clear();
                    EditorUtility.SetDirty(decorSet);
                    RefreshOccupancy();
                    CaveDecorSpawner.MarkSceneDirty();
                    SetStatus("레코드와 인스턴스를 모두 삭제했다.");
                }
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("검증"))
                RunValidate();
        }

        // ── actions ───────────────────────────────────────────────────────

        private void RunAdopt()
        {
            RefreshContext();
            Undo.SetCurrentGroupName("Adopt legacy cave decor");
            CaveDecorAdoption.Result result = CaveDecorAdoption.Adopt(decorSet, context, true);
            CaveDecorSpawner.Rebuild(decorSet, context);
            RefreshOccupancy();
            SetStatus("흡수: " + CaveDecorAdoption.Describe(result));
        }

        private void RunAutoScatter(bool replace)
        {
            RefreshContext();
            CaveDecorAutoScatter.Result result = CaveDecorAutoScatter.Scatter(decorSet, context, replace);
            CaveDecorSpawner.Result spawn = CaveDecorSpawner.Rebuild(decorSet, context);
            RefreshOccupancy();
            SetStatus("자동 산포: " + CaveDecorAutoScatter.Describe(result) +
                      "\n재생성: " + CaveDecorSpawner.Describe(spawn));
        }

        private void RunRebuild()
        {
            RefreshContext();
            Undo.SetCurrentGroupName("Rebuild cave decor");
            CaveDecorSpawner.Result result = CaveDecorSpawner.Rebuild(decorSet, context);
            RefreshOccupancy();
            SetStatus("재생성: " + CaveDecorSpawner.Describe(result));
        }

        private void RunValidate()
        {
            RefreshContext();
            CaveDecorValidator.Report report = CaveDecorValidator.Validate(decorSet, context);
            string path = CaveDecorValidator.Write(report, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            SetStatus((report.Passed ? "검증 PASS" : "검증 FAIL") +
                      $"\n배치 {report.placements} / 안착 {report.resolved} / 통로위반 {report.corridorViolations}" +
                      $"\n{path}");
            Debug.Log(CaveDecorValidator.Format(report));
        }

        private void RefreshContext()
        {
            context = CaveDecorContext.Create();
            RefreshOccupancy();
            Repaint();
        }

        private void RefreshOccupancy()
        {
            occupancy.Clear();
            if (decorSet == null || context == null || !context.IsValid)
                return;

            foreach (CaveDecorPlacement placement in decorSet.Placements)
            {
                CaveDecorPaletteEntry entry = decorSet.FindEntry(placement.paletteId);
                if (entry == null)
                    continue;
                if (CaveDecorProjector.TryResolve(context, placement, out Vector3 position, out _, out _))
                    occupancy.Add((position, placement.paletteId, entry.boundingRadius * placement.scale));
            }
        }

        private void SetStatus(string message)
        {
            status = message;
            Repaint();
        }

        private int CountRoutes()
        {
            int count = 0;
            foreach (string _ in context.RouteIds)
                count++;
            return count;
        }

        // ── scene view ────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!brushActive || decorSet == null || context == null || !context.IsValid)
                return;

            // Without this the scene view keeps its default click-to-select and drag-to-box-select
            // behaviour, and every stroke would fight the selection tool.
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);

            Event current = Event.current;
            UpdateCursor(current);

            if (hasCursor)
                DrawCursor();

            if (current.alt || current.control || current.command)
                return;

            switch (current.type)
            {
                case EventType.MouseDown when current.button == 0:
                    hasPainted = false;
                    ApplyStroke(current);
                    current.Use();
                    break;

                case EventType.MouseDrag when current.button == 0 && mode != BrushMode.Single:
                    ApplyStroke(current);
                    current.Use();
                    break;

                case EventType.MouseUp when current.button == 0:
                    if (hasPainted)
                    {
                        RefreshOccupancy();
                        CaveDecorSpawner.MarkSceneDirty();
                    }
                    current.Use();
                    break;

                case EventType.KeyDown when current.keyCode == KeyCode.Escape:
                    brushActive = false;
                    Repaint();
                    current.Use();
                    break;
            }
        }

        private void UpdateCursor(Event current)
        {
            hasCursor = false;
            if (current.type != EventType.Repaint && current.type != EventType.MouseMove &&
                current.type != EventType.MouseDown && current.type != EventType.MouseDrag &&
                current.type != EventType.Layout)
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!context.Shell.Raycast(ray, out RaycastHit hit, 5000f))
                return;

            hasCursor = true;
            cursorPoint = hit.point;
            cursorNormal = hit.normal;

            string routeId = context.FindNearestRouteId(hit.point, out cursorRouteDistance, out _);
            cursorZone = routeId != null ? context.ResolveZoneId(routeId, cursorRouteDistance) ?? "-" : "-";

            if (current.type == EventType.MouseMove)
                SceneView.RepaintAll();
        }

        private void DrawCursor()
        {
            Handles.color = mode == BrushMode.Erase
                ? new Color(1f, 0.35f, 0.3f, 0.9f)
                : new Color(0.3f, 0.9f, 1f, 0.9f);
            Handles.DrawWireDisc(cursorPoint, cursorNormal, brushRadius);
            Handles.DrawWireDisc(cursorPoint, cursorNormal, brushRadius * 0.5f);
            Handles.color = new Color(1f, 1f, 1f, 0.6f);
            Handles.DrawLine(cursorPoint, cursorPoint + cursorNormal * brushRadius * 0.3f);
            Handles.Label(cursorPoint + cursorNormal * brushRadius * 0.35f,
                $"{cursorZone}  {cursorRouteDistance:0.0} m");
        }

        private void ApplyStroke(Event current)
        {
            UpdateCursor(current);
            if (!hasCursor)
                return;

            // Space strokes out so a slow drag does not dump a hundred props on one spot.
            if (hasPainted && mode != BrushMode.Single &&
                (cursorPoint - lastPaintPoint).sqrMagnitude < Mathf.Pow(brushRadius * 0.35f, 2f))
                return;

            lastPaintPoint = cursorPoint;
            hasPainted = true;

            if (mode == BrushMode.Erase)
                EraseAtCursor();
            else
                PaintAtCursor(mode == BrushMode.Single ? 1 : propsPerStroke);
        }

        private void EraseAtCursor()
        {
            float radiusSqr = brushRadius * brushRadius;
            var doomed = new List<string>();

            foreach (CaveDecorInstance marker in CaveDecorSpawner.FindAllInstances())
            {
                if ((marker.transform.position - cursorPoint).sqrMagnitude <= radiusSqr)
                    doomed.Add(marker.placementId);
            }

            foreach (string placementId in doomed)
            {
                CaveDecorSpawner.RemoveInstance(placementId);
                decorSet.RemovePlacement(placementId);
            }

            if (doomed.Count > 0)
            {
                EditorUtility.SetDirty(decorSet);
                SetStatus($"지우기: {doomed.Count}개 제거");
            }
        }

        private void PaintAtCursor(int count)
        {
            // The brush works in route space rather than on a screen-aligned plane: the cursor is
            // unprojected to (distance along the route, angle around the tube), and candidates are
            // jittered in those two axes. Samples then follow the tunnel around corners instead of
            // sliding off a flat disc.
            if (!CaveDecorProjector.TryUnproject(context, cursorPoint, out string routeId, out float centreDistance,
                    out float centreAngle, out _, out CaveDecorSurface centreSurface))
                return;

            float tubeRadius = Mathf.Max(1f, centreSurface.CenterlineDistance);
            float angleSpread = Mathf.Rad2Deg * (brushRadius / tubeRadius);
            string zoneId = context.ResolveZoneId(routeId, centreDistance);

            List<CaveDecorPaletteEntry> candidates = EligibleEntries(zoneId);
            if (candidates.Count == 0)
            {
                SetStatus($"{zoneId} 구간에서 쓸 수 있는 팔레트 항목이 없다.");
                return;
            }

            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                CaveDecorPaletteEntry entry = PickWeighted(candidates);
                if (entry == null)
                    continue;

                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float distance = centreDistance + ((float)random.NextDouble() * 2f - 1f) * brushRadius;
                    float angle = centreAngle + ((float)random.NextDouble() * 2f - 1f) * angleSpread;

                    if (!CaveDecorProjector.TryCast(context, routeId, distance, angle, out CaveDecorSurface surface))
                        continue;
                    if (!entry.AllowsSurface(surface.kind) || (surfaceFilter & surface.kind) == 0)
                        continue;
                    if ((surface.point - cursorPoint).sqrMagnitude > brushRadius * brushRadius)
                        continue;

                    float scale = Mathf.Lerp(entry.scaleRange.x, entry.scaleRange.y, (float)random.NextDouble());
                    float radius = entry.boundingRadius * scale;
                    float embed = Mathf.Lerp(entry.embedFractionRange.x, entry.embedFractionRange.y,
                        (float)random.NextDouble()) * radius;
                    Vector3 position = surface.point + surface.normal * embed;

                    if (!decorSet.ClearsCorridor(position, surface.centerline, radius))
                        continue;
                    if (ViolatesSpacing(position, entry, radius))
                        continue;

                    string placementZone = context.ResolveZoneId(routeId, distance);
                    var placement = new CaveDecorPlacement
                    {
                        id = Guid.NewGuid().ToString("N"),
                        paletteId = entry.id,
                        routeId = routeId,
                        routeDistance = distance,
                        angleDegrees = angle,
                        surfaceOffset = embed,
                        surfaceRotation = CaveDecorProjector.BuildRandomSurfaceRotation(surface, entry, random),
                        scale = scale,
                        zoneId = placementZone
                    };

                    decorSet.Placements.Add(placement);
                    CaveDecorSpawner.Spawn(decorSet, context, placement);
                    occupancy.Add((position, entry.id, radius));
                    placed++;
                    break;
                }
            }

            if (placed > 0)
            {
                EditorUtility.SetDirty(decorSet);
                SetStatus($"칠하기: {zoneId}에 {placed}개 배치 (총 {decorSet.Placements.Count})");
            }
        }

        private List<CaveDecorPaletteEntry> EligibleEntries(string zoneId)
        {
            var eligible = new List<CaveDecorPaletteEntry>();
            foreach (CaveDecorPaletteEntry entry in decorSet.Palette)
            {
                if (entry == null || entry.prefab == null)
                    continue;
                if (disabledEntries.Contains(entry.id))
                    continue;
                if (!entry.AllowsZone(zoneId))
                    continue;
                if ((entry.allowedSurfaces & surfaceFilter) == 0)
                    continue;
                eligible.Add(entry);
            }
            return eligible;
        }

        private CaveDecorPaletteEntry PickWeighted(List<CaveDecorPaletteEntry> candidates)
        {
            float total = 0f;
            foreach (CaveDecorPaletteEntry entry in candidates)
                total += Mathf.Max(0.0001f, entry.weight);

            float pick = (float)random.NextDouble() * total;
            foreach (CaveDecorPaletteEntry entry in candidates)
            {
                pick -= Mathf.Max(0.0001f, entry.weight);
                if (pick <= 0f)
                    return entry;
            }
            return candidates.Count > 0 ? candidates[candidates.Count - 1] : null;
        }

        private bool ViolatesSpacing(Vector3 position, CaveDecorPaletteEntry entry, float radius)
        {
            for (int i = 0; i < occupancy.Count; i++)
            {
                var other = occupancy[i];
                float required = other.paletteId == entry.id ? entry.minSpacing : radius + other.radius;
                if ((position - other.position).sqrMagnitude < required * required)
                    return true;
            }
            return false;
        }

        private static string DescribeSurfaces(CaveSurfaceKind kind)
        {
            if (kind == CaveSurfaceKind.Any)
                return "전체";

            string text = "";
            if ((kind & CaveSurfaceKind.Floor) != 0)
                text += "바닥 ";
            if ((kind & CaveSurfaceKind.Wall) != 0)
                text += "벽 ";
            if ((kind & CaveSurfaceKind.Ceiling) != 0)
                text += "천장";
            return text.Trim();
        }
    }
}
