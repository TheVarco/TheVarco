using UnityEditor;
using UnityEngine;

namespace NCAI.VARCO3D.Bridge
{
    /// <summary>
    /// Top-level VARCO3D menu — the plugin's only UI surface.
    ///
    /// Mirrors the menu-driven UX used by the 3ds Max and Maya plugins: a single
    /// dropdown with a Connect toggle (showing a checkmark while the server is
    /// running) plus links out to the web. There is no persistent panel — the
    /// plugin sits in the background until the user toggles Connect.
    ///
    /// Per-DCC equivalents:
    ///   - 3ds Max: macroScript ConnectVarco3D + cuiRegisterMenus callback
    ///     (Post-Start-Up_Scripts/varco3d_post_startup.ms)
    ///   - Maya:    userSetup.py menu entries with checkBox tracking
    /// </summary>
    internal static class VARCO3DMenu
    {
        // Priorities: gap > 10 inserts a separator. Connect at top, links below.
        private const int ConnectPriority = 100;
        private const int WebPriority = 200;
        private const int GuidePriority = 201;

        // "Connect VARCO3D" — matches the Maya / 3ds Max plugins' menu label
        // (instead of bare "Connect") so the action is self-describing when the
        // menu is glanced at from outside the VARCO3D submenu.
        private const string ConnectPath = "VARCO3D/Connect VARCO3D";
        private const string WebPath = "VARCO3D/Open VARCO3D";
        private const string GuidePath = "VARCO3D/Open User Guide";

        // Hardcoded for now. If per-environment overrides (staging vs prod) are
        // ever needed, promote to a JSON config alongside the plugin (Maya does
        // this via plugin_config.json).
        private const string WebUrl = "https://3d.varco.ai/";
        private const string GuideUrl = "https://3d.varco.ai/blog/2026-03-19-varco-3d-bridge-for-unity";

        // ----------------------------------------------------------------
        // Connect (toggle with checkmark)
        // ----------------------------------------------------------------

        [MenuItem(ConnectPath, false, ConnectPriority)]
        private static void ToggleConnect()
        {
            if (VARCO3DServer.IsRunning)
            {
                VARCO3DServer.Stop(blocking: true);
                // Clear the session intent — user explicitly disconnected, so
                // the lifecycle must NOT auto-reconnect after the next domain
                // reload (e.g., entering Play mode).
                SessionState.SetBool(VARCO3DConstants.AutoConnectSessionKey, false);
            }
            else
            {
                VARCO3DServer.Start(VARCO3DConstants.DefaultPort);
                // Record the user's intent so VARCO3DLifecycle re-starts the
                // server automatically across domain reloads. Without this the
                // user has to re-Connect every time they enter/exit Play mode.
                SessionState.SetBool(VARCO3DConstants.AutoConnectSessionKey, true);
            }

            // Push the new checkmark state right away. Without this the checkmark
            // would only refresh on the next menu open.
            Menu.SetChecked(ConnectPath, VARCO3DServer.IsRunning);
        }

        /// <summary>
        /// Validator — fires every time the menu opens. Reconciles the checkmark
        /// with the actual server state so external changes (domain reload,
        /// lifecycle hooks, server self-stop on error) stay visible.
        /// </summary>
        [MenuItem(ConnectPath, true)]
        private static bool ToggleConnectValidate()
        {
            Menu.SetChecked(ConnectPath, VARCO3DServer.IsRunning);
            return true;
        }

        // ----------------------------------------------------------------
        // External links
        // ----------------------------------------------------------------

        [MenuItem(WebPath, false, WebPriority)]
        private static void OpenWeb() => Application.OpenURL(WebUrl);

        [MenuItem(GuidePath, false, GuidePriority)]
        private static void OpenGuide() => Application.OpenURL(GuideUrl);
    }
}
