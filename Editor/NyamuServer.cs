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
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Linq;
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
// ReSharper disable InconsistentlySynchronizedField

namespace Nyamu
{
    // ============================================================================
    // CONFIGURATION AND CONSTANTS
    // ============================================================================

    // Configuration constants for the Nyamu MCP server
    internal static class Constants
    {
        public const int CompileTimeoutSeconds = 5;
        public const int ThreadSleepMilliseconds = 50;

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
        private const string SessionKeyEditorRunning = "Nyamu_EditorRunning";
        public const string SessionKeyRefreshRequestTime = "Nyamu_RefreshRequestTime";
        public const string SessionKeyRefreshCompletedTime = "Nyamu_RefreshCompletedTime";

        // HTTP server components
        private static HttpListener _listener;
        private static Task _acceptTask;
        private static CancellationTokenSource _cancellation;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Task, byte> ActiveHandlers = new();

        // Deferred recovery: retries port binding via EditorApplication.update after
        // immediate retries fail (e.g. due to TIME_WAIT from Mono raw sockets).
        private static bool _deferredRecoveryActive;
        private static int _deferredRetryAttempt;
        private static double _deferredRetryTime;
        private const int MaxDeferredAttempts = 20;
        private const double DeferredRetryIntervalSeconds = 3.0;

        // Infrastructure components for refactored architecture
        private static CompilationStateManager _compilationStateManager;
        private static TestStateManager _testStateManager;
        private static ShaderStateManager _shaderStateManager;
        private static AssetStateManager _assetStateManager;
        private static EditorStateManager _editorStateManager;
        private static SettingsStateManager _settingsStateManager;
        private static UnityThreadExecutor _unityThreadExecutor;
        private static Core.ExecutionContext _executionContext;

        // Monitors and services
        private static CompilationMonitor _compilationMonitor;
        private static EditorMonitor _editorMonitor;
        private static SettingsMonitor _settingsMonitor;
        private static TestExecutionService _testExecutionService;
        private static TestCallbacks _testCallbacks;

        // Tools (Step 2-3: read-only tools)
        private static CompilationStatusTool _compilationStatusTool;
        private static TestsStatusTool _testsStatusTool;
        private static ShaderCompilationStatusTool _shaderCompilationStatusTool;
        private static EditorStatusTool _editorStatusTool;
        private static McpSettingsTool _mcpSettingsTool;

        // Tools (Step 4 Group A: simple write tools)
        private static CompilationTriggerTool _compilationTriggerTool;
        private static AssetsRefreshTool _assetsRefreshTool;
        private static ExecuteMenuItemTool _executeMenuItemTool;
        private static EditorExitPlayModeTool _editorExitPlayModeTool;

        // Step 4 Group B: test tools
        private static TestsRunSingleTool _testsRunSingleTool;
        private static TestsRunAllTool _testsRunAllTool;
        private static TestsRunRegexTool _testsRunRegexTool;
#if UTF_TESTS_CANCEL_TOOL_AVAILABLE
        static TestsCancelTool _testsCancelTool;
#endif

        // Step 4 Group C: shader tools
        private static CompileShaderTool _compileShaderTool;
        private static CompileAllShadersTool _compileAllShadersTool;
        private static CompileShadersRegexTool _compileShadersRegexTool;

        static Server()
        {
            Initialize();
        }

