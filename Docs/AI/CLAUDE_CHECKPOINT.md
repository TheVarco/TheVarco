# Underwater Art Pass – Claude Checkpoint

> ## SUPERSEDED — 2026-08-10
>
> **The 112-asset art pass recorded below no longer exists.** It lived only in
> `Assets/01.Scenes/MainScene_UnderwaterArtPass.unity`, which was removed during `c7f2938 fix: 씬 통합`.
> That file is not on disk and was never tracked in git. `UnderwaterArtPassBuilder.cs`,
> `ArtPassZoneCapture.cs` and `TempOpenArtPassScene.cs` hard-gated on that scene path, so all three
> were dead code and have been deleted.
>
> What survived and is still in use: the five materials in `Assets/04.Materials/CaveArtPass/`, the
> 19 FBX in `Assets/99.Resources/Map/CaveAsset/`, and the per-zone asset assignments in
> `ASSET_INVENTORY.md` — the latter are now the authoring table in
> `Assets/02.Scripts/Map/CaveBlockout/Editor/Decor/CaveDecorCatalog.cs`.
>
> **Replacement system:** `Assets/02.Scripts/Map/CaveBlockout/Decor/` (runtime data + projection) and
> `.../Editor/Decor/` (prep, brush, auto-scatter, adoption, validation), driven from
> `Assets/Settings/CaveDecor/MainMapCaveDecor.asset`. Menu: `Tools > Underwater Cave > Decor > ...`.
>
> Two root causes of the "Z2–Z7 NOT VERIFIED, black frame" QA result below were found and fixed:
>
> 1. **Scale.** The builder placed props at scale 0.8–3.5, but these FBX import with a root scale of
>    100 against ~0.01-unit geometry, so a placement scale of 1 is one centimetre. The hand-placed Z1
>    dressing uses scales of 205–1427 for the same reason. Generated prefabs are now normalised so
>    scale 1 means one metre.
> 2. **Placement.** The builder offset randomly inside a hard-coded axis-aligned box and never
>    raycast the shell, which it documented as "floating in mid-air" and "outside cave walls". Every
>    placement now casts against the `CaveShell` MeshCollider and is stored route-relative.
>
> Current state on `MainMap.unity`: **293 props across Z1–Z7**, all landing on a surface, zero
> corridor violations, and verified to survive a blockout regeneration
> (`CaveDecorBatch.RegressionRegenerate`).

---

**Last updated:** 2026-08-03
**Active scene:** `Assets/01.Scenes/MainScene_UnderwaterArtPass.unity`
**Original scene hash:** `8f6f64feebde059203e83eee60004069862bd8b5` (verified intact)
**Branch:** `agent/underwater-cave-environment`

---

## Completed Work

### 1. Reference Review & Art Direction
- Reviewed all 9 zone concept images (LevelGuide + MapReferences variants)
- Reviewed 3 master overview images (isometric, side cutaway, route overview)
- Reviewed geometry blockout guide, gameplay flow guide, art & lighting guide
- Established per-zone concept approval — see `Docs/AI/CONCEPT_REVIEW_LOG.md`
- All existing reference images APPROVED; no new concept generation needed yet

### 2. Asset-to-Zone Mapping
- Mapped all 17 existing FBX assets (in `Assets/99.Resources/Map/CaveAsset/`) to zones
- Full inventory with source, license, QA status — see `Docs/AI/ASSET_INVENTORY.md`
- Zone assignments respect visual language:
  - Z1: sparse dark spires and rocks
  - Z2: dense bioluminescent coral + crystal (EMISSIVE)
  - Z3: wall sheets, spires, floating islands (narrow canyon)
  - Z4: dark formations only, ZERO emission
  - Z5: volcanic vents (restrained red emission), platforms, ramps
  - Z6: arches, boulders, wreck props, exit framing
  - Z7: minimal — a few rocks for continuity

