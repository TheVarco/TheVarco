using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace NCAI.VARCO3D.Bridge
{
    /// <summary>
    /// TCP-based HTTP server running on a background thread.
    /// Handles /status, /import, and OPTIONS endpoints.
    /// Downloads assets and enqueues them for main-thread processing.
    /// </summary>
    public static class VARCO3DServer
    {
        private static Thread _serverThread;
        private static Thread _guardThread;
        private static TcpListener _listener;
        private static volatile bool _shouldStop;
        private static bool _isRunning;
        private static int _port;

        private static readonly Queue<ImportTask> _importQueue = new Queue<ImportTask>();
        private static readonly object _queueLock = new object();

        public static bool IsRunning => _isRunning;
        public static int Port => _port;

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        public static void Start(int port = VARCO3DConstants.DefaultPort)
        {
            if (_isRunning) return;

            _port = port;
            _shouldStop = false;

            _serverThread = new Thread(() => RunServer(port))
            {
                IsBackground = true,
                Name = "VARCO3DServer"
            };
            _serverThread.Start();
        }

        public static void Stop(bool blocking = false)
        {
            if (!_isRunning && _serverThread == null) return;

            _shouldStop = true;

            if (blocking)
            {
                _serverThread?.Join(3000);
                _guardThread?.Join(1000);
            }

            _serverThread = null;
            _guardThread = null;
        }

        public static ImportTask DequeueImport()
        {
            lock (_queueLock)
            {
                return _importQueue.Count > 0 ? _importQueue.Dequeue() : null;
            }
        }

        // ----------------------------------------------------------------
        // Server loop (background thread)
        // ----------------------------------------------------------------

        private static void RunServer(int port)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                _isRunning = true;

                // Start guard thread to handle clean shutdown
                _guardThread = new Thread(GuardJob) { IsBackground = true, Name = "VARCO3DGuard" };
                _guardThread.Start();

                Debug.Log($"{VARCO3DConstants.LogPrefix} Listening on port {port}");

                while (!_shouldStop)
                {
                    if (_listener.Pending())
                    {
                        TcpClient client = _listener.AcceptTcpClient();
                        try
                        {
                            using (NetworkStream stream = client.GetStream())
                            {
                                stream.ReadTimeout = 5000;
                                ProcessClientRequest(stream);
                            }
                        }
                        catch (IOException)
                        {
                            // Browser may open connections for preflight or close
                            // before sending data — safe to ignore silently.
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"{VARCO3DConstants.LogPrefix} Client error: {e.Message}");
                        }
                        finally
                        {
                            client.Close();
                        }
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
            }
            catch (SocketException e)
            {
                if (!_shouldStop)
                    Debug.LogError($"{VARCO3DConstants.LogPrefix} Server error: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{VARCO3DConstants.LogPrefix} Server error: {e.Message}");
            }
            finally
            {
                _isRunning = false;
                try { _listener?.Stop(); } catch { }
                Debug.Log($"{VARCO3DConstants.LogPrefix} Server stopped");
            }
        }

        private static void GuardJob()
        {
            while (!_shouldStop)
                Thread.Sleep(200);

            try { _listener?.Stop(); } catch { }
        }

        // ----------------------------------------------------------------
        // HTTP request processing
        // ----------------------------------------------------------------

        private static void ProcessClientRequest(NetworkStream stream)
        {
            byte[] buffer = new byte[16384];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) return;

            string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            string[] lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return;

            // Parse request line: "METHOD /path HTTP/1.1"
            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2) return;

            string method = requestLine[0].ToUpper();
            string path = requestLine[1];
            string origin = GetHeaderValue(lines, "Origin");

            if (method == "OPTIONS")
            {
                HandleOptions(stream, origin);
            }
            else if (method == "GET" && path == "/status")
            {
                HandleStatus(stream, origin);
            }
            else if (method == "POST" && path == "/import")
            {
                HandleImport(stream, request, origin);
            }
            else
            {
                SendNotFound(stream, origin);
            }
        }

        // ----------------------------------------------------------------
        // Route handlers
        // ----------------------------------------------------------------

        private static void HandleStatus(NetworkStream stream, string origin)
        {
            var response = new StatusResponse
            {
                version = Application.unityVersion
            };
            SendHttpResponse(stream, 200, "OK", JsonUtility.ToJson(response), origin);
        }

        private static void HandleImport(NetworkStream stream, string request, string origin)
        {
            try
            {
                // Find JSON body
                int bodyStart = request.IndexOf('{');
                if (bodyStart < 0)
                {
                    SendError(stream, "Missing JSON body", 400, "Bad Request", origin);
                    return;
                }

                string jsonBody = request.Substring(bodyStart);
                ImportRequest data = JsonUtility.FromJson<ImportRequest>(jsonBody);

                if (string.IsNullOrEmpty(data.url))
                {
                    SendError(stream, "Missing 'url' field", 400, "Bad Request", origin);
                    return;
                }

                if (string.IsNullOrEmpty(data.name))
                    data.name = "varco3d_asset";

                // Determine format from URL extension. Mirrors the pattern used in
                // Blender (server.py), Maya, 3ds Max, and Unreal — single /import
                // endpoint, format dispatched by URL extension.
                string fmt = DetectFormat(data.url);

                // Download to a temp .usdz or .zip file based on detected format
                string downloadPath = DownloadFile(data.url, data.name, fmt);

                // Validate magic bytes. USDZ is also a ZIP archive (uncompressed),
                // so both formats share the PK\x03\x04 signature.
                if (!ValidateZipMagic(downloadPath))
                {
                    File.Delete(downloadPath);
                    string expected = fmt == "usdz" ? "USDZ" : "ZIP";
                    SendError(stream, $"Downloaded file is not a valid {expected}", 400, "Bad Request", origin);
                    return;
                }

                // Enqueue for main thread processing
                lock (_queueLock)
                {
                    _importQueue.Enqueue(new ImportTask
                    {
                        FilePath = downloadPath,
                        AssetName = data.name,
                        Fmt = fmt
                    });
                }

                Debug.Log($"{VARCO3DConstants.LogPrefix} Queued import: {data.name} (fmt={fmt})");

                var response = new ImportQueuedResponse { status = "queued", name = data.name, fmt = fmt };
                SendHttpResponse(stream, 200, "OK", JsonUtility.ToJson(response), origin);
            }
            catch (WebException e)
            {
                Debug.LogError($"{VARCO3DConstants.LogPrefix} Download failed: {e.Message}");
                SendError(stream, $"Download failed: {e.Message}", 500, "Internal Server Error", origin);
            }
            catch (Exception e)
            {
                Debug.LogError($"{VARCO3DConstants.LogPrefix} Import error: {e.Message}");
                SendError(stream, e.Message, 500, "Internal Server Error", origin);
            }
        }

        private static void HandleOptions(NetworkStream stream, string origin)
        {
            SendHttpResponse(stream, 200, "OK", "", origin);
        }

        private static void SendNotFound(NetworkStream stream, string origin)
        {
            var response = new ErrorResponse { status = "error", message = "Not found" };
            SendHttpResponse(stream, 404, "Not Found", JsonUtility.ToJson(response), origin);
        }

        private static void SendError(NetworkStream stream, string message,
            int statusCode = 500, string statusText = "Internal Server Error", string origin = "")
        {
            var response = new ErrorResponse { status = "error", message = message };
            SendHttpResponse(stream, statusCode, statusText, JsonUtility.ToJson(response), origin);
        }

        // ----------------------------------------------------------------
        // HTTP helpers
        // ----------------------------------------------------------------

        private static void SendHttpResponse(NetworkStream stream, int statusCode,
            string statusText, string jsonBody, string origin)
        {
            string allowedOrigin = GetAllowedOrigin(origin);

            var sb = new StringBuilder();
            sb.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
            sb.Append($"Access-Control-Allow-Origin: {allowedOrigin}\r\n");
            sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
            sb.Append("Access-Control-Allow-Private-Network: true\r\n");
            sb.Append("Access-Control-Max-Age: 86400\r\n");
            sb.Append("Connection: close\r\n");

            if (!string.IsNullOrEmpty(jsonBody))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                sb.Append("Content-Type: application/json; charset=utf-8\r\n");
                sb.Append($"Content-Length: {bodyBytes.Length}\r\n");
                sb.Append("\r\n");

                byte[] headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }
            else
            {
                sb.Append("Content-Length: 0\r\n");
                sb.Append("\r\n");

                byte[] headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
                stream.Write(headerBytes, 0, headerBytes.Length);
            }
        }

        private static string GetAllowedOrigin(string origin)
        {
            if (!string.IsNullOrEmpty(origin) && origin.StartsWith("http://localhost"))
                return origin;

            return "*";
        }

        private static string GetHeaderValue(string[] lines, string headerName)
        {
            string prefix = headerName + ":";
            foreach (string line in lines)
            {
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(prefix.Length).Trim();
            }
            return "";
        }

        // ----------------------------------------------------------------
        // Download & validation
        // ----------------------------------------------------------------

        private static string DownloadFile(string url, string assetName, string fmt)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string guid = Guid.NewGuid().ToString("N").Substring(0, 8);
            string ext = fmt == "usdz" ? "usdz" : "zip";
            string fileName = $"varco3d_{timestamp}_{guid}.{ext}";
            string dirPath = Path.Combine(Path.GetTempPath(), "VARCO3D");
            Directory.CreateDirectory(dirPath);
            string filePath = Path.Combine(dirPath, fileName);

            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "VARCO3D-Unity/1.0";
                client.DownloadFile(url, filePath);
            }

            return filePath;
        }

        /// <summary>Pick "usdz" or "zip" from the URL extension. Matches the
        /// dispatch convention used by Blender / Maya / 3ds Max / Unreal.</summary>
        private static string DetectFormat(string url)
        {
            try
            {
                string path = new Uri(url).AbsolutePath.ToLowerInvariant();
                return path.EndsWith(".usdz") ? "usdz" : "zip";
            }
            catch
            {
                // Malformed URL — fall back to legacy ZIP path
                return "zip";
            }
        }

        private static bool ValidateZipMagic(string filePath)
        {
            byte[] header = new byte[4];
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                if (fs.Read(header, 0, 4) < 4) return false;
            }
            // ZIP magic: PK\x03\x04
            return header[0] == 0x50 && header[1] == 0x4B &&
                   header[2] == 0x03 && header[3] == 0x04;
        }
    }
}