        private static void Initialize()
        {
            NyamuLogger.RefreshMinLogLevel();
            
            NyamuLogger.LogDebug("[Nyamu][Server] Initialize started");

            // Cancel any deferred recovery from a previous initialization attempt.
            // After domain reload, EditorApplication.update subscriptions are cleared, but
            // _deferredRecoveryActive may still be true if "Disable Domain Reload" is on.
            _deferredRecoveryActive = false;

            // Domain reload safety: ensure previous instance is fully cleaned up
            // When "Disable Domain Reload" is enabled, static fields persist across Play Mode transitions
            if (_acceptTask != null || _cancellation != null || _listener != null)
            {
                NyamuLogger.LogDebug("[Nyamu][Server] Detected stale state, forcing cleanup");
                Cleanup();
            }
            else
            {
                Cleanup();
            }

            // Initialize infrastructure components first (creates state managers and monitors)
            InitializeInfrastructure();

            // Load cached timestamps after infrastructure is ready
            LoadTimestampsCache();

            // Detect if domain reload occurred after a pending refresh
            DetectRefreshCompletion();

            // Try to start HTTP listener with retry logic for port release delays
            // Increased retry window to handle TIME_WAIT state (can take up to 2 minutes on some systems)
            const int maxRetries = 10;
            const int retryDelayMs = 500;
            var port = NyamuSettings.Instance.serverPort;
            var success = false;
            var bindStart = DateTime.UtcNow;

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
                catch (Exception ex)
                {
                    // Port still in use (likely TIME_WAIT state after domain reload)
                    // Note: Mono may throw SocketException instead of HttpListenerException,
                    // so we catch all exceptions here to ensure retries always happen.
                    try
                    {
                        _listener?.Close();
                    }
                    catch
                    {
                        // ignored
                    }

                    _listener = null;

                    if (attempt < maxRetries - 1)
                    {
                        var elapsed = (DateTime.UtcNow - bindStart).TotalMilliseconds;
                        NyamuLogger.LogDebug($"[Nyamu][Server] Port {port} unavailable, retrying in {retryDelayMs}ms (attempt {attempt + 1}/{maxRetries}, elapsed: {elapsed:F0}ms): [{ex.GetType().Name}] {ex.Message}");
                        Thread.Sleep(retryDelayMs);
                    }
                    else
                    {
                        var elapsed = (DateTime.UtcNow - bindStart).TotalMilliseconds;
                        NyamuLogger.LogError($"[Nyamu][Server] Port {port} remains in use after {maxRetries} attempts ({elapsed:F0}ms): [{ex.GetType().Name}] {ex.Message}. " +
                            "Run 'netsh http show servicepoint' or 'netstat -ano | findstr {port}' to identify what holds the port. " +
                            "This may happen if another Unity Editor instance is using this port. Please check Project Settings > Nyamu to change the port.");
                    }
                }
            }

            if (!success)
            {
                NyamuLogger.LogWarning($"[Nyamu][Server] Port {port} still in use after immediate retries. Scheduling deferred recovery every {DeferredRetryIntervalSeconds}s...");
                _deferredRecoveryActive = true;
                _deferredRetryAttempt = 0;
                _deferredRetryTime = EditorApplication.timeSinceStartup + DeferredRetryIntervalSeconds;
                EditorApplication.update += TryDeferredBind;
                return;
            }

            _cancellation = new CancellationTokenSource();
            _acceptTask = AcceptRequestsAsync(_cancellation.Token);

            EditorApplication.quitting += Cleanup;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        private static void InitializeInfrastructure()
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
#if UTF_TESTS_CANCEL_TOOL_AVAILABLE
            _testsCancelTool = new TestsCancelTool();
#endif

            // Create tools (Step 4 Group C: shader tools)
            _compileShaderTool = new CompileShaderTool();
            _compileAllShadersTool = new CompileAllShadersTool();
            _compileShadersRegexTool = new CompileShadersRegexTool();
        }