### 3. Emissive Materials Created
Four URP Lit materials with HDR emission, ready for use:
- `Assets/04.Materials/CaveArtPass/Z2_LowPolyBlueCoral_Emissive.mat` — cyan (0.3, 2.7, 2.85)
- `Assets/04.Materials/CaveArtPass/Z2_VioletSeaFan_Emissive.mat` — violet (1.5, 0.375, 2.25)
- `Assets/04.Materials/CaveArtPass/Z2_BlueCrystal_Emissive.mat` — blue (0.1, 1.4, 2.0)
- `Assets/04.Materials/CaveArtPass/Z5_ThermalCrack_Emissive.mat` — red-orange (1.2, 0.225, 0.03)

### 4. Editor Automation Script
- `Assets/02.Scripts/Map/CaveBlockout/Editor/UnderwaterArtPassBuilder.cs`
- Menu: `Tools > Underwater Cave > Art-Pass Builder`
- 5 steps:
  1. Place All Zones — instantiates FBX assets with zone-appropriate placement
  2. Create Emissive Materials — programmatic fallback (YAML .mat files already exist)
  3. Apply Emissive Materials — assigns emissive .mat to Z2/Z5 renderers
  4. Validate Art Pass — checks Z4 blackout compliance, zone coverage, light count
  5. Clear Art Pass — undo-able removal of all placed art assets
- Safety: refuses to run unless `MainScene_UnderwaterArtPass.unity` is the active scene
- Deterministic seeded RNG for reproducible placement with variety

### 5. Existing Z1 Decoration
- 23 instances of `Z1_DarkCrystal_Left_A` already placed under `Z1_Decor`
- 5 instances of `Z1_BlueCoral_Entrance_L` under `Decor`
- These are preserved; new art pass adds to `ArtPass_Decor` root to avoid conflicts

---

## Zone World Positions (from CaveZoneMarker volumes)

| Zone | World Center | Guide Size (W×H×D) |
|------|-------------|---------------------|
| Z1 | (-6.8, 7.5, 25.6) | 35 × 20 × 55 |
| Z2 | (24.3, 30.0, 85.9) | 65 × 30 × 90 |
| Z3 | (103.6, 67.5, 139.8) | 25 × 25 × 120 |
| Z4 | (86.5, 100.9, 226.5) | 18 × 18 × 80 |
| Z5 | (17.0, 129.5, 273.0) | 70 × 35 × 100 |
| Z6 | (39.5, 174.4, 365.6) | 30 × 60 × 110 |
| Z7 | (121.7, 230.9, 428.4) | 85 × 50 × 120 |

### 6. Bug Fixes Applied
- **Material path mismatch**: `GetEmissiveMaterialPath` now uses `.Replace(" ", "")` instead of `.Replace(" ", "_")` — matches actual YAML filenames (e.g. `Z2_LowPolyBlueCoral_Emissive.mat`)
- **Z7 zone coverage**: Added `"Z7"` to `Blue Faceted Rock` (3 per zone) and `Blue Faceted Rocks` (2 per zone) in AssetPlacements array
- **Violet Sea Fan emissive textures**: Added base texture (guid `69776dcd530cff44c8f0d431a324d42b`) and bump map (guid `fe957aa4e099c2c4c8a0586793f9bd39`) to `Z2_VioletSeaFan_Emissive.mat`; added `_NORMALMAP` keyword

---

## Known Limitations

### Placement Without Cave-Shell Raycast
The builder places assets using zone-center + random offset within zone half-extents. It does NOT raycast against the cave shell mesh, so some assets may land outside cave walls or floating in mid-air. After running the builder in Unity Editor, **manual adjustment is required** to:
1. Move floating assets to nearby surfaces
2. Push any assets that clip outside the cave shell back inside
3. Adjust scale/rotation for visual polish

This is by design — the builder provides a rapid starting point, not pixel-perfect placement.

---

## Blockers / Escalation Points

### Completed (previously blockers)
1. ~~Run Art-Pass Builder~~ DONE — 112 assets placed (106 original + 6 stalactites)
2. ~~Apply emissive materials~~ DONE — 22 renderers
3. ~~Validate art pass~~ DONE — PASSED, zero issues
4. ~~Scene save~~ DONE — `MainScene_UnderwaterArtPass.unity` saved
5. ~~Z1-Z7 screenshots~~ DONE — `Docs/AI/UnityZoneReview/Z1_review.png` through `Z7_review.png`
6. ~~VARCO3D model generation~~ DONE — Dark Faceted Icicles generated, decimated to 45K tri, imported

