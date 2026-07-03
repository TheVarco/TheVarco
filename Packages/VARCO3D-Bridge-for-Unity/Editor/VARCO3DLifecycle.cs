using UnityEditor;

namespace NCAI.VARCO3D.Bridge
{
    /// <summary>
    /// Editor-wide lifecycle wiring for the bridge.
    ///
    /// Two responsibilities, both anchored to the Editor's lifetime rather than
    /// to any UI window (the plugin no longer has a persistent panel — see
    /// VARCO3DMenu.cs):
    ///
    ///   1. Pump the server's import queue on the main thread every editor tick.
    ///      The TCP server runs on a background thread and parks completed
    ///      downloads in a queue; AssetDatabase / scene mutation must happen on
    ///      the main thread, so we drain the queue from EditorApplication.update.
    ///
    ///   2. Stop the server cleanly on domain reload (script recompile) and on
    ///      Editor quit. Without these guards the TcpListener can hold port 5326
    ///      across reloads, causing "address already in use" on the next session.
    ///
    /// Domain reload note: Unity recycles the AppDomain on reload, which clears
    /// all static event subscriptions. The static constructor runs again under
    /// [InitializeOnLoad], so re-subscribing happens automatically — no explicit
    /// unsubscribe is needed.
    ///
    /// Play mode re-connect: entering Play mode triggers a domain reload, which
    /// fires beforeAssemblyReload → StopServer. The user-intent flag stored in
    /// SessionState (VARCO3DConstants.AutoConnectSessionKey) survives the
    /// reload, and we use it here to restart the server automatically. The
    /// flag is set/cleared only by explicit user toggle in VARCO3DMenu, so
    /// StopServer's reload-time call does not clobber the intent.
    /// </summary>
    [InitializeOnLoad]
    internal static class VARCO3DLifecycle
    {
        static VARCO3DLifecycle()
        {
            EditorApplication.update += PumpImportQueue;
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
            EditorApplication.quitting += StopServer;

            // If the user had Connect on before the reload, restore it. Deferred
            // to delayCall so the Editor is fully settled before we bind the
            // TCP socket — starting straight from the static ctor occasionally
            // races with other [InitializeOnLoad] initializers.
            if (SessionState.GetBool(VARCO3DConstants.AutoConnectSessionKey, false))
                EditorApplication.delayCall += AutoStartIfNeeded;
        }

        private static void AutoStartIfNeeded()
        {
            if (!VARCO3DServer.IsRunning &&
                SessionState.GetBool(VARCO3DConstants.AutoConnectSessionKey, false))
            {
                VARCO3DServer.Start(VARCO3DConstants.DefaultPort);
            }
        }

        /// <summary>
        /// Drain one queued import per tick. Cheap when the queue is empty
        /// (single lock + count check) so leaving this registered while the
        /// server is idle costs effectively nothing.
        /// </summary>
        private static void PumpImportQueue()
        {
            ImportTask task = VARCO3DServer.DequeueImport();
            if (task != null)
                VARCO3DImporter.ProcessImport(task);
        }

        private static void StopServer()
        {
            if (VARCO3DServer.IsRunning)
                VARCO3DServer.Stop(blocking: true);
        }
    }
}
