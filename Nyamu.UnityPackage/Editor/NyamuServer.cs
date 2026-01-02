//
// NyamuServer.cs - Nyamu MCP (Model Context Protocol) Server
//
// This file implements an HTTP server that enables external tools to interact with Unity Editor
// for compilation and test execution via MCP protocol. The server runs on a background thread
// and provides REST API endpoints for:
//
// - Script compilation triggering and status monitoring
// - PlayMode and EditMode test execution
// - Real-time compilation error reporting
// - Test result collection with detailed status
//
// Key features:
// - Automatic domain reload handling via [InitializeOnLoad]
// - PlayMode test execution without domain reload (preserves server state)
// - Thread-safe communication between background HTTP server and Unity main thread
// - Graceful shutdown on Unity exit or domain reload
//
// Note: Currently, PlayMode tests work by temporarily modifying Enter Play Mode settings
// to disable domain reload, which prevents server state loss. However, this approach
// may not be ideal and should potentially be replaced with a more robust solution that
// doesn't rely on changing Unity's global editor settings.
//

using UnityEngine;
using UnityEditor;
using System.Net;
using System.Threading;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System;
using Nyamu.Core;
using Nyamu.Core.Monitors;
using Nyamu.Core.StateManagers;
using Nyamu.Tools.Compilation;
using Nyamu.Tools.Testing;
using Nyamu.Tools.Shaders;
using Nyamu.Tools.Editor;
using Nyamu.Tools.Settings;
using Nyamu.Tools.Assets;
using Nyamu.Tools.Editor.PlayMode;
using Nyamu.TestExecution;

namespace Nyamu
{
    // ============================================================================
    // CONFIGURATION AND CONSTANTS
    // ============================================================================

    // Configuration constants for the Nyamu MCP server
    static class Constants
    {
        public const int CompileTimeoutSeconds = 5;
        public const int ThreadSleepMilliseconds = 50;
        public const int ThreadJoinTimeoutMilliseconds = 1000;

        public static class Endpoints
        {
            public const string AssetsRefresh = "/assets-refresh";
            public const string AssetsRefreshStatus = "/assets-refresh-status";
            public const string EditorExitPlayMode = "/editor-exit-play-mode";
            public const string EditorStatus = "/editor-status";
            public const string InternalMcpSettings = "/internal-mcp-settings";
            public const string MenuItemsExecute = "/menu-items-execute";
            public const string ScriptsCompile = "/scripts-compile";
            public const string ScriptsCompileStatus = "/scripts-compile-status";
            public const string ShadersCompileAll = "/shaders-compile-all";
            public const string ShadersCompileRegex = "/shaders-compile-regex";
            public const string ShadersCompileSingle = "/shaders-compile-single";
            public const string ShadersCompileStatus = "/shaders-compile-status";
            public const string TestsRunAll = "/tests-run-all";
            public const string TestsRunCancel = "/tests-run-cancel";
            public const string TestsRunRegex = "/tests-run-regex";
            public const string TestsRunSingle = "/tests-run-single";
            public const string TestsRunStatus = "/tests-run-status";
        }
    }

    // ============================================================================
    // MAIN HTTP SERVER CLASS
    // ============================================================================
    // Handles HTTP server lifecycle, request routing, and Unity integration

    [InitializeOnLoad]
    public static class Server
    {
        // ========================================================================
        // STATE VARIABLES
        // ========================================================================

        // SessionState keys for detecting fresh editor start vs domain reload
        internal const string SESSION_KEY_EDITOR_RUNNING = "Nyamu_EditorRunning";
        internal const string SESSION_KEY_REFRESH_REQUEST_TIME = "Nyamu_RefreshRequestTime";
        internal const string SESSION_KEY_REFRESH_COMPLETED_TIME = "Nyamu_RefreshCompletedTime";

        // HTTP server components
        static HttpListener _listener;
        static Thread _thread;
        static ManualResetEvent _listenerReady;

        // Shutdown coordination
        static volatile bool _shouldStop;

        // Infrastructure components for refactored architecture
        static CompilationStateManager _compilationStateManager;
        static TestStateManager _testStateManager;
        static ShaderStateManager _shaderStateManager;
        static AssetStateManager _assetStateManager;
        static EditorStateManager _editorStateManager;
        static SettingsStateManager _settingsStateManager;
        static UnityThreadExecutor _unityThreadExecutor;
        static Core.ExecutionContext _executionContext;

        // Monitors and services
        static CompilationMonitor _compilationMonitor;
        static EditorMonitor _editorMonitor;
        static SettingsMonitor _settingsMonitor;
        static TestExecutionService _testExecutionService;
        static TestCallbacks _testCallbacks;