        private static void Cleanup()
        {
            var cleanupStart = DateTime.UtcNow;
            NyamuLogger.LogDebug("[Nyamu][Server] Cleanup started");

            // Event unsubscription (idempotent)
            EditorApplication.quitting -= Cleanup;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;

            // Cleanup monitors
            _compilationMonitor?.Cleanup();
            _editorMonitor?.Cleanup();
            SaveTimestampsCache();

            // 1. Cancel accept loop (idempotent - safe to call multiple times)
            try
            {
                _cancellation?.Cancel();
            }
            catch (ObjectDisposedException) { } // Already disposed

            // 2. Stop listener (releases port immediately)
            if (_listener != null)
            {
                var wasListening = false;
                try
                {
                    wasListening = _listener.IsListening;
                    NyamuLogger.LogDebug($"[Nyamu][Server] Stopping listener (IsListening={wasListening})");
                    if (wasListening) _listener.Stop();
                    _listener.Close();
                    NyamuLogger.LogDebug("[Nyamu][Server] Listener closed");
                }
                catch (Exception ex)
                {
                    NyamuLogger.LogWarning($"[Nyamu][Server] Exception during listener stop/close (wasListening={wasListening}): [{ex.GetType().Name}] {ex.Message}");
                }
                finally { _listener = null; }
            }
            else
            {
                NyamuLogger.LogDebug("[Nyamu][Server] Listener was null at cleanup time (already stopped or never started)");
            }

            // 3. Wait for accept loop to exit (with timeout to avoid blocking too long)
            if (_acceptTask != null)
            {
                var taskToTrack = _acceptTask;
                var taskStatusBefore = _acceptTask.Status;
                try
                {
                    // Wait up to 1 second for task to complete
                    // This is necessary to ensure port is fully released before rebinding
                    if (!_acceptTask.Wait(1000))
                    {
                        var elapsed = (DateTime.UtcNow - cleanupStart).TotalMilliseconds;
                        NyamuLogger.LogDebug($"[Nyamu][Server] Accept loop still running after 1s (elapsed: {elapsed:F0}ms, status before wait: {taskStatusBefore}, status after: {taskToTrack.Status})");

                        _ = taskToTrack.ContinueWith(t =>
                        {
                            var status = t.IsFaulted ? $"faulted ({t.Exception?.GetBaseException().Message})"
                                       : t.IsCanceled ? "canceled"
                                       : "completed";
                            NyamuLogger.LogDebug($"[Nyamu][Server] Dangling accept task finally {status}");
                        }, TaskScheduler.Default);
                    }
                    else
                    {
                        var elapsed = (DateTime.UtcNow - cleanupStart).TotalMilliseconds;
                        NyamuLogger.LogDebug($"[Nyamu][Server] Accept loop exited cleanly (elapsed: {elapsed:F0}ms)");
                    }
                }
                catch (AggregateException) { } // Expected from task cancellation
                catch
                {
                    // ignored
                }
                finally { _acceptTask = null; }
            }

            // 4. Wait for active request handlers to complete (with timeout)
            var activeHandlers = ActiveHandlers.Keys.ToArray();
            if (activeHandlers.Length > 0)
            {
                try
                {
                    NyamuLogger.LogDebug($"[Nyamu][Server] Waiting for {activeHandlers.Length} active request handler(s) to complete");

                    // Wait up to 2 seconds for handlers to finish
                    if (!Task.WaitAll(activeHandlers, 2000))
                    {
                        var stillRunning = 0;
                        foreach (var t in activeHandlers)
                            if (!t.IsCompleted) stillRunning++;
                        NyamuLogger.LogWarning($"[Nyamu][Server] {stillRunning} request handler(s) still running after 2s, forcing cleanup");
                    }
                }
                catch (AggregateException) { } // Expected from handler exceptions
                catch
                {
                    // ignored
                }
                finally
                {
                    ActiveHandlers.Clear();
                }
            }

            // 5. Dispose cancellation token
            try
            {
                _cancellation?.Dispose();
                _cancellation = null;
            }
            catch
            {
                // ignored
            }

            NyamuLogger.LogDebug($"[Nyamu][Server] Cleanup finished (total elapsed: {(DateTime.UtcNow - cleanupStart).TotalMilliseconds:F0}ms)");
        }

        // Public method to restart server (e.g., when port changes)
        public static void Restart()
        {
            NyamuLogger.LogInfo("[Nyamu][Server] Restarting server...");
            Initialize();
            NyamuLogger.LogInfo($"[Nyamu][Server] Server restarted on port {NyamuSettings.Instance.serverPort}");
        }

