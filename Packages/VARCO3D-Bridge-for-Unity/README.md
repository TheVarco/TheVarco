# VARCO3D Bridge for Unity

Receive 3D assets generated on the VARCO3D web service directly into your Unity scene with one click.

When you click **Send to Unity** in the VARCO3D web service, this plugin's local bridge server downloads the asset (USDZ or legacy ZIP+FBX) and places it into the active scene. Materials are produced by Unity's USD Importer and adapt automatically to the active render pipeline.

## Requirements

- **Unity 6 LTS (`6000.0`)** or later
- **`com.unity.importer.usd`** — installed automatically as a dependency
- Built-in / URP / HDRP — supported via the USD Importer's pipeline-aware material handling

## Installation

1. Download `VARCO3D-Bridge-for-Unity-v<semver>.zip` from the VARCO3D service.
2. Extract the archive to any folder.
3. In Unity: **Window > Package Manager** → `+` → **"Add package from disk..."** → select the `package.json` inside the extracted `VARCO3D-Bridge-for-Unity/` folder.
4. Unity downloads the USD Importer dependency on first install (this can take several minutes).

## Usage

Use the **VARCO3D** menu in the Editor's main menu bar:

| Item | Action |
|---|---|
| **Connect** | Toggle the bridge server (port 5326). A checkmark appears while the server is running. |
| **Open VARCO3D** | Open `https://3d.varco.ai/` in your default browser. |
| **Open User Guide** | Open the plugin user guide in your default browser. |

With **Connect** enabled, return to the VARCO3D web service and click **Send to Unity** on any asset. The asset will appear in the scene under `Assets/VARCO3DImports/`.

## Links

- **Documentation**: https://3d.varco.ai/blog/2026-03-19-varco-3d-bridge-for-unity
- **Web service**: https://3d.varco.ai/
- **Terms of Use**: https://terms.varco.ai/

## License

Use of this plugin is governed by the VARCO3D Terms of Use. See https://terms.varco.ai/.