        // Tools (Step 2-3: read-only tools)
        static CompilationStatusTool _compilationStatusTool;
        static TestsStatusTool _testsStatusTool;
        static ShaderCompilationStatusTool _shaderCompilationStatusTool;
        static EditorStatusTool _editorStatusTool;
        static McpSettingsTool _mcpSettingsTool;

        // Tools (Step 4 Group A: simple write tools)
        static CompilationTriggerTool _compilationTriggerTool;
        static AssetsRefreshTool _assetsRefreshTool;
        static ExecuteMenuItemTool _executeMenuItemTool;
        static EditorExitPlayModeTool _editorExitPlayModeTool;

        // Step 4 Group B: test tools
        static TestsRunSingleTool _testsRunSingleTool;
        static TestsRunAllTool _testsRunAllTool;
        static TestsRunRegexTool _testsRunRegexTool;
        static TestsCancelTool _testsCancelTool;

        // Step 4 Group C: shader tools
        static CompileShaderTool _compileShaderTool;
        static CompileAllShadersTool _compileAllShadersTool;
        static CompileShadersRegexTool _compileShadersRegexTool;

        static Server()
        {
            Initialize();
        }

        static void Initialize()
        {
            Cleanup();

            // Initialize infrastructure components first (creates state managers and monitors)
            InitializeInfrastructure();

            // Load cached timestamps after infrastructure is ready
            LoadTimestampsCache();

            // Detect if domain reload occurred after a pending refresh
            DetectRefreshCompletion();

            _shouldStop = false;
            _listenerReady = new ManualResetEvent(false);

            // Try to start HTTP listener with retry logic for port release delays
            const int maxRetries = 3;
            const int retryDelayMs = 300;
            var port = NyamuSettings.Instance.serverPort;
            var success = false;

            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{port}/");
                    _listener.Start();
                    success = true;
                    if (attempt > 0)
                        NyamuLogger.LogInfo($"[Nyamu][Server] Server started on port {port} after {attempt + 1} attempt(s)");
                    break;
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 48 || ex.ErrorCode == 32 || ex.Message.Contains("already in use") || ex.Message.Contains("normally permitted"))
                {
                    // Port still in use (likely TIME_WAIT state after domain reload)
                    try
                    {
                        _listener?.Close();
                    }
                    catch { }
                    _listener = null;

                    if (attempt < maxRetries - 1)
                    {
                        NyamuLogger.LogDebug($"[Nyamu][Server] Port {port} temporarily unavailable, retrying in {retryDelayMs}ms (attempt {attempt + 1}/{maxRetries})");
                        System.Threading.Thread.Sleep(retryDelayMs);
                    }
                    else
                    {
                        NyamuLogger.LogError($"[Nyamu][Server] Port {port} remains in use after {maxRetries} attempts: {ex.Message}. " +
                            "This may happen if another Unity Editor instance is using this port. Please check Project Settings > Nyamu to change the port.");
                    }
                }
                catch (Exception ex)
                {
                    NyamuLogger.LogError($"[Nyamu][Server] Unexpected error starting HTTP listener: {ex.Message}");
                    try
                    {
                        _listener?.Close();
                    }
                    catch { }
                    _listener = null;
                    break;
                }
            }

            if (!success)
            {
                NyamuLogger.LogError("[Nyamu][Server] Failed to start Nyamu MCP server. MCP integration will not be available.");
                return;
            }

            _thread = new(HttpRequestProcessor);
            _thread.IsBackground = true;
            _thread.Start();

