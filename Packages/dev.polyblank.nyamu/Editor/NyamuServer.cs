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
using UnityEditor.TestTools.TestRunner.Api;
using System.Linq;

namespace Nyamu
{
    // ============================================================================
    // CONFIGURATION AND CONSTANTS
    // ============================================================================

    // Configuration constants for the Nyamu MCP server
    static class Constants
    {
        public const int ServerPort = 17932;
        public const int CompileTimeoutSeconds = 5;
        public const int ThreadSleepMilliseconds = 50;
        public const int ThreadJoinTimeoutMilliseconds = 1000;

        public static class Endpoints
        {
            public const string CompileAndWait = "/compile-and-wait";
            public const string CompileStatus = "/compile-status";
            public const string RunTests = "/run-tests";
            public const string TestStatus = "/test-status";
            public const string RefreshAssets = "/refresh-assets";
            public const string EditorStatus = "/editor-status";
            public const string McpSettings = "/mcp-settings";
            public const string CancelTests = "/cancel-tests";
        }

        public static class JsonResponses
        {
            public const string CompileStarted = "{\"status\":\"ok\", \"message\":\"Compilation started.\"}";
            public const string TestStarted = "{\"status\":\"ok\", \"message\":\"Test execution started.\"}";
            public const string AssetsRefreshed = "{\"status\":\"ok\", \"message\":\"Asset database refreshed.\"}";
        }
    }

    // ============================================================================
    // DATA TRANSFER OBJECTS (DTOs)
    // ============================================================================
    // These classes define the JSON structure for API requests and responses

    [System.Serializable]
    public class CompileError
    {
        public string file;
        public int line;
        public string message;
    }

    [System.Serializable]
    public class CompileStatusResponse
    {
        public string status;
        public bool isCompiling;
        public string lastCompileTime;
        public CompileError[] errors;
    }

    [System.Serializable]
    public class TestResult
    {
        public string name;
        public string outcome;
        public string message;
        public double duration;
    }

    [System.Serializable]
    public class TestResults
    {
        public int totalTests;
        public int passedTests;
        public int failedTests;
        public int skippedTests;
        public double duration;
        public TestResult[] results;
    }

    [System.Serializable]
    public class TestStatusResponse
    {
        public string status;
        public bool isRunning;
        public string lastTestTime;
        public TestResults testResults;
        public string testRunId;
        public bool hasError;
        public string errorMessage;
    }

    [System.Serializable]
    public class EditorStatusResponse
    {
        public bool isCompiling;
        public bool isRunningTests;
        public bool isPlaying;
    }

    [System.Serializable]
    public class McpSettingsResponse
    {
        public int responseCharacterLimit;
        public bool enableTruncation;
        public string truncationMessage;
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

        // Compilation tracking
        static List<CompileError> _compilationErrors = new();
        static bool _isCompiling;
        static DateTime _lastCompileTime = DateTime.MinValue;  // When last compilation finished
        static DateTime _compileRequestTime = DateTime.MinValue;  // When compilation was requested

        // Unity main thread action queue (required for Unity API calls)
        static Queue<Action> _mainThreadActionQueue = new();

        // Asset refresh state tracking (prevent concurrent refresh operations)
        static bool _isRefreshing = false;
        static bool _isMonitoringRefresh = false;
        static bool _unityIsUpdating = false;  // Cache Unity's isUpdating state for thread-safe access
        static readonly object _refreshLock = new object();

        // Test execution state tracking (prevent concurrent test runs)
        static readonly object _testLock = new object();

        // Settings cache for thread-safe access from HTTP requests
        static readonly object _settingsLock = new object();
        static McpSettingsResponse _cachedSettings;
        static DateTime _lastSettingsRefresh = DateTime.MinValue;

        // Shutdown coordination
        static volatile bool _shouldStop;

