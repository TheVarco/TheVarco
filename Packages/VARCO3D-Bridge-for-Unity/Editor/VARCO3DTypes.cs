using System;
using UnityEngine;

namespace NCAI.VARCO3D.Bridge
{
    /// <summary>
    /// Shared data types and constants for the VARCO3D plugin.
    /// </summary>
    public static class VARCO3DConstants
    {
        public const int DefaultPort = 5326;
        public const string LogPrefix = "[VARCO3D]";
        public const string ImportFolder = "Assets/VARCO3DImports";

        // SessionState key for "user has toggled Connect on". Used by the
        // lifecycle to re-start the server after a domain reload (Play mode
        // enter/exit, script recompile, package install). SessionState scope
        // matches the desired UX: persists across reloads, resets on Editor
        // restart — Connect remains opt-in per Unity session.
        public const string AutoConnectSessionKey = "NCAI.VARCO3D.Bridge.AutoConnect";
    }

    public enum RenderPipelineType
    {
        BuiltIn,
        URP,
        HDRP,
        Unsupported
    }

    /// <summary>HTTP POST /import request body.</summary>
    [Serializable]
    public class ImportRequest
    {
        public string url;
        public string name;
    }

    /// <summary>Thread-safe queue item passed from server thread to main thread.
    /// Fmt is determined in the HTTP handler from URL extension — "usdz" for .usdz,
    /// "zip" for legacy ZIP+FBX bundles. FilePath points to the downloaded file
    /// (either .usdz or .zip).</summary>
    public class ImportTask
    {
        public string FilePath;
        public string AssetName;
        public string Fmt;
    }

    /// <summary>Deserialized metadata.json root.</summary>
    [Serializable]
    public class AssetMetadata
    {
        public string version;
        public MaterialDef[] materials;
    }

    /// <summary>Single material definition from metadata.json.</summary>
    [Serializable]
    public class MaterialDef
    {
        public string name;
        public string baseColorTexture;
        public float[] baseColorFactor;
        public string normalTexture;
        public string metallicRoughnessTexture;
        public float roughnessFactor;
        public float metallicFactor;
        public string occlusionTexture;
        public string emissiveTexture;
        public float[] emissiveFactor;
    }

    /// <summary>HTTP GET /status response.</summary>
    [Serializable]
    public class StatusResponse
    {
        public string dcc = "unity";
        public string version;
        public string status = "running";
    }

    /// <summary>HTTP POST /import response.</summary>
    [Serializable]
    public class ImportQueuedResponse
    {
        public string status;
        public string name;
        public string fmt;
    }

    /// <summary>HTTP error response.</summary>
    [Serializable]
    public class ErrorResponse
    {
        public string status;
        public string message;
    }
}