            EditorApplication.quitting += Cleanup;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        static void InitializeInfrastructure()
        {
            // Create state managers
            _compilationStateManager = new CompilationStateManager();
            _testStateManager = new TestStateManager();
            _shaderStateManager = new ShaderStateManager();
            _assetStateManager = new AssetStateManager();
            _editorStateManager = new EditorStateManager();
            _settingsStateManager = new SettingsStateManager();

            // Create Unity thread executor (owns the main thread action queue)
            _unityThreadExecutor = new UnityThreadExecutor();

            // Create monitors
            _compilationMonitor = new CompilationMonitor(_compilationStateManager);
            _settingsMonitor = new SettingsMonitor(_settingsStateManager);
            _editorMonitor = new EditorMonitor(_editorStateManager, _unityThreadExecutor, _settingsMonitor);

            // Create test infrastructure
            _testCallbacks = new TestCallbacks(_testStateManager, _compilationMonitor.TimestampLock);
            _testExecutionService = new TestExecutionService(_testStateManager, _assetStateManager, _testCallbacks);
            _testStateManager.TestCallbacks = _testCallbacks;

            // Initialize monitors (subscribe to Unity events)
            _compilationMonitor.Initialize();
            _editorMonitor.Initialize();

            // Initialize logger's cached min log level to avoid thread-safety issues
            NyamuLogger.RefreshMinLogLevel();

            // Create execution context with monitors and services
            _executionContext = new Core.ExecutionContext(
                _unityThreadExecutor,
                _compilationStateManager,
                _testStateManager,
                _shaderStateManager,
                _assetStateManager,
                _editorStateManager,
                _settingsStateManager,
                _compilationMonitor,
                _editorMonitor,
                _settingsMonitor,
                _testExecutionService
            );

            // Create tools (Step 2-3: read-only tools)
            _compilationStatusTool = new CompilationStatusTool();
            _testsStatusTool = new TestsStatusTool();
            _shaderCompilationStatusTool = new ShaderCompilationStatusTool();
            _editorStatusTool = new EditorStatusTool();
            _mcpSettingsTool = new McpSettingsTool();

            // Create tools (Step 4 Group A: simple write tools)
            _compilationTriggerTool = new CompilationTriggerTool();
            _assetsRefreshTool = new AssetsRefreshTool();
            _executeMenuItemTool = new ExecuteMenuItemTool();
            _editorExitPlayModeTool = new EditorExitPlayModeTool();

            // Create tools (Step 4 Group B: test tools)
            _testsRunSingleTool = new TestsRunSingleTool();
            _testsRunAllTool = new TestsRunAllTool();
            _testsRunRegexTool = new TestsRunRegexTool();
            _testsCancelTool = new TestsCancelTool();

            // Create tools (Step 4 Group C: shader tools)
            _compileShaderTool = new CompileShaderTool();
            _compileAllShadersTool = new CompileAllShadersTool();
            _compileShadersRegexTool = new CompileShadersRegexTool();
        }

        static void Cleanup()
        {
            _shouldStop = true;

            // Cleanup monitors (unsubscribe from Unity events)
            _compilationMonitor?.Cleanup();
            _editorMonitor?.Cleanup();

            // Save timestamps before shutdown/domain reload
            SaveTimestampsCache();

            // Properly close HttpListener to release port
            if (_listener != null)
            {
                try
                {
                    if (_listener.IsListening)
                        _listener.Stop();
                    _listener.Close();
                }
                catch { }
                finally
                {
                    _listener = null;
                }
            }

            // Dispose ManualResetEvent
            try
            {
                _listenerReady?.Set(); // Unblock any waiting threads
                _listenerReady?.Dispose();
                _listenerReady = null;
            }
            catch { }

            if (_thread?.IsAlive == true)
            {
                if (!_thread.Join(Constants.ThreadJoinTimeoutMilliseconds))
                {
                    NyamuLogger.LogWarning("[Nyamu][Server] HTTP thread did not stop gracefully");
                }
            }
        }

        // Public method to restart server (e.g., when port changes)
        public static void Restart()
        {
            NyamuLogger.LogInfo("[Nyamu][Server] Restarting server...");
            Initialize();
            NyamuLogger.LogInfo($"[Nyamu][Server] Server restarted on port {NyamuSettings.Instance.serverPort}");
        }

        // ========================================================================
        // HTTP SERVER INFRASTRUCTURE
        // ========================================================================

        static void ProcessRequestCallback(IAsyncResult result)
        {
            try
            {
                var listener = (HttpListener)result.AsyncState;
                if (!listener.IsListening)
                {
                    _listenerReady.Set();
                    return;
                }

                var context = listener.EndGetContext(result);

                // Signal that we're ready for the next request
                _listenerReady.Set();

                // Process this request in ThreadPool (multi-threaded)
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        ProcessHttpRequest(context);
                    }
                    catch (Exception ex)
                    {
                        HandleHttpException(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                HandleHttpException(ex);
                _listenerReady.Set(); // Unblock even on error
            }
        }

        static void HttpRequestProcessor()
        {
            while (!_shouldStop && _listener?.IsListening == true && _listenerReady != null)
            {
                try
                {
                    _listenerReady.Reset();
                    _listener.BeginGetContext(ProcessRequestCallback, _listener);
                    _listenerReady.WaitOne(); // Wait for callback to be ready for next request
                }
                catch (Exception ex)
                {
                    HandleHttpException(ex);
                }
            }
        }

        static void ProcessHttpRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            SetupResponseHeaders(response);

            var responseString = RouteRequest(request, response);
            SendResponse(response, responseString);
        }

        static void SetupResponseHeaders(HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        }

