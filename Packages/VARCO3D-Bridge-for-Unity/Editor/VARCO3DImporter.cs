namespace NCAI.VARCO3D.Bridge
{
    /// <summary>
    /// Format dispatcher — routes an ImportTask to the USDZ or legacy FBX
    /// pipeline based on the format detected in the HTTP handler.
    ///
    /// The actual work lives in:
    ///   - VARCO3DUsdzImporter (current format, materials handled by Unity USD Importer)
    ///   - VARCO3DFbxImporter  (legacy ZIP+FBX+metadata.json, per-pipeline PBR rebuild)
    ///
    /// Mirrors the dispatch pattern used by Blender (operators._import_asset),
    /// Maya, and 3ds Max. The legacy FBX path will be removed once VARCO3D
    /// stops emitting ZIP+FBX URLs.
    /// </summary>
    public static class VARCO3DImporter
    {
        public static void ProcessImport(ImportTask task)
        {
            if (task == null) return;

            if (task.Fmt == "usdz")
                VARCO3DUsdzImporter.Import(task);
            else
                VARCO3DFbxImporter.Import(task);  // legacy
        }
    }
}
