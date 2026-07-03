# Changelog

All notable changes to this plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] — 2026-05-26

### Added
- USDZ asset import. USDZ assets carry their own materials, which are applied automatically and adapt to the active render pipeline (Built-in / URP / HDRP).
- `VARCO3D` top-level menu with **Connect VARCO3D** (toggle, shows running status via a checkmark), **Open VARCO3D**, and **Open User Guide**.
- In-package README displayed in Unity's Package Manager details panel.

### Changed
- The bridge accepts both USDZ and the legacy ZIP+FBX bundle, dispatched by the incoming URL.
- Minimum Unity version raised to Unity 6 LTS (required by Unity's USD Importer dependency).
- Imports now work as long as Unity is open and the bridge is connected — no need to keep any plugin window open.
- More reliable server shutdown when Unity exits or recompiles scripts.
- Reduced Console log noise during normal asset imports. Warnings and errors remain unchanged.

### Fixed
- Entering Play mode no longer leaves the bridge disconnected. The user's Connect state is remembered for the current Unity session and the server reconnects automatically after Unity's domain reload (Play mode enter/exit, script recompile). Explicit Disconnect still sticks until the user reconnects.

### Removed
- Bridge panel window. All controls have moved to the `VARCO3D` menu.

## [1.0.0] — 2026-04-24

First public release.

- ZIP+FBX asset import with PBR materials reconstructed from accompanying metadata.
- Bridge server on local port 5326.
- Render pipeline auto-detection (Built-in / URP / HDRP) with matching shader selection.
- Editor panel with Connect / Disconnect controls.