        // Test execution state
        internal static bool _isRunningTests;
        internal static DateTime _lastTestTime = DateTime.MinValue;
        internal static TestResults _testResults;
        internal static string _currentTestRunId = null;  // Unique ID to track test runs across domain reloads
        static TestCallbacks _testCallbacks;
        // Test execution error state
        // NOTE: Currently not populated due to Unity TestRunner API limitations
        // IErrorCallbacks.OnError is not triggered for compilation errors as expected
        // Infrastructure is ready for future Unity fixes or other error scenarios
        internal static string _testExecutionError = null;
        internal static bool _hasTestExecutionError = false;

        // Play mode state tracking (cached for thread-safe access)
        static bool _isPlaying = false;

        static Server()
        {
            Initialize();
        }

        static void Initialize()
        {
            Cleanup();

            _shouldStop = false;
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

            EditorApplication.quitting += Cleanup;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        static void Cleanup()
        {
            _shouldStop = true;

            if (_listener?.IsListening == true)
            {
                try
                {
                    _listener.Stop();
                }
                catch { }
            }

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
            Debug.Log("[NyamuServer] Restarting server...");
            Initialize();
            Debug.Log($"[NyamuServer] Server restarted on port {NyamuSettings.Instance.serverPort}");
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
            _lastCompileTime = DateTime.Now;
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
        }

        // ========================================================================
        // HTTP SERVER INFRASTRUCTURE
        // ========================================================================

        static void HttpRequestProcessor()
        {
            while (!_shouldStop && _listener?.IsListening == true)
            {
                try
                {
                    var context = _listener.GetContext();
                    ProcessHttpRequest(context);
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
                Constants.Endpoints.RunTests => HandleRunTestsRequest(request),
                Constants.Endpoints.TestStatus => HandleTestStatusRequest(),
                Constants.Endpoints.RefreshAssets => HandleRefreshAssetsRequest(request),
                Constants.Endpoints.EditorStatus => HandleEditorStatusRequest(),
                Constants.Endpoints.McpSettings => HandleMcpSettingsRequest(),
                Constants.Endpoints.CancelTests => HandleCancelTestsRequest(request),
                _ => HandleNotFoundRequest(response)
            };
        }

        static string HandleCompileAndWaitRequest()
        {
            if (NyamuSettings.Instance.enableDebugLogs)
            {
                Debug.Log($"[NyamuServer][Debug] Entering HandleCompileAndWaitRequest");
            }
            _compileRequestTime = DateTime.Now;
            lock (_mainThreadActionQueue)
            {
                _mainThreadActionQueue.Enqueue(() => CompilationPipeline.RequestScriptCompilation());
            }

            var (success, message) = WaitForCompilationToStart(_compileRequestTime, TimeSpan.FromSeconds(Constants.CompileTimeoutSeconds));
            return success ? Constants.JsonResponses.CompileStarted : $"{{\"status\":\"warning\", \"message\":\"{message}\"}}";
        }

        static (bool success, string message) WaitForCompilationToStart(DateTime requestTime, TimeSpan timeout)
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

                if (_lastCompileTime > requestTime)
                    return (true, "Compilation completed quickly.");

                Thread.Sleep(Constants.ThreadSleepMilliseconds);
            }

            return (false, "Compilation may not have started.");
        }

        static string HandleCompileStatusRequest()
        {
            if (NyamuSettings.Instance.enableDebugLogs)
            {
                Debug.Log($"[NyamuServer][Debug] Entering HandleCompileStatusRequest");
            }
            var status = _isCompiling || EditorApplication.isCompiling ? "compiling" : "idle";
            var statusResponse = new CompileStatusResponse
            {
                status = status,
                isCompiling = _isCompiling || EditorApplication.isCompiling,
                lastCompileTime = _lastCompileTime.ToString("yyyy-MM-dd HH:mm:ss"),
                errors = _compilationErrors.ToArray()
            };
            return JsonUtility.ToJson(statusResponse);
        }