        // Called every editor frame during deferred recovery. Attempts to bind the port
        // without blocking the main thread. Unsubscribes itself on success, failure, or
        // when a domain reload causes Initialize() to run again.
        private static void TryDeferredBind()
        {
            // A domain reload triggered a new Initialize() which either succeeded or started
            // its own deferred recovery — either way, our job here is done.
            if (!_deferredRecoveryActive)
            {
                EditorApplication.update -= TryDeferredBind;
                return;
            }

            if (EditorApplication.timeSinceStartup < _deferredRetryTime)
                return;

            _deferredRetryAttempt++;
            var port = NyamuSettings.Instance.serverPort;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();

                _cancellation = new CancellationTokenSource();
                _acceptTask = AcceptRequestsAsync(_cancellation.Token);
                EditorApplication.quitting += Cleanup;
                AssemblyReloadEvents.beforeAssemblyReload += Cleanup;

                _deferredRecoveryActive = false;
                NyamuLogger.LogInfo($"[Nyamu][Server] Deferred recovery succeeded on attempt {_deferredRetryAttempt} (port {port})");
                EditorApplication.update -= TryDeferredBind;
            }
            catch (Exception ex)
            {
                try { _listener?.Close(); }
                catch
                {
                    // ignored
                }

                _listener = null;

                if (_deferredRetryAttempt >= MaxDeferredAttempts)
                {
                    _deferredRecoveryActive = false;
                    NyamuLogger.LogError($"[Nyamu][Server] Deferred recovery gave up after {MaxDeferredAttempts} attempts. MCP integration will not be available.");
                    EditorApplication.update -= TryDeferredBind;
                    return;
                }

                NyamuLogger.LogDebug($"[Nyamu][Server] Deferred attempt {_deferredRetryAttempt}/{MaxDeferredAttempts} failed, next in {DeferredRetryIntervalSeconds}s: [{ex.GetType().Name}] {ex.Message}");
                _deferredRetryTime = EditorApplication.timeSinceStartup + DeferredRetryIntervalSeconds;
            }
        }

        // ========================================================================
        // HTTP SERVER INFRASTRUCTURE
        // ========================================================================

        private static async Task AcceptRequestsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // GetContextAsync() is cancellation-aware
                    var contextTask = _listener.GetContextAsync();

                    // Race between context arrival and cancellation
                    var completed = await Task.WhenAny(contextTask, Task.Delay(-1, token));

                    if (completed != contextTask)
                        break; // Cancellation won the race

                    var context = await contextTask;

                    // Process in ThreadPool (preserves existing multi-threading behavior)
                    var handlerTask = Task.Run(() =>
                    {
                        try
                        {
                            ProcessHttpRequest(context);
                        }
                        catch (Exception ex)
                        {
                            HandleHttpException(ex);
                        }
                    }, token);

                    // Track active handler and remove when completed
                    ActiveHandlers.TryAdd(handlerTask, 0);
                    _ = handlerTask.ContinueWith(t => ActiveHandlers.TryRemove(t, out _), TaskScheduler.Default);
                }
                catch (ObjectDisposedException) { break; } // Listener stopped
                catch (HttpListenerException) { break; }  // Listener error
                catch (Exception ex) { HandleHttpException(ex); }
            }
        }

        private static void ProcessHttpRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            SetupResponseHeaders(response);

            var responseString = RouteRequest(request, response);
            SendResponse(response, responseString);
        }

        private static void SetupResponseHeaders(HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        }

        private static string RouteRequest(HttpListenerRequest request, HttpListenerResponse response)
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

        private static string HandleCompileAndWaitRequest()
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
                var isCompiling = _compilationStateManager.IsCompiling;

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

        private static string HandleCompileStatusRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleCompileStatusRequest");

            // Use new tool architecture
            var request = new CompilationStatusRequest();
            var response = _compilationStatusTool.ExecuteAsync(request, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        private static string ExtractQueryParameter(string query, string paramName)
        {
            if (!query.Contains($"{paramName}="))
                return null;

            var paramStart = query.IndexOf($"{paramName}=", StringComparison.Ordinal) + paramName.Length + 1;
            var paramEnd = query.IndexOf("&", paramStart, StringComparison.Ordinal);
            var value = paramEnd == -1 ? query.Substring(paramStart) : query.Substring(paramStart, paramEnd - paramStart);
            return Uri.UnescapeDataString(value);
        }

        private static string HandleEditorStatusRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleEditorStatusRequest");

            // Use new tool architecture
            var request = new EditorStatusRequest();
            var response = _editorStatusTool.ExecuteAsync(request, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        private static string HandleMcpSettingsRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleMcpSettingsRequest");

            // Use new tool architecture (tool handles caching internally)
            var request = new McpSettingsRequest();
            var response = _mcpSettingsTool.ExecuteAsync(request, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        private static string HandleTestsRunSingleRequest(HttpListenerRequest request)
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
                catch
                {
                    // ignored
                }
            }

            // Fallback to query parameters if body not available
            if (toolRequest == null)
            {
                var query = request.Url.Query;
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

        private static string HandleTestsRunAllRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleTestsRunAllRequest");

            TestsRunAllRequest toolRequest = null;

            // Try to read request body first (for async/timeout params)
            if (request.HttpMethod == "POST" && request.ContentLength64 > 0)
            {
                try
                {
                    var bodyText = new StreamReader(request.InputStream).ReadToEnd();
                    toolRequest = JsonUtility.FromJson<TestsRunAllRequest>(bodyText);
                }
                catch
                {
                    // ignored
                }
            }

            // Fallback to query parameters if body not available
            if (toolRequest == null)
            {
                var query = request.Url.Query;
                var mode = ExtractQueryParameter(query, "mode") ?? "EditMode";

                toolRequest = new TestsRunAllRequest
                {
                    testMode = mode
                };
            }

            var response = _testsRunAllTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        private static string HandleTestsRunRegexRequest(HttpListenerRequest request)
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
                catch
                {
                    // ignored
                }
            }

            // Fallback to query parameters if body not available
            if (toolRequest == null)
            {
                var query = request.Url.Query;
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

        private static string HandleTestsStatusRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsStatusRequest");

            // Use new tool architecture (no sync needed - read-only operation)
            var toolRequest = new TestsStatusRequest();
            var response = _testsStatusTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        // ReSharper disable once UnusedParameter.Local
        private static string HandleTestsCancelRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestsCancelRequest");

#if UTF_TESTS_CANCEL_TOOL_AVAILABLE
            var query = request.Url.Query ?? "";
            var testRunGuid = ExtractQueryParameter(query, "guid");

            var toolRequest = new TestsCancelRequest
            {
                testRunGuid = testRunGuid
            };

            var response = _testsCancelTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
#else
            return "{\"status\":\"error\",\"message\":\"tests_run_cancel tool is only available with NUnit v2 (package: com.unity.ext.nunit)\",\"guid\":\"\"}";
#endif
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


        private static string HandleCompileShaderRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileShaderRequest");

            CompileShaderRequest toolRequest = null;
            try
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
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
            catch
            {
                return "{\"status\":\"error\",\"message\":\"Invalid request body.\"}";
            }

            toolRequest ??= new CompileShaderRequest { timeout = 30 };

            var response = _compileShaderTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        private static string HandleCompileAllShadersRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileAllShadersRequest");
            if (request.HttpMethod != "POST")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use POST.\"}";

            CompileAllShadersRequest toolRequest;
            try
            {
                var bodyText = new StreamReader(request.InputStream).ReadToEnd();
                toolRequest = JsonUtility.FromJson<CompileAllShadersRequest>(bodyText);
            }
            catch
            {
                return "{\"status\":\"error\",\"message\":\"Invalid request body.\"}";
            }

            toolRequest ??= new CompileAllShadersRequest { timeout = 120 };

            var response = _compileAllShadersTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        private static string HandleCompileShadersRegexRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileShadersRegexRequest");
            if (request.HttpMethod != "POST")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use POST.\"}";

            CompileShadersRegexToolRequest toolRequest;
            try
            {
                var bodyText = new StreamReader(request.InputStream).ReadToEnd();
                toolRequest = JsonUtility.FromJson<CompileShadersRegexToolRequest>(bodyText);
            }
            catch
            {
                return "{\"status\":\"error\",\"message\":\"Invalid request body.\"}";
            }

            toolRequest ??= new CompileShadersRegexToolRequest { timeout = 120 };

            var response = _compileShadersRegexTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        private static string HandleShaderCompilationStatusRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleShaderCompilationStatusRequest");
            if (request.HttpMethod != "GET")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use GET.\"}";

            // Use new tool architecture (no sync needed - read-only operation)
            var toolRequest = new ShaderCompilationStatusRequest();
            var response = _shaderCompilationStatusTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        private static string HandleExecuteMenuItemRequest(HttpListenerRequest request)
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

        private static string HandleEditorExitPlayModeRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleEditorExitPlayModeRequest");

            if (request.HttpMethod != "GET")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use GET.\"}";

            // Use new tool architecture
            var toolRequest = new EditorExitPlayModeRequest();
            var response = _editorExitPlayModeTool.ExecuteAsync(toolRequest, _executionContext).Result;
            return JsonUtility.ToJson(response);
        }

        private static string HandleNotFoundRequest(HttpListenerResponse response)
        {
            response.StatusCode = 404;
            return "{\"status\":\"error\",\"message\":\"Endpoint not found\"}";
        }

        private static void SendResponse(HttpListenerResponse response, string content)
        {
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private static void LoadTimestampsCache()
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
                var isEditorRunning = SessionState.GetBool(SessionKeyEditorRunning, false);

                if (!isEditorRunning)
                {
                    // Fresh editor start - set the flag for subsequent domain reloads
                    SessionState.SetBool(SessionKeyEditorRunning, true);
                    NyamuLogger.LogDebug("[Nyamu][Server] Fresh Unity Editor start detected. Clearing any stale refresh state.");

                    // Clear refresh state in AssetStateManager
                    _assetStateManager.RefreshRequestTime = DateTime.MinValue;
                    _assetStateManager.RefreshCompletedTime = DateTime.MinValue;
                    _assetStateManager.IsRefreshing = false;
                    _assetStateManager.IsWaitingForCompilation = false;

                    // Clear SessionState refresh timestamps
                    SessionState.EraseString(SessionKeyRefreshRequestTime);
                    SessionState.EraseString(SessionKeyRefreshCompletedTime);
                }
                else
                {
                    // Domain reload within same editor session - restore refresh state from SessionState
                    NyamuLogger.LogDebug("[Nyamu][Server] Domain reload detected. Restoring refresh state from SessionState.");

                    var refreshRequestStr = SessionState.GetString(SessionKeyRefreshRequestTime, "");
                    var refreshCompletedStr = SessionState.GetString(SessionKeyRefreshCompletedTime, "");

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

        private static void DetectRefreshCompletion()
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
                    SessionState.SetString(SessionKeyRefreshCompletedTime, DateTime.Now.ToString("o"));

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
                    SessionState.EraseString(SessionKeyRefreshRequestTime);
                    SessionState.EraseString(SessionKeyRefreshCompletedTime);
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

        private static DateTime ParseDateTime(string str)
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

        private static void MonitorRefreshCompletion()
        {
            // Update cached state (this runs on main thread)
            var unityIsUpdating = EditorApplication.isUpdating;
            var isCompiling = EditorApplication.isCompiling;

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
                    SessionState.SetString(SessionKeyRefreshCompletedTime, DateTime.Now.ToString("o"));

                    EditorApplication.update -= MonitorRefreshCompletion;
                    NyamuLogger.LogDebug("[Nyamu][Server] Refresh chain completed (with compilation)");
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
                    SessionState.SetString(SessionKeyRefreshCompletedTime, DateTime.Now.ToString("o"));

                    EditorApplication.update -= MonitorRefreshCompletion;
                    NyamuLogger.LogDebug("[Nyamu][Server] Refresh completed (no compilation)");
                }
            }
        }

        private static string HandleAssetsRefreshRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleAssetsRefreshRequest");

            // Parse force parameter from query string
            var force = request.Url.Query.Contains("force=true");

            // Use new tool architecture
            var toolRequest = new AssetsRefreshRequest { force = force };
            var response = _assetsRefreshTool.ExecuteAsync(toolRequest, _executionContext).Result;

            return JsonUtility.ToJson(response);
        }

        private static string HandleAssetsRefreshStatusRequest(HttpListenerRequest _)
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

            var isCompiling = _compilationStateManager.IsCompiling;

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
            var hadCompilation = false;
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

        private static void HandleHttpException(Exception ex)
        {
            if (ex is HttpListenerException or ThreadAbortException)
                return;

            // Ignore common client disconnection errors
            if (ex.Message.Contains("transport connection") ||
                ex.Message.Contains("forcibly closed") ||
                ex.Message.Contains("connection was aborted"))
                return;

            if (_cancellation == null || !_cancellation.IsCancellationRequested)
                NyamuLogger.LogException($"[Nyamu][Server] NyamuServer error: {ex.Message}", ex);
        }
    }
}