        static string RouteRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            return request.Url.AbsolutePath switch
            {
                Constants.Endpoints.ScriptsCompile => HandleCompileAndWaitRequest(),
                Constants.Endpoints.ScriptsCompileStatus => HandleCompileStatusRequest(),
                Constants.Endpoints.TestsRunSingle => HandleTestsRunSingleRequest(request),
                Constants.Endpoints.TestsRunAll => HandleTestsRunAllRequest(request),
                Constants.Endpoints.TestsRunRegex => HandleTestsRunRegexRequest(request),
                Constants.Endpoints.TestsRunStatus => HandleTestsStatusRequest(),
                Constants.Endpoints.AssetsRefresh => HandleAssetsRefreshRequest(request),
                Constants.Endpoints.AssetsRefreshStatus => HandleAssetsRefreshStatusRequest(request),
                Constants.Endpoints.EditorStatus => HandleEditorStatusRequest(),
                Constants.Endpoints.InternalMcpSettings => HandleMcpSettingsRequest(),
                Constants.Endpoints.TestsRunCancel => HandleTestsCancelRequest(request),
                Constants.Endpoints.ShadersCompileSingle => HandleCompileShaderRequest(request),
                Constants.Endpoints.ShadersCompileAll => HandleCompileAllShadersRequest(request),
                Constants.Endpoints.ShadersCompileRegex => HandleCompileShadersRegexRequest(request),
                Constants.Endpoints.ShadersCompileStatus => HandleShaderCompilationStatusRequest(request),
                Constants.Endpoints.MenuItemsExecute => HandleExecuteMenuItemRequest(request),
                Constants.Endpoints.EditorExitPlayMode => HandleEditorExitPlayModeRequest(request),
                _ => HandleNotFoundRequest(response)
            };
        }

        static string HandleCompileAndWaitRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleCompileAndWaitRequest");

            // Use new tool architecture
            var request = new CompilationTriggerRequest();
            var response = _compilationTriggerTool.ExecuteAsync(request, _executionContext).Result;

            return JsonUtility.ToJson(response);
        }

        // Made public for CompilationTriggerTool
        public static (bool success, string message) WaitForCompilationToStart(DateTime requestTime, TimeSpan timeout)
        {
            var waitStart = DateTime.Now;

            // First, wait for asset refresh to complete if it's in progress
            while ((DateTime.Now - waitStart) < timeout)
            {
                // Check both our flag and Unity's cached refresh state (thread-safe)
                bool refreshInProgress, unityIsUpdating;
                lock (_assetStateManager.Lock)
                {
                    refreshInProgress = _assetStateManager.IsRefreshing;
                    unityIsUpdating = _assetStateManager.UnityIsUpdating;
                }

                if (!refreshInProgress && !unityIsUpdating)
                    break; // Asset refresh is complete

                Thread.Sleep(Constants.ThreadSleepMilliseconds);
            }

            // If we timed out waiting for refresh, return failure
            if ((DateTime.Now - waitStart) >= timeout)
                return (false, "Timed out waiting for asset refresh to complete.");

            // Now wait for compilation to start
            while ((DateTime.Now - waitStart) < timeout)
            {
                bool isCompiling;
                lock (_compilationStateManager.Lock)
                {
                    isCompiling = _compilationStateManager.IsCompiling;
                }

                if (isCompiling || EditorApplication.isCompiling)
                    return (true, "Compilation started.");

                DateTime lastCompileTimeCopy;
                lock (_compilationMonitor.TimestampLock)
                {
                    lastCompileTimeCopy = _compilationStateManager.LastCompileTime;
                }

                if (lastCompileTimeCopy > requestTime)
                    return (true, "Compilation completed quickly.");

                Thread.Sleep(Constants.ThreadSleepMilliseconds);
            }

            return (false, "Compilation may not have started.");
        }

        static string HandleCompileStatusRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleCompileStatusRequest");

            // Use new tool architecture
            var request = new CompilationStatusRequest();
            var response = _compilationStatusTool.ExecuteAsync(request, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string ExtractQueryParameter(string query, string paramName)
        {
            if (!query.Contains($"{paramName}="))
                return null;

            var paramStart = query.IndexOf($"{paramName}=") + paramName.Length + 1;
            var paramEnd = query.IndexOf("&", paramStart);
            var value = paramEnd == -1 ? query.Substring(paramStart) : query.Substring(paramStart, paramEnd - paramStart);
            return Uri.UnescapeDataString(value);
        }

        static string HandleEditorStatusRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleEditorStatusRequest");

            // Use new tool architecture
            var request = new EditorStatusRequest();
            var response = _editorStatusTool.ExecuteAsync(request, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleMcpSettingsRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleMcpSettingsRequest");

            // Use new tool architecture (tool handles caching internally)
            var request = new McpSettingsRequest();
            var response = _mcpSettingsTool.ExecuteAsync(request, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleTestsRunSingleRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsRunSingleRequest");

            TestsRunSingleRequest toolRequest = null;

            // Try to read request body first (for async/timeout params)
            if (request.HttpMethod == "POST" && request.ContentLength64 > 0)
            {
                try
                {
                    var bodyText = new StreamReader(request.InputStream).ReadToEnd();
                    toolRequest = JsonUtility.FromJson<TestsRunSingleRequest>(bodyText);
                }
                catch { }
            }

            // Fallback to query parameters if body not available
            if (toolRequest == null)
            {
                var query = request.Url.Query ?? "";
                var testName = ExtractQueryParameter(query, "test_name");
                var mode = ExtractQueryParameter(query, "mode") ?? "EditMode";

                toolRequest = new TestsRunSingleRequest
                {
                    testName = testName,
                    testMode = mode
                };
            }

            var response = _testsRunSingleTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleTestsRunAllRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsRunAllRequest");

            TestsRunAllRequest toolRequest = null;

            // Try to read request body first (for async/timeout params)
            if (request.HttpMethod == "POST" && request.ContentLength64 > 0)
            {
                try
                {
                    var bodyText = new StreamReader(request.InputStream).ReadToEnd();
                    toolRequest = JsonUtility.FromJson<TestsRunAllRequest>(bodyText);
                }
                catch { }
            }

            // Fallback to query parameters if body not available
            if (toolRequest == null)
            {
                var query = request.Url.Query ?? "";
                var mode = ExtractQueryParameter(query, "mode") ?? "EditMode";

                toolRequest = new TestsRunAllRequest
                {
                    testMode = mode
                };
            }

            var response = _testsRunAllTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleTestsRunRegexRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsRunRegexRequest");

            TestsRunRegexRequest toolRequest = null;

            // Try to read request body first (for async/timeout params)
            if (request.HttpMethod == "POST" && request.ContentLength64 > 0)
            {
                try
                {
                    var bodyText = new StreamReader(request.InputStream).ReadToEnd();
                    toolRequest = JsonUtility.FromJson<TestsRunRegexRequest>(bodyText);
                }
                catch { }
            }

            // Fallback to query parameters if body not available
            if (toolRequest == null)
            {
                var query = request.Url.Query ?? "";
                var filterRegex = ExtractQueryParameter(query, "filter_regex");
                var mode = ExtractQueryParameter(query, "mode") ?? "EditMode";

                toolRequest = new TestsRunRegexRequest
                {
                    testFilterRegex = filterRegex,
                    testMode = mode
                };
            }

            var response = _testsRunRegexTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleTestsStatusRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsStatusRequest");

            // Use new tool architecture (no sync needed - read-only operation)
            var toolRequest = new TestsStatusRequest();
            var response = _testsStatusTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleTestsCancelRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsCancelRequest");

            var query = request.Url.Query ?? "";
            var testRunGuid = ExtractQueryParameter(query, "guid");

            var toolRequest = new TestsCancelRequest
            {
                testRunGuid = testRunGuid
            };

            var response = _testsCancelTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        // Helper method for ShaderCompilationService to update progress tracking
        public static void UpdateRegexShadersProgress(string pattern, int total, int completed, string currentShader)
        {
            lock (_shaderStateManager.Lock)
            {
                _shaderStateManager.RegexShadersPattern = pattern;
                _shaderStateManager.RegexShadersTotal = total;
                _shaderStateManager.RegexShadersCompleted = completed;
                _shaderStateManager.RegexShadersCurrentShader = currentShader;
            }
        }

        // Helper method for ShaderCompilationService to update all shaders progress tracking
        public static void UpdateAllShadersProgress(int total, int completed, string currentShader)
        {
            lock (_shaderStateManager.Lock)
            {
                _shaderStateManager.AllShadersTotal = total;
                _shaderStateManager.AllShadersCompleted = completed;
                _shaderStateManager.AllShadersCurrentShader = currentShader;
            }
        }


        static string HandleCompileShaderRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileShaderRequest");

            CompileShaderRequest toolRequest = null;
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    var body = reader.ReadToEnd();
                    // Parse as old format first, then convert to new
                    var oldRequest = JsonUtility.FromJson<CompileShaderRequest>(body);
                    if (oldRequest != null)
                    {
                        toolRequest = new CompileShaderRequest
                        {
                            shaderName = oldRequest.shaderName,
                            timeout = 30
                        };
                    }
                }
            }
            catch
            {
                return "{\"status\":\"error\",\"message\":\"Invalid request body.\"}";
            }

            if (toolRequest == null)
                toolRequest = new CompileShaderRequest { timeout = 30 };

            var response = _compileShaderTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleCompileAllShadersRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileAllShadersRequest");
            if (request.HttpMethod != "POST")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use POST.\"}";

            CompileAllShadersRequest toolRequest = null;
            try
            {
                var bodyText = new StreamReader(request.InputStream).ReadToEnd();
                toolRequest = JsonUtility.FromJson<CompileAllShadersRequest>(bodyText);
            }
            catch
            {
                return "{\"status\":\"error\",\"message\":\"Invalid request body.\"}";
            }

            if (toolRequest == null)
                toolRequest = new CompileAllShadersRequest { timeout = 120 };

            var response = _compileAllShadersTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleCompileShadersRegexRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileShadersRegexRequest");
            if (request.HttpMethod != "POST")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use POST.\"}";

            CompileShadersRegexToolRequest toolRequest = null;
            try
            {
                var bodyText = new StreamReader(request.InputStream).ReadToEnd();
                toolRequest = JsonUtility.FromJson<CompileShadersRegexToolRequest>(bodyText);
            }
            catch
            {
                return "{\"status\":\"error\",\"message\":\"Invalid request body.\"}";
            }

            if (toolRequest == null)
                toolRequest = new CompileShadersRegexToolRequest { timeout = 120 };

            var response = _compileShadersRegexTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleShaderCompilationStatusRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleShaderCompilationStatusRequest");
            if (request.HttpMethod != "GET")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use GET.\"}";

            // Use new tool architecture (no sync needed - read-only operation)
            var toolRequest = new ShaderCompilationStatusRequest();
            var response = _shaderCompilationStatusTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleExecuteMenuItemRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleExecuteMenuItemRequest");

            if (request.HttpMethod != "GET")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use GET.\"}";

            var menuItemPath = request.QueryString["menuItemPath"];

            // Use new tool architecture
            var toolRequest = new ExecuteMenuItemRequest { menuItemPath = menuItemPath };
            var response = _executeMenuItemTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleEditorExitPlayModeRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleEditorExitPlayModeRequest");

            if (request.HttpMethod != "GET")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use GET.\"}";

            // Use new tool architecture
            var toolRequest = new EditorExitPlayModeRequest();
            var response = _editorExitPlayModeTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleNotFoundRequest(HttpListenerResponse response)
        {
            response.StatusCode = 404;
            return "{\"status\":\"error\",\"message\":\"Endpoint not found\"}";
        }

        static void SendResponse(HttpListenerResponse response, string content)
        {
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        static void LoadTimestampsCache()
        {
            try
            {
                // Skip if infrastructure not yet initialized
                if (_compilationMonitor == null || _compilationStateManager == null || _testStateManager == null)
                    return;

                var cache = NyamuServerCache.Load();
                lock (_compilationMonitor.TimestampLock)
                {
                    _compilationStateManager.LastCompileTime = ParseDateTime(cache.lastCompilationTime);
                    _compilationStateManager.CompileRequestTime = ParseDateTime(cache.lastCompilationRequestTime);
                    _testStateManager.LastTestTime = ParseDateTime(cache.lastTestRunTime);
                }

                // Detect if this is a fresh editor start or domain reload
                bool isEditorRunning = SessionState.GetBool(SESSION_KEY_EDITOR_RUNNING, false);

                if (!isEditorRunning)
                {
                    // Fresh editor start - set the flag for subsequent domain reloads
                    SessionState.SetBool(SESSION_KEY_EDITOR_RUNNING, true);
                    NyamuLogger.LogDebug("[Nyamu][Server] Fresh Unity Editor start detected. Clearing any stale refresh state.");

                    // Clear refresh state in AssetStateManager
                    _assetStateManager.RefreshRequestTime = DateTime.MinValue;
                    _assetStateManager.RefreshCompletedTime = DateTime.MinValue;
                    _assetStateManager.IsRefreshing = false;
                    _assetStateManager.IsWaitingForCompilation = false;

                    // Clear SessionState refresh timestamps
                    SessionState.EraseString(SESSION_KEY_REFRESH_REQUEST_TIME);
                    SessionState.EraseString(SESSION_KEY_REFRESH_COMPLETED_TIME);
                }
                else
                {
                    // Domain reload within same editor session - restore refresh state from SessionState
                    NyamuLogger.LogDebug("[Nyamu][Server] Domain reload detected. Restoring refresh state from SessionState.");

                    var refreshRequestStr = SessionState.GetString(SESSION_KEY_REFRESH_REQUEST_TIME, "");
                    var refreshCompletedStr = SessionState.GetString(SESSION_KEY_REFRESH_COMPLETED_TIME, "");

                    _assetStateManager.RefreshRequestTime = ParseDateTime(refreshRequestStr);
                    _assetStateManager.RefreshCompletedTime = ParseDateTime(refreshCompletedStr);

                    // Don't restore IsRefreshing/IsWaitingForCompilation flags - they'll be detected in DetectRefreshCompletion()
                }

                NyamuLogger.LogDebug($"[Nyamu][Server] Restored timestamps from cache - " +
                         $"LastCompile: {_compilationStateManager.LastCompileTime:yyyy-MM-dd HH:mm:ss}, " +
                         $"CompileRequest: {_compilationStateManager.CompileRequestTime:yyyy-MM-dd HH:mm:ss}, " +
                         $"LastTest: {_testStateManager.LastTestTime:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Failed to load timestamp cache: {ex.Message}");
            }
        }

        static void DetectRefreshCompletion()
        {
            if (_assetStateManager == null) return;

            var refreshRequest = _assetStateManager.RefreshRequestTime;
            var refreshCompleted = _assetStateManager.RefreshCompletedTime;

            // Check if there's a pending refresh (requested but not yet marked complete)
            if (refreshRequest > refreshCompleted && refreshRequest > DateTime.MinValue)
            {
                // Check if request is recent (within 60 seconds) - protects against clock issues
                var age = DateTime.Now - refreshRequest;
                if (age.TotalSeconds < 60)
                {
                    // Domain reload occurred within current session, mark refresh as completed
                    _assetStateManager.RefreshCompletedTime = DateTime.Now;
                    _assetStateManager.IsRefreshing = false;
                    _assetStateManager.IsWaitingForCompilation = false;

                    // Update SessionState
                    SessionState.SetString(SESSION_KEY_REFRESH_COMPLETED_TIME, DateTime.Now.ToString("o"));

                    NyamuLogger.LogDebug($"[Nyamu][Server] Detected domain reload after refresh. Age: {age.TotalSeconds:F1}s");
                }
                else
                {
                    // Too old, even within session - clear it
                    NyamuLogger.LogDebug($"[Nyamu][Server] Refresh request too old ({age.TotalSeconds:F1}s), clearing");
                    _assetStateManager.RefreshRequestTime = DateTime.MinValue;
                    _assetStateManager.RefreshCompletedTime = DateTime.MinValue;
                    _assetStateManager.IsRefreshing = false;
                    _assetStateManager.IsWaitingForCompilation = false;

                    // Clear SessionState
                    SessionState.EraseString(SESSION_KEY_REFRESH_REQUEST_TIME);
                    SessionState.EraseString(SESSION_KEY_REFRESH_COMPLETED_TIME);
                }
            }
        }

        internal static void SaveTimestampsCache()
        {
            try
            {
                // Skip if infrastructure not yet initialized
                if (_compilationMonitor == null || _compilationStateManager == null || _testStateManager == null)
                    return;

                lock (_compilationMonitor.TimestampLock)
                {
                    var cache = new NyamuServerCache
                    {
                        lastCompilationTime = _compilationStateManager.LastCompileTime.ToString("o"),
                        lastCompilationRequestTime = _compilationStateManager.CompileRequestTime.ToString("o"),
                        lastTestRunTime = _testStateManager.LastTestTime.ToString("o")
                    };
                    NyamuServerCache.Save(cache);
                }
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Failed to save timestamp cache: {ex.Message}");
            }
        }

        static DateTime ParseDateTime(string str)
        {
            if (string.IsNullOrEmpty(str))
                return DateTime.MinValue;

            try
            {
                return DateTime.Parse(str);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        // Helper for RefreshAssetsTool to start refresh monitoring
        public static void StartRefreshMonitoring(AssetStateManager state)
        {
            lock (state.Lock)
            {
                if (!state.IsMonitoringRefresh)
                {
                    state.IsMonitoringRefresh = true;
                    state.UnityIsUpdating = true;  // Assume refresh is starting
                    EditorApplication.update += MonitorRefreshCompletion;
                }
            }
        }

        static void MonitorRefreshCompletion()
        {
            // Update cached state (this runs on main thread)
            bool unityIsUpdating = EditorApplication.isUpdating;
            bool isCompiling = EditorApplication.isCompiling;

            // Update state manager
            lock (_assetStateManager.Lock)
            {
                _assetStateManager.UnityIsUpdating = unityIsUpdating;
            }

            // Phase 1: Wait for asset refresh to complete
            if (unityIsUpdating)
                return;  // Still refreshing assets

            // Phase 2: After refresh completes, check if compilation started
            bool waitingForCompilation;
            lock (_assetStateManager.Lock)
            {
                waitingForCompilation = _assetStateManager.IsWaitingForCompilation;
            }

            if (!waitingForCompilation)
            {
                // First time asset refresh completed - check if compilation is starting
                if (isCompiling || _compilationStateManager.IsCompiling)
                {
                    // Compilation triggered by refresh
                    lock (_assetStateManager.Lock)
                    {
                        _assetStateManager.IsWaitingForCompilation = true;
                    }
                    NyamuLogger.LogDebug("[Nyamu][Server] Asset refresh completed, compilation detected");
                    return;  // Keep monitoring for compilation completion
                }
            }

            // Phase 3: If waiting for compilation, check if it completed
            if (waitingForCompilation)
            {
                if (!isCompiling && !_compilationStateManager.IsCompiling)
                {
                    // Compilation finished, domain reload will occur soon
                    // Mark as completed - domain reload detection will happen on next Initialize()
                    lock (_assetStateManager.Lock)
                    {
                        _assetStateManager.RefreshCompletedTime = DateTime.Now;
                        _assetStateManager.IsRefreshing = false;
                        _assetStateManager.IsWaitingForCompilation = false;
                        _assetStateManager.IsMonitoringRefresh = false;
                    }

                    // Update SessionState
                    SessionState.SetString(SESSION_KEY_REFRESH_COMPLETED_TIME, DateTime.Now.ToString("o"));

                    EditorApplication.update -= MonitorRefreshCompletion;
                    NyamuLogger.LogDebug("[Nyamu][Server] Refresh chain completed (with compilation)");
                    return;
                }
            }
            else
            {
                // No compilation after reasonable wait, mark as completed
                var timeSinceNotUpdating = DateTime.Now - _assetStateManager.RefreshRequestTime;
                if (timeSinceNotUpdating.TotalSeconds > 1.0)  // Wait 1 second
                {
                    lock (_assetStateManager.Lock)
                    {
                        _assetStateManager.RefreshCompletedTime = DateTime.Now;
                        _assetStateManager.IsRefreshing = false;
                        _assetStateManager.IsMonitoringRefresh = false;
                    }

                    // Update SessionState
                    SessionState.SetString(SESSION_KEY_REFRESH_COMPLETED_TIME, DateTime.Now.ToString("o"));

                    EditorApplication.update -= MonitorRefreshCompletion;
                    NyamuLogger.LogDebug("[Nyamu][Server] Refresh completed (no compilation)");
                }
            }
        }

        static string HandleAssetsRefreshRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleAssetsRefreshRequest");

            // Parse force parameter from query string
            bool force = request.Url.Query.Contains("force=true");

            // Use new tool architecture
            var toolRequest = new AssetsRefreshRequest { force = force };
            var response = _assetsRefreshTool.ExecuteAsync(toolRequest, _executionContext).Result;

            return JsonUtility.ToJson(response);
        }

        static string HandleAssetsRefreshStatusRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleAssetsRefreshStatusRequest");

            bool isRefreshing, isWaitingForCompilation, unityIsUpdating;
            DateTime refreshRequest, refreshCompleted;

            lock (_assetStateManager.Lock)
            {
                isRefreshing = _assetStateManager.IsRefreshing;
                isWaitingForCompilation = _assetStateManager.IsWaitingForCompilation;
                unityIsUpdating = _assetStateManager.UnityIsUpdating;
                refreshRequest = _assetStateManager.RefreshRequestTime;
                refreshCompleted = _assetStateManager.RefreshCompletedTime;
            }

            bool isCompiling = _compilationStateManager.IsCompiling;

            // Determine status
            string status;
            if (!isRefreshing && refreshCompleted > refreshRequest)
                status = "completed";
            else if (unityIsUpdating)
                status = "refreshing";
            else if (isCompiling || isWaitingForCompilation)
                status = "compiling";
            else if (isRefreshing)
                status = "waiting";  // Between refresh and compilation detection
            else
                status = "idle";

            // Always get last compilation status (regardless of when it occurred)
            bool hadCompilation = false;
            CompileError[] compilationErrors;
            DateTime lastCompileTime;

            lock (_compilationStateManager.Lock)
            {
                // Always get current compilation errors and time
                compilationErrors = _compilationStateManager.GetErrorsSnapshot();
                lastCompileTime = _compilationStateManager.LastCompileTime;
            }

            // Determine if compilation occurred during THIS refresh
            if (refreshRequest > DateTime.MinValue &&
                refreshCompleted > refreshRequest &&
                lastCompileTime > refreshRequest)
            {
                hadCompilation = true;
            }

            var response = new AssetsRefreshStatusResponse
            {
                isRefreshing = isRefreshing,
                isCompiling = isCompiling,
                isWaitingForCompilation = isWaitingForCompilation,
                unityIsUpdating = unityIsUpdating,
                status = status,
                refreshRequestTime = refreshRequest.ToString("o"),
                refreshCompletedTime = refreshCompleted > DateTime.MinValue ? refreshCompleted.ToString("o") : null,

                // Add compilation report (always includes last compilation state)
                hadCompilation = hadCompilation,
                compilationErrors = compilationErrors,
                lastCompilationTime = lastCompileTime > DateTime.MinValue ? lastCompileTime.ToString("o") : null
            };

            return JsonUtility.ToJson(response);
        }

        static void HandleHttpException(Exception ex)
        {
            if (ex is HttpListenerException || ex is ThreadAbortException)
                return;

            // Ignore common client disconnection errors
            if (ex.Message.Contains("transport connection") ||
                ex.Message.Contains("forcibly closed") ||
                ex.Message.Contains("connection was aborted"))
                return;

            if (!_shouldStop)
                NyamuLogger.LogException($"[Nyamu][Server] NyamuServer error: {ex.Message}", ex);
        }
    }
}