        static string HandleRunTestsRequest(HttpListenerRequest request)
        {
            if (NyamuSettings.Instance.enableDebugLogs)
            {
                Debug.Log($"[NyamuServer][Debug] Entering HandleRunTestsRequest");
            }
            var query = request.Url.Query ?? "";
            var mode = ExtractQueryParameter(query, "mode") ?? "EditMode";
            var filter = ExtractQueryParameter(query, "filter");
            var filterRegex = ExtractQueryParameter(query, "filter_regex");

            // Check if tests are already running (non-blocking check)
            lock (_testLock)
            {
                if (_isRunningTests)
                {
                    // Return immediately with warning - don't queue another test run
                    return "{\"status\":\"warning\",\"message\":\"Tests are already running. Please wait for current test run to complete.\"}";
                }

                // Mark test run as starting
                _isRunningTests = true;
            }

            lock (_mainThreadActionQueue)
            {
                _mainThreadActionQueue.Enqueue(() => StartTestExecutionWithRefreshWait(mode, filter, filterRegex));
            }

            return Constants.JsonResponses.TestStarted;
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

        static string HandleTestStatusRequest()
        {
            if (NyamuSettings.Instance.enableDebugLogs)
            {
                Debug.Log($"[NyamuServer][Debug] Entering HandleTestStatusRequest");
            }
            var status = _isRunningTests ? "running" : "idle";
            var statusResponse = new TestStatusResponse
            {
                status = status,
                isRunning = _isRunningTests,
                lastTestTime = _lastTestTime.ToString("yyyy-MM-dd HH:mm:ss"),
                testResults = _testResults,
                testRunId = _currentTestRunId,
                hasError = _hasTestExecutionError,
                errorMessage = _testExecutionError
            };
            return JsonUtility.ToJson(statusResponse);
        }

        static string HandleEditorStatusRequest()
        {
            if (NyamuSettings.Instance.enableDebugLogs)
            {
                Debug.Log($"[NyamuServer][Debug] Entering HandleEditorStatusRequest");
            }
            var statusResponse = new EditorStatusResponse
            {
                isCompiling = _isCompiling || EditorApplication.isCompiling,
                isRunningTests = _isRunningTests,
                isPlaying = _isPlaying
            };
            return JsonUtility.ToJson(statusResponse);
        }

        static string HandleMcpSettingsRequest()
        {
            if (NyamuSettings.Instance.enableDebugLogs)
            {
                Debug.Log($"[NyamuServer][Debug] Entering HandleMcpSettingsRequest");
            }
            lock (_settingsLock)
            {
                if (_cachedSettings == null)
                {
                    // First time access, queue main thread action to load settings
                    McpSettingsResponse settingsResult = null;
                    bool settingsLoaded = false;

                    lock (_mainThreadActionQueue)
                    {
                        _mainThreadActionQueue.Enqueue(() =>
                        {
                            try
                            {
                                var settings = NyamuSettings.Instance;
                                settingsResult = new McpSettingsResponse
                                {
                                    responseCharacterLimit = settings.responseCharacterLimit,
                                    enableTruncation = settings.enableTruncation,
                                    truncationMessage = settings.truncationMessage
                                };
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogError($"[NyamuServer] Failed to load Nyamu settings: {ex.Message}");
                                // Use default settings as fallback
                                settingsResult = new McpSettingsResponse
                                {
                                    responseCharacterLimit = 25000,
                                    enableTruncation = true,
                                    truncationMessage = "\n\n... (response truncated due to length limit)"
                                };
                            }
                            finally
                            {
                                settingsLoaded = true;
                            }
                        });
                    }

                    // Wait for settings to be loaded (with timeout)
                    var timeout = DateTime.Now.AddSeconds(5);
                    while (!settingsLoaded && DateTime.Now < timeout)
                    {
                        Thread.Sleep(50);
                    }

                    if (settingsLoaded && settingsResult != null)
                    {
                        _cachedSettings = settingsResult;
                    }
                    else
                    {
                        // Fallback to default settings if loading failed
                        _cachedSettings = new McpSettingsResponse
                        {
                            responseCharacterLimit = 25000,
                            enableTruncation = true,
                            truncationMessage = "\n\n... (response truncated due to length limit)"
                        };
                    }
                }

                return JsonUtility.ToJson(_cachedSettings);
            }
        }

        static string HandleCancelTestsRequest(HttpListenerRequest request)
        {
            if (NyamuSettings.Instance.enableDebugLogs)
            {
                Debug.Log($"[NyamuServer][Debug] Entering HandleCancelTestsRequest");
            }
            try
            {
                var query = request.Url.Query ?? "";
                var testRunGuid = ExtractQueryParameter(query, "guid");

                // Use provided guid or current test run ID
                var guidToCancel = !string.IsNullOrEmpty(testRunGuid) ? testRunGuid : _currentTestRunId;

                if (string.IsNullOrEmpty(guidToCancel))
                {
                    // Check if tests are running without a stored GUID (edge case)
                    lock (_testLock)
                    {
                        if (_isRunningTests)
                        {
                            return "{\"status\":\"warning\", \"message\":\"Test run is active but no GUID available for cancellation. Provide explicit guid parameter.\"}";
                        }
                    }
                    return "{\"status\":\"error\", \"message\":\"No test run to cancel. Either provide a guid parameter or start a test run first.\"}";
                }

                // Check if we have a test running first
                lock (_testLock)
                {
                    if (!_isRunningTests && guidToCancel == _currentTestRunId)
                    {
                        return "{\"status\":\"warning\", \"message\":\"No test run currently active.\"}";
                    }
                }

                // Try to cancel the test run using Unity's TestRunnerApi
                bool cancelResult = TestRunnerApi.CancelTestRun(guidToCancel);

                if (cancelResult)
                {
                    Debug.Log($"[NyamuServer] Test run cancellation requested for ID: {guidToCancel}");
                    return $"{{\"status\":\"ok\", \"message\":\"Test run cancellation requested for ID: {guidToCancel}\", \"guid\":\"{guidToCancel}\"}}";
                }
                else
                {
                    return $"{{\"status\":\"error\", \"message\":\"Failed to cancel test run with ID: {guidToCancel}. Test run may not exist or may not be cancellable.\", \"guid\":\"{guidToCancel}\"}}";
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NyamuServer] Error cancelling tests: {ex.Message}");
                return $"{{\"status\":\"error\", \"message\":\"Failed to cancel tests: {ex.Message}\"}}";
            }
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
            catch (System.Exception ex)
            {
                Debug.LogError($"[NyamuServer] Failed to refresh cached Nyamu settings: {ex.Message}");
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
                EditorApplication.update -= MonitorRefreshCompletion;
            }
        }

        static string HandleRefreshAssetsRequest(HttpListenerRequest request)
        {
            if (NyamuSettings.Instance.enableDebugLogs)
            {
                Debug.Log($"[NyamuServer][Debug] Entering HandleRefreshAssetsRequest");
            }
            // Parse force parameter from query string
            bool force = request.Url.Query.Contains("force=true");

            // Check if refresh is already in progress (non-blocking check)
            lock (_refreshLock)
            {
                if (_isRefreshing)
                {
                    // Return immediately with warning - don't queue another refresh
                    return "{\"status\":\"warning\",\"message\":\"Asset refresh already in progress. Please wait for current refresh to complete.\"}";
                }

                // Mark refresh as starting
                _isRefreshing = true;
            }

            // Queue the refresh operation on main thread
            lock (_mainThreadActionQueue)
            {
                _mainThreadActionQueue.Enqueue(() =>
                {
                    try
                    {
                        if (force)
                        {
                            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                        }
                        else
                        {
                            AssetDatabase.Refresh();
                        }

                        // Start monitoring for refresh completion using EditorApplication.isUpdating
                        lock (_refreshLock)
                        {
                            if (!_isMonitoringRefresh)
                            {
                                _isMonitoringRefresh = true;
                                _unityIsUpdating = true;  // Assume refresh is starting
                                EditorApplication.update += MonitorRefreshCompletion;
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        // Reset refresh flags immediately if AssetDatabase.Refresh() fails
                        lock (_refreshLock)
                        {
                            _isRefreshing = false;
                            _isMonitoringRefresh = false;
                            _unityIsUpdating = false;
                        }
                        Debug.LogError($"[NyamuServer] AssetDatabase.Refresh failed: {ex.Message}");
                    }
                });
            }

            // Return success response immediately (operation queued)
            return Constants.JsonResponses.AssetsRefreshed;
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
                Debug.LogError($"[NyamuServer] NyamuServer error: {ex.Message}");
        }

        // ========================================================================
        // TEST EXECUTION COORDINATION
        // ========================================================================

        static void StartTestExecutionWithRefreshWait(string mode, string filter, string filterRegex)
        {
            bool executionStarted = false;
            try
            {
                // First, wait for asset refresh to complete if it's in progress
                WaitForAssetRefreshCompletion();

                // Now start the actual test execution
                StartTestExecution(mode, filter, filterRegex);
                executionStarted = true; // If we reach here, execution started successfully
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NyamuServer] Failed to start test execution: {ex.Message}");
            }
            finally
            {
                // Only clear the flag if execution failed to start
                if (!executionStarted)
                {
                    lock (_testLock)
                    {
                        _isRunningTests = false;
                    }
                }
            }
        }

        static void WaitForAssetRefreshCompletion()
        {
            // Wait for asset refresh to complete (similar to WaitForCompilationToStart but simpler)
            int maxWait = 30000; // 30 seconds max wait
            int waited = 0;
            const int sleepInterval = 100; // 100ms intervals

            while (waited < maxWait)
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

                System.Threading.Thread.Sleep(sleepInterval);
                waited += sleepInterval;
            }

            if (waited >= maxWait)
            {
                Debug.LogWarning("[NyamuServer] Timed out waiting for asset refresh to complete before running tests");
            }
        }

        static void StartTestExecution(string mode, string filter, string filterRegex)
        {
            _testResults = null;

            // Reset error state for new test execution
            _testExecutionError = null;
            _hasTestExecutionError = false;

            bool apiExecuteCalled = false;
            try
            {
                var testMode = mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;

                // Override Enter Play Mode settings for PlayMode tests to avoid domain reload
                var originalEnterPlayModeOptionsEnabled = false;
                var originalEnterPlayModeOptions = EnterPlayModeOptions.None;

                if (testMode == TestMode.PlayMode)
                {
                    originalEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
                    originalEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;

                    EditorSettings.enterPlayModeOptionsEnabled = true;
                    EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;

                    Debug.Log("[NyamuServer] Overriding Enter Play Mode settings to disable domain reload for PlayMode tests");
                }

                var api = ScriptableObject.CreateInstance<TestRunnerApi>();

                var filterObj = new Filter
                {
                    testMode = testMode
                };

                if (!string.IsNullOrEmpty(filter))
                {
                    var testNames = filter.Split('|')
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToArray();
                    filterObj.testNames = testNames;
                }

                if (!string.IsNullOrEmpty(filterRegex))
                {
                    filterObj.groupNames = new[] { filterRegex };
                }

                // Store original settings in test callbacks for restoration
                _testCallbacks.SetOriginalPlayModeSettings(testMode == TestMode.PlayMode, originalEnterPlayModeOptionsEnabled, originalEnterPlayModeOptions);

                api.RegisterCallbacks(_testCallbacks);
                _currentTestRunId = api.Execute(new ExecutionSettings(filterObj));
                apiExecuteCalled = true; // If we reach here, api.Execute was called successfully

                Debug.Log($"[NyamuServer] Started test execution with ID: {_currentTestRunId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NyamuServer] Failed to start test execution: {ex.Message}");
                _testResults = new TestResults
                {
                    totalTests = 0,
                    passedTests = 0,
                    failedTests = 1,
                    skippedTests = 0,
                    duration = 0,
                    results = new[] { new TestResult { name = "TestExecution", outcome = "Failed", message = ex.Message, duration = 0 } }
                };
            }
            finally
            {
                // Only clear the flag if api.Execute failed to be called
                if (!apiExecuteCalled)
                {
                    _isRunningTests = false;
                }
            }
        }
    }

