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
using UnityEditor.Compilation;
using System.Collections.Generic;
using System;
using Nyamu.Core;
using Nyamu.Core.StateManagers;
using Nyamu.Tools.Compilation;
using Nyamu.Tools.Testing;
using Nyamu.Tools.Shaders;
using Nyamu.Tools.Editor;
using Nyamu.Tools.Settings;
using Nyamu.Tools.Assets;
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
            public const string CompileAndWait = "/compilation-trigger";
            public const string CompileStatus = "/compilation-status";
            public const string TestsRunSingle = "/tests-run-single";
            public const string TestsRunAll = "/tests-run-all";
            public const string TestsRunRegex = "/tests-run-regex";
            public const string TestsStatus = "/tests-status";
            public const string RefreshAssets = "/refresh-assets";
            public const string EditorStatus = "/editor-status";
            public const string McpSettings = "/mcp-settings";
            public const string TestsCancel = "/tests-cancel";
            public const string CompileShader = "/compile-shader";
            public const string CompileAllShaders = "/compile-all-shaders";
            public const string CompileShadersRegex = "/compile-shaders-regex";
            public const string ShaderCompilationStatus = "/shader-compilation-status";
            public const string ExecuteMenuItem = "/execute-menu-item";
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

        // HTTP server components
        static HttpListener _listener;
        static Thread _thread;
        static ManualResetEvent _listenerReady;

        // Compilation tracking
        static List<CompileError> _compilationErrors = new();
        static bool _isCompiling;
        static DateTime _lastCompileTime = DateTime.MinValue;  // When last compilation finished
        static DateTime _compileRequestTime = DateTime.MinValue;  // When compilation was requested

        // Unity main thread action queue (required for Unity API calls)
        static Queue<Action> _mainThreadActionQueue = new();

        // Asset refresh state tracking (prevent concurrent refresh operations)
        internal static bool _isRefreshing = false;
        static bool _isMonitoringRefresh = false;
        internal static bool _unityIsUpdating = false;  // Cache Unity's isUpdating state for thread-safe access
        internal static readonly object _refreshLock = new object();

        // Test execution state tracking (prevent concurrent test runs)
        internal static readonly object _testLock = new object();

        // Settings cache for thread-safe access from HTTP requests
        static readonly object _settingsLock = new object();
        static McpSettingsResponse _cachedSettings;
        static DateTime _lastSettingsRefresh = DateTime.MinValue;

        // Timestamp cache for thread-safe access
        internal static readonly object _timestampLock = new object();

        // Shutdown coordination
        static volatile bool _shouldStop;

        // Test execution state
        internal static bool _isRunningTests;
        internal static DateTime _lastTestTime = DateTime.MinValue;
        internal static TestResults _testResults;
        internal static string _currentTestRunId = null;  // Unique ID to track test runs across domain reloads
        internal static TestCallbacks _testCallbacks;
        // Test execution error state
        // NOTE: Currently not populated due to Unity TestRunner API limitations
        // IErrorCallbacks.OnError is not triggered for compilation errors as expected
        // Infrastructure is ready for future Unity fixes or other error scenarios
        internal static string _testExecutionError = null;
        internal static bool _hasTestExecutionError = false;

        // Play mode state tracking (cached for thread-safe access)
        static bool _isPlaying = false;

        // Shader compilation state tracking
        static bool _isCompilingShaders = false;
        static readonly object _shaderCompileLock = new object();

        // Shader compilation result tracking
        static CompileShaderResponse _lastSingleShaderResult = null;
        static CompileAllShadersResponse _lastAllShadersResult = null;
        static CompileShadersRegexResponse _lastRegexShadersResult = null;
        static string _lastShaderCompilationType = "none";
        static DateTime _lastShaderCompilationTime = DateTime.MinValue;
        static readonly object _shaderCompilationResultLock = new object();

        // Regex shader compilation progress tracking
        static string _regexShadersPattern = "";
        static int _regexShadersTotal = 0;
        static int _regexShadersCompleted = 0;
        static string _regexShadersCurrentShader = "";

        // Infrastructure components for refactored architecture
        static CompilationStateManager _compilationStateManager;
        static TestStateManager _testStateManager;
        static ShaderStateManager _shaderStateManager;
        static AssetStateManager _assetStateManager;
        static EditorStateManager _editorStateManager;
        static SettingsStateManager _settingsStateManager;
        static UnityThreadExecutor _unityThreadExecutor;
        static Core.ExecutionContext _executionContext;

        // Tools (Step 2-3: read-only tools)
        static CompilationStatusTool _compilationStatusTool;
        static TestsStatusTool _testsStatusTool;
        static ShaderCompilationStatusTool _shaderCompilationStatusTool;
        static EditorStatusTool _editorStatusTool;
        static McpSettingsTool _mcpSettingsTool;

        // Tools (Step 4 Group A: simple write tools)
        static CompilationTriggerTool _compilationTriggerTool;
        static RefreshAssetsTool _refreshAssetsTool;
        static ExecuteMenuItemTool _executeMenuItemTool;

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
            // Load cached timestamps BEFORE cleanup (so they're not lost)
            LoadTimestampsCache();

            Cleanup();

            _shouldStop = false;
            _listenerReady = new ManualResetEvent(false);
            _listener = new HttpListener();
            int port = NyamuSettings.Instance.serverPort;
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();

            _thread = new(HttpRequestProcessor);
            _thread.IsBackground = true;
            _thread.Start();

            CompilationPipeline.assemblyCompilationFinished += OnCompilationFinished;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            EditorApplication.update += OnEditorUpdate;

            _testCallbacks = new TestCallbacks();

            // Initialize infrastructure components
            InitializeInfrastructure();

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

            // Create Unity thread executor wrapping existing queue
            _unityThreadExecutor = new UnityThreadExecutor(_mainThreadActionQueue);

            // Create execution context
            _executionContext = new Core.ExecutionContext(
                _unityThreadExecutor,
                _compilationStateManager,
                _testStateManager,
                _shaderStateManager,
                _assetStateManager,
                _editorStateManager,
                _settingsStateManager
            );

            // Create tools (Step 2-3: read-only tools)
            _compilationStatusTool = new CompilationStatusTool();
            _testsStatusTool = new TestsStatusTool();
            _shaderCompilationStatusTool = new ShaderCompilationStatusTool();
            _editorStatusTool = new EditorStatusTool();
            _mcpSettingsTool = new McpSettingsTool();

            // Create tools (Step 4 Group A: simple write tools)
            _compilationTriggerTool = new CompilationTriggerTool();
            _refreshAssetsTool = new RefreshAssetsTool();
            _executeMenuItemTool = new ExecuteMenuItemTool();

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

            // Save timestamps before shutdown/domain reload
            SaveTimestampsCache();

            if (_listener?.IsListening == true)
            {
                try
                {
                    _listener.Stop();
                }
                catch { }
            }

            // Dispose ManualResetEvent
            try
            {
                _listenerReady?.Set(); // Unblock any waiting threads
                _listenerReady?.Dispose();
            }
            catch { }

            if (_thread?.IsAlive == true)
            {
                if (!_thread.Join(Constants.ThreadJoinTimeoutMilliseconds))
                {
                    try
                    {
                        _thread.Abort();
                    }
                    catch { }
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
        // UNITY EVENT HANDLERS
        // ========================================================================

        static void OnEditorUpdate()
        {
            while (_mainThreadActionQueue.Count > 0)
                _mainThreadActionQueue.Dequeue().Invoke();

            // Update cached play mode state (thread-safe)
            _isPlaying = EditorApplication.isPlaying;

            // Refresh cached settings periodically (every 2 seconds)
            if ((DateTime.Now - _lastSettingsRefresh).TotalSeconds >= 2.0)
            {
                RefreshCachedSettings();
                _lastSettingsRefresh = DateTime.Now;
            }
        }

        static void OnCompilationStarted(object obj) => _isCompiling = true;

        static void OnCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            _isCompiling = false;

            lock (_timestampLock)
            {
                _lastCompileTime = DateTime.Now;
            }

            _compilationErrors.Clear();
            foreach (var msg in messages)
            {
                if (msg.type == CompilerMessageType.Error)
                    _compilationErrors.Add(new CompileError
                    {
                        file = msg.file,
                        line = msg.line,
                        message = msg.message
                    });
            }

            // Save cache after compilation completes
            SaveTimestampsCache();
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
                Constants.Endpoints.CompileAndWait => HandleCompileAndWaitRequest(),
                Constants.Endpoints.CompileStatus => HandleCompileStatusRequest(),
                Constants.Endpoints.TestsRunSingle => HandleTestsRunSingleRequest(request),
                Constants.Endpoints.TestsRunAll => HandleTestsRunAllRequest(request),
                Constants.Endpoints.TestsRunRegex => HandleTestsRunRegexRequest(request),
                Constants.Endpoints.TestsStatus => HandleTestsStatusRequest(),
                Constants.Endpoints.RefreshAssets => HandleRefreshAssetsRequest(request),
                Constants.Endpoints.EditorStatus => HandleEditorStatusRequest(),
                Constants.Endpoints.McpSettings => HandleMcpSettingsRequest(),
                Constants.Endpoints.TestsCancel => HandleTestsCancelRequest(request),
                Constants.Endpoints.CompileShader => HandleCompileShaderRequest(request),
                Constants.Endpoints.CompileAllShaders => HandleCompileAllShadersRequest(request),
                Constants.Endpoints.CompileShadersRegex => HandleCompileShadersRegexRequest(request),
                Constants.Endpoints.ShaderCompilationStatus => HandleShaderCompilationStatusRequest(request),
                Constants.Endpoints.ExecuteMenuItem => HandleExecuteMenuItemRequest(request),
                _ => HandleNotFoundRequest(response)
            };
        }

        static string HandleCompileAndWaitRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleCompileAndWaitRequest");

            // Sync old state (during transition - Step 4)
            SyncCompilationStateToManager();

            // Use new tool architecture
            var request = new CompilationTriggerRequest();
            var response = _compilationTriggerTool.ExecuteAsync(request, _executionContext).Result;

            // Sync state back to old variables
            lock (_timestampLock)
            {
                _compileRequestTime = _compilationStateManager.CompileRequestTime;
            }

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
                lock (_refreshLock)
                {
                    refreshInProgress = _isRefreshing;
                    unityIsUpdating = _unityIsUpdating;
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
                if (_isCompiling || EditorApplication.isCompiling)
                    return (true, "Compilation started.");

                DateTime lastCompileTimeCopy;
                lock (_timestampLock)
                {
                    lastCompileTimeCopy = _lastCompileTime;
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

            // Sync old state into state manager (during transition - Step 2)
            SyncCompilationStateToManager();

            // Use new tool architecture
            var request = new CompilationStatusRequest();
            var response = _compilationStatusTool.ExecuteAsync(request, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static void SyncCompilationStateToManager()
        {
            lock (_compilationStateManager.Lock)
            {
                _compilationStateManager.IsCompiling = _isCompiling || EditorApplication.isCompiling;
                _compilationStateManager.Errors = _compilationErrors;
                lock (_timestampLock)
                {
                    _compilationStateManager.LastCompileTime = _lastCompileTime;
                    _compilationStateManager.CompileRequestTime = _compileRequestTime;
                }
            }
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

        static void SyncTestStateToManager()
        {
            lock (_testStateManager.Lock)
            {
                _testStateManager.IsRunningTests = _isRunningTests;
                _testStateManager.TestResults = _testResults;
                _testStateManager.CurrentTestRunId = _currentTestRunId;
                _testStateManager.HasTestExecutionError = _hasTestExecutionError;
                _testStateManager.TestExecutionError = _testExecutionError;
                lock (_timestampLock)
                {
                    _testStateManager.LastTestTime = _lastTestTime;
                }
            }
        }

        static string HandleEditorStatusRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleEditorStatusRequest");

            // Sync old state into state managers (during transition - Step 3)
            SyncCompilationStateToManager();
            SyncTestStateToManager();
            SyncEditorStateToManager();

            // Use new tool architecture
            var request = new EditorStatusRequest();
            var response = _editorStatusTool.ExecuteAsync(request, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static void SyncEditorStateToManager()
        {
            lock (_editorStateManager.Lock)
            {
                _editorStateManager.IsPlaying = _isPlaying;
            }
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

            SyncTestStateToManager();

            var query = request.Url.Query ?? "";
            var testName = ExtractQueryParameter(query, "test_name");
            var mode = ExtractQueryParameter(query, "mode") ?? "EditMode";

            var toolRequest = new TestsRunSingleRequest
            {
                testName = testName,
                testMode = mode
            };

            var response = _testsRunSingleTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleTestsRunAllRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsRunAllRequest");

            SyncTestStateToManager();

            var query = request.Url.Query ?? "";
            var mode = ExtractQueryParameter(query, "mode") ?? "EditMode";

            var toolRequest = new TestsRunAllRequest
            {
                testMode = mode
            };

            var response = _testsRunAllTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleTestsRunRegexRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsRunRegexRequest");

            SyncTestStateToManager();

            var query = request.Url.Query ?? "";
            var filterRegex = ExtractQueryParameter(query, "filter_regex");
            var mode = ExtractQueryParameter(query, "mode") ?? "EditMode";

            var toolRequest = new TestsRunRegexRequest
            {
                testFilterRegex = filterRegex,
                testMode = mode
            };

            var response = _testsRunRegexTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleTestsStatusRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsStatusRequest");

            var status = _isRunningTests ? "running" : "idle";

            DateTime lastTestTimeCopy;
            lock (_timestampLock)
            {
                lastTestTimeCopy = _lastTestTime;
            }

            var statusResponse = new TestStatusResponse
            {
                status = status,
                isRunning = _isRunningTests,
                lastTestTime = lastTestTimeCopy.ToString("yyyy-MM-dd HH:mm:ss"),
                testResults = _testResults,
                testRunId = _currentTestRunId,
                hasError = _hasTestExecutionError,
                errorMessage = _testExecutionError
            };

            return JsonUtility.ToJson(statusResponse);
        }

        static string HandleTestsCancelRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsCancelRequest");

            SyncTestStateToManager();

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
            lock (_shaderCompilationResultLock)
            {
                _regexShadersPattern = pattern;
                _regexShadersTotal = total;
                _regexShadersCompleted = completed;
                _regexShadersCurrentShader = currentShader;
            }
        }


        static string HandleCompileShaderRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileShaderRequest");

            SyncShaderStateToManager();

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

        static string HandleCompileAllShadersRequest(HttpListenerRequest _)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileAllShadersRequest");

            SyncShaderStateToManager();

            var toolRequest = new CompileAllShadersRequest { timeout = 120 };
            var response = _compileAllShadersTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        static string HandleCompileShadersRegexRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileShadersRegexRequest");
            if (request.HttpMethod != "POST")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use POST.\"}";

            SyncShaderStateToManager();

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

        static void SyncShaderStateToManager()
        {
            lock (_shaderStateManager.Lock)
            {
                _shaderStateManager.IsCompiling = _isCompilingShaders;
                _shaderStateManager.RegexShadersPattern = _regexShadersPattern;
                _shaderStateManager.RegexShadersTotal = _regexShadersTotal;
                _shaderStateManager.RegexShadersCompleted = _regexShadersCompleted;
                _shaderStateManager.RegexShadersCurrentShader = _regexShadersCurrentShader;
            }

            lock (_shaderStateManager.ResultLock)
            {
                _shaderStateManager.LastSingleShaderResult = _lastSingleShaderResult;
                _shaderStateManager.LastAllShadersResult = _lastAllShadersResult;
                _shaderStateManager.LastRegexShadersResult = _lastRegexShadersResult;
                _shaderStateManager.LastCompilationType = _lastShaderCompilationType;
                _shaderStateManager.LastCompilationTime = _lastShaderCompilationTime;
            }
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

        static string HandleNotFoundRequest(HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            return "{\"status\":\"error\", \"message\":\"Not Found\"}";
        }

        static void SendResponse(HttpListenerResponse response, string content)
        {
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        static void RefreshCachedSettings()
        {
            try
            {
                var settings = NyamuSettings.Instance;
                var newSettings = new McpSettingsResponse
                {
                    responseCharacterLimit = settings.responseCharacterLimit,
                    enableTruncation = settings.enableTruncation,
                    truncationMessage = settings.truncationMessage
                };

                lock (_settingsLock)
                {
                    _cachedSettings = newSettings;
                }
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Failed to refresh cached Nyamu settings: {ex.Message}");
            }
        }

        static void LoadTimestampsCache()
        {
            try
            {
                var cache = NyamuServerCache.Load();
                lock (_timestampLock)
                {
                    _lastCompileTime = ParseDateTime(cache.lastCompilationTime);
                    _compileRequestTime = ParseDateTime(cache.lastCompilationRequestTime);
                    _lastTestTime = ParseDateTime(cache.lastTestRunTime);
                }

                NyamuLogger.LogDebug($"[Nyamu][Server] Restored timestamps from cache - " +
                         $"LastCompile: {_lastCompileTime:yyyy-MM-dd HH:mm:ss}, " +
                         $"CompileRequest: {_compileRequestTime:yyyy-MM-dd HH:mm:ss}, " +
                         $"LastTest: {_lastTestTime:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Failed to load timestamp cache: {ex.Message}");
            }
        }

        internal static void SaveTimestampsCache()
        {
            try
            {
                lock (_timestampLock)
                {
                    var cache = new NyamuServerCache
                    {
                        lastCompilationTime = _lastCompileTime.ToString("o"),
                        lastCompilationRequestTime = _compileRequestTime.ToString("o"),
                        lastTestRunTime = _lastTestTime.ToString("o")
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
            lock (_refreshLock)
            {
                _unityIsUpdating = unityIsUpdating;
            }

            // Also update state manager
            lock (_assetStateManager.Lock)
            {
                _assetStateManager.UnityIsUpdating = unityIsUpdating;
            }

            // Check if AssetDatabase refresh is complete
            if (!unityIsUpdating)
            {
                // Refresh is complete, reset the flags and unsubscribe
                lock (_refreshLock)
                {
                    _isRefreshing = false;
                    _isMonitoringRefresh = false;
                    _unityIsUpdating = false;
                }
                lock (_assetStateManager.Lock)
                {
                    _assetStateManager.IsRefreshing = false;
                    _assetStateManager.IsMonitoringRefresh = false;
                    _assetStateManager.UnityIsUpdating = false;
                }
                EditorApplication.update -= MonitorRefreshCompletion;
            }
        }

        static string HandleRefreshAssetsRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleRefreshAssetsRequest");

            // Parse force parameter from query string
            bool force = request.Url.Query.Contains("force=true");

            // Sync old state (during transition - Step 4)
            SyncAssetStateToManager();

            // Use new tool architecture
            var toolRequest = new RefreshAssetsRequest { force = force };
            var response = _refreshAssetsTool.ExecuteAsync(toolRequest, _executionContext).Result;

            // Sync state back to old variables
            lock (_refreshLock)
            {
                _isRefreshing = _assetStateManager.IsRefreshing;
                _isMonitoringRefresh = _assetStateManager.IsMonitoringRefresh;
                _unityIsUpdating = _assetStateManager.UnityIsUpdating;
            }

            return JsonUtility.ToJson(response);
        }

        static void SyncAssetStateToManager()
        {
            lock (_assetStateManager.Lock)
            {
                lock (_refreshLock)
                {
                    _assetStateManager.IsRefreshing = _isRefreshing;
                    _assetStateManager.IsMonitoringRefresh = _isMonitoringRefresh;
                    _assetStateManager.UnityIsUpdating = _unityIsUpdating;
                }
            }
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
                NyamuLogger.LogError($"[Nyamu][Server] NyamuServer error: {ex.Message}");
        }
    }
}