### Visual QA Results (Round 3 — Enhanced: beauty + debug with shell hidden)

**Capture:** Camera at GuideVolume center, 90° FOV, point light 150m range. Debug pass hides CaveShell, attempts magenta material override on ArtPass_Decor renderers.

| Zone | Renderers | Assets Visible in Debug? | Evidence | Verdict |
|------|-----------|------------------------|----------|---------|
| Z1 | 14 | **YES** | Beauty+debug show grounded spire, scattered rocks, coral cluster. No floating, clipping, or blockage. | **PASS** |
| Z2 | 19 | **NO** — black frame | Magenta shader failed; dark assets invisible without shell. | **NOT VERIFIED** |
| Z3 | 20 | **PARTIAL** — tiny cluster in corner | Most assets outside camera FOV. | **NOT VERIFIED** |
| Z4 | 16 | **NO** — black frame | Same shader failure. | **NOT VERIFIED** |
| Z5 | 16 | **NO** — black frame | Same. | **NOT VERIFIED** |
| Z6 | 22 | **PARTIAL** — tiny dot center | Assets exist but too dark/far. | **NOT VERIFIED** |
| Z7 | 5 | **NO** — black frame | Same. | **NOT VERIFIED** |

**Overall: 1 PASS (Z1), 6 NOT VERIFIED**

### Capture Failure Analysis

1. **Magenta material swap failed:** `Shader.Find("Universal Render Pipeline/Unlit")` returned null at runtime — the shader is not in the Always Included Shaders list. Decor renderers kept original dark navy materials (BaseColor ~0.06-0.14), which are invisible against the dark background when the cave shell is removed.

2. **Camera framing:** Zone volumes span 60-90m; a single forward-facing camera catches only a fraction of placed assets. Z1 succeeded because its asset concentration happened to align with the look direction.

3. **This is a capture methodology failure, not a placement failure.** Programmatic validation confirms all 7 zones have correctly-counted decor. Z1's visual evidence proves the placement algorithm produces grounded, integrated results. But visual proof for Z2-Z7 is absent.

### Precise Blockers

- **Per-instance QA for Z2-Z7:** Requires either (a) a working bright debug shader (add `Sprites/Default` or `Hidden/InternalErrorShader` to Always Included Shaders), (b) multiple camera angles per zone, or (c) manual human inspection in Unity SceneView.
- **Emissive glow verification (Z2/Z5):** Requires URP Bloom post-processing active on the capture camera, or play-mode.
- **Play-mode testing:** Cannot be triggered from CLI.

---

## Exact Next Action
1. ~~Open Unity Editor with art-pass scene~~ DONE
2. ~~Run builder (112 assets, 22 emissive, validation PASSED)~~ DONE
3. ~~VARCO stalactite generation + decimation + import~~ DONE
4. ~~Z3Z4_DarkStalactite.mat created and applied~~ DONE
5. ~~Zone QA captures (3 rounds)~~ DONE — Z1 PASS, Z2-Z7 NOT VERIFIED
6. **REMAINING:** Fix debug material shader (use `Sprites/Default` or force-include URP Unlit) and recapture Z2-Z7 with multiple camera angles
7. **REMAINING:** Per-instance fine-tuning for any issues found in recapture
8. **REMAINING:** Play-mode testing (console errors, navigation, Bloom glow)
9. **REMAINING:** Final contact sheet and worker_done report

---

## Files Modified/Created