    // ============================================================================
    // TEST EXECUTION MANAGEMENT
    // ============================================================================
    // Handles Unity Test Runner callbacks and result collection

    class TestCallbacks : ICallbacks, IErrorCallbacks
    {
        bool _shouldRestorePlayModeSettings;
        bool _originalEnterPlayModeOptionsEnabled;
        EnterPlayModeOptions _originalEnterPlayModeOptions;

        public void SetOriginalPlayModeSettings(bool shouldRestore, bool originalEnabled, EnterPlayModeOptions originalOptions)
        {
            _shouldRestorePlayModeSettings = shouldRestore;
            _originalEnterPlayModeOptionsEnabled = originalEnabled;
            _originalEnterPlayModeOptions = originalOptions;
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            // Reset error state when new test run starts
            Server._testExecutionError = null;
            Server._hasTestExecutionError = false;
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            Debug.Log($"[NyamuServer] Test run finished with status: {result.TestStatus}, ID: {Server._currentTestRunId}");

            var results = new List<TestResult>();
            CollectTestResults(result, results);


            // Update results first, then mark as complete
            Server._testResults = new TestResults
            {
                totalTests = results.Count,
                passedTests = results.Count(r => r.outcome == "Passed"),
                failedTests = results.Count(r => r.outcome == "Failed"),
                skippedTests = results.Count(r => r.outcome == "Skipped"),
                duration = result.Duration,
                results = results.ToArray()
            };

            Server._lastTestTime = DateTime.Now;
            // Mark as complete LAST to ensure results are available
            Server._isRunningTests = false;

            // Restore original Enter Play Mode settings if they were overridden
            if (_shouldRestorePlayModeSettings)
            {
                EditorSettings.enterPlayModeOptionsEnabled = _originalEnterPlayModeOptionsEnabled;
                EditorSettings.enterPlayModeOptions = _originalEnterPlayModeOptions;
                Debug.Log("[NyamuServer] Restored original Enter Play Mode settings after PlayMode test completion");
            }
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
        }

        // NOTE: IErrorCallbacks.OnError methods are implemented but appear to have issues in Unity
        // Testing shows that compilation errors in test assemblies do NOT trigger these callbacks
        // Unity seems to handle compilation errors by excluding broken test classes from execution
        // rather than calling OnError. This may be a Unity TestRunner API bug or limitation.
        // The infrastructure is in place for when/if Unity fixes this behavior.

        public void OnError(string errorDetails)
        {
            Debug.LogError($"[NyamuServer] Test execution error occurred: {errorDetails}");

            // Store error information for status endpoint
            Server._testExecutionError = errorDetails;
            Server._hasTestExecutionError = true;

            // Mark test execution as no longer running since it failed to start
            Server._isRunningTests = false;
        }

        void CollectTestResults(ITestResultAdaptor result, List<TestResult> results)
        {
            // Recursively collect test results from Unity's test hierarchy
            if (result.Test.IsTestAssembly)
            {
                // Assembly level - recurse into child test suites
                foreach (var child in result.Children)
                    CollectTestResults(child, results);
            }
            else if (result.Test.IsSuite)
            {
                // Test suite level - recurse into individual tests
                foreach (var child in result.Children)
                    CollectTestResults(child, results);
            }
            else
            {
                // Individual test - add to results
                results.Add(new TestResult
                {
                    name = result.Test.FullName,
                    outcome = result.TestStatus.ToString(),
                    message = result.Message ?? "",
                    duration = result.Duration
                });
            }
        }
    }
}