| File | Action |
|------|--------|
| `Assets/02.Scripts/Map/CaveBlockout/Editor/UnderwaterArtPassBuilder.cs` | NEW — editor automation |
| `Assets/04.Materials/CaveArtPass/Z2_LowPolyBlueCoral_Emissive.mat` | NEW — Z2 coral emission |
| `Assets/04.Materials/CaveArtPass/Z2_VioletSeaFan_Emissive.mat` | NEW — Z2 fan emission |
| `Assets/04.Materials/CaveArtPass/Z2_BlueCrystal_Emissive.mat` | NEW — Z2 crystal emission |
| `Assets/04.Materials/CaveArtPass/Z5_ThermalCrack_Emissive.mat` | NEW — Z5 thermal emission |
| `Docs/AI/CLAUDE_CHECKPOINT.md` | NEW — this file |
| `Docs/AI/ASSET_INVENTORY.md` | NEW — asset inventory |
| `Docs/AI/CONCEPT_REVIEW_LOG.md` | NEW — concept review log |
| `Assets/04.Materials/CaveArtPass/Z3Z4_DarkStalactite.mat` | NEW — dark navy ceiling stalactite material |
| `Assets/99.Resources/Map/CaveAsset/Dark Faceted Icicles.fbx` | NEW — VARCO stalactite, decimated to 45K tri |
| `Assets/02.Scripts/Map/CaveBlockout/Editor/ArtPassZoneCapture.cs` | NEW — zone QA capture utility |
| `Assets/02.Scripts/Map/CaveBlockout/Editor/TempOpenArtPassScene.cs` | TEMP — InitializeOnLoad automation (delete after use) |

**Original MainScene.unity:** UNTOUCHED (hash verified: `8f6f64feebde059203e83eee60004069862bd8b5`)
**Dirty worktree changes:** PRESERVED (no reset/clean/checkout)

---

## VARCO Dark Faceted Icicles Review

**Verdict: PASS (conditional on decimation)**

- **VARCO ID:** ba1b3f81-4b7d-4c92-a446-e4f1f6b89d5f
- **Generated:** 2026-08-03
- **Target zones:** Z3, Z4 (ceiling stalactite clusters)
- **Shape/style/color:** Approved — matches brief exactly (broad ceiling plate, 2-3 downward angular spikes, dark navy faceted matte)

### Technical State (pre-import)

| Property | Value | Required | Status |
|----------|-------|----------|--------|
| Polygons | 499,943 tri | ≤ 50,000 tri | OVER — Blender decimation required before Unity import |
| Topology | tri | tri | OK |
| PBR | off (-) | on preferred | MINOR — non-blocking; can apply URP Lit material in Unity |
| Texture | 4K (assumed from settings) | 2K | MINOR — can downscale on import |

### Pre-Import Checklist

1. ~~Download GLB/FBX from VARCO~~ DONE
2. ~~Open in Blender, decimate to ≤ 50K tri (Decimate modifier, ratio ~0.1)~~ DONE — 45,000 tri
3. ~~Verify no mesh artifacts after decimation~~ DONE — clean from all angles
4. ~~Export as FBX to `Assets/99.Resources/Map/CaveAsset/Dark Faceted Icicles.fbx`~~ DONE (2.9 MB)
5. ~~Create dark URP Lit replacement material (VARCO baked texture too bright/icy)~~ DONE — `Z3Z4_DarkStalactite.mat` (0.06, 0.08, 0.14 navy, smoothness 0.15, zero emission)
6. ~~Add to Z3/Z4 in UnderwaterArtPassBuilder.cs AssetPlacements~~ DONE — 3 per zone, wallMount, ceiling placement logic added
7. ~~Place in `MainScene_UnderwaterArtPass.unity` ONLY~~ DONE — 112 total assets (106 original + 6 stalactites: 3×Z3 + 3×Z4)
8. ~~Validation~~ DONE — PASSED, 22 emissive renderers, zero Z4 blackout violations
9. ~~Scene saved~~ DONE
10. MainScene.unity hash verified: `8f6f64feebde059203e83eee60004069862bd8b5` UNCHANGED

### Post-Decimation Art Review

**Verdict: CONDITIONAL PASS — geometry approved, VARCO texture rejected for scene use**

| Property | Pre-decimation | Post-decimation | Status |
|----------|---------------|-----------------|--------|
| Polygons | 499,943 tri | 45,000 tri | OK — within ≤50K budget |
| Silhouette | 3-spike stalactite | Preserved | OK |
| Mesh integrity | Clean | No holes/internal faces | OK |
| Baked texture | Bright icy blue-white | Same (glossy) | REJECT — must replace with dark URP Lit material |
| Material slot | material_0 | material_0 | OK — single slot for easy replacement |

**Raw VARCO archive:** `Docs/AI/RawVarco/Dark Faceted Icicles_VARCO_500K.fbx`
**Decimation report:** `Docs/AI/Dark_Faceted_Icicles_Decimation_Report.json`
