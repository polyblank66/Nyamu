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
using System.Threading.Tasks;

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
            public const string CompileAndWait = "/compilation-trigger";
            public const string CompileStatus = "/compilation-status";
            public const string RunTests = "/run-tests";
            public const string TestStatus = "/test-status";
            public const string RefreshAssets = "/refresh-assets";
            public const string EditorStatus = "/editor-status";
            public const string McpSettings = "/mcp-settings";
            public const string CancelTests = "/cancel-tests";
            public const string CompileShader = "/compile-shader";
            public const string CompileAllShaders = "/compile-all-shaders";
            public const string CompileShadersRegex = "/compile-shaders-regex";
            public const string ShaderCompilationStatus = "/shader-compilation-status";
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
        public string lastCompilationTime;
        public string lastCompilationRequestTime;
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

    [System.Serializable]
    public class ShaderCompileError
    {
        public string message;
        public string messageDetails;
        public string file;
        public int line;
        public string platform;
    }

    [System.Serializable]
    public class ShaderMatch
    {
        public string name;
        public string path;
        public int matchScore;
    }

    [System.Serializable]
    public class ShaderCompileResult
    {
        public string shaderName;
        public string shaderPath;
        public bool hasErrors;
        public bool hasWarnings;
        public int errorCount;
        public int warningCount;
        public ShaderCompileError[] errors;
        public ShaderCompileError[] warnings;
        public double compilationTime;
        public string[] targetPlatforms;
    }

    [System.Serializable]
    public class CompileShaderRequest
    {
        public string shaderName;
    }

    [System.Serializable]
    public class CompileShaderResponse
    {
        public string status;
        public string message;
        public ShaderMatch[] allMatches;
        public ShaderMatch bestMatch;
        public ShaderCompileResult result;
    }

    [System.Serializable]
    public class CompileAllShadersResponse
    {
        public string status;
        public int totalShaders;
        public int successfulCompilations;
        public int failedCompilations;
        public double totalCompilationTime;
        public ShaderCompileResult[] results;
    }

    [Serializable]
    public class CompileShadersRegexRequest
    {
        public string pattern;
        public bool async;  // If true, return immediately after queuing compilation
    }

    [Serializable]
    public class ShaderRegexProgressInfo
    {
        public string pattern;
        public int totalShaders;
        public int completedShaders;
        public string currentShader;
    }

    [Serializable]
    public class CompileShadersRegexResponse
    {
        public string status;
        public string message;
        public string pattern;
        public int totalShaders;
        public int successfulCompilations;
        public int failedCompilations;
        public double totalCompilationTime;
        public ShaderCompileResult[] results;
    }

    [Serializable]
    public class NoneResult
    {
    }

    [Serializable]
    public class ShaderCompilationStatusResponse<T>
    {
        public string status;
        public bool isCompiling;
        public string lastCompilationType;
        public string lastCompilationTime;
        public T lastCompilationResult;
        public ShaderRegexProgressInfo progress;  // Progress info for regex compilation (when isCompiling=true)
    }

    // ============================================================================
    // FUZZY MATCHER UTILITY CLASS
    // ============================================================================
    // Implements fuzzy string matching for shader name search

    static class FuzzyMatcher
    {
        public static int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
            if (string.IsNullOrEmpty(target)) return source.Length;

            var n = source.Length;
            var m = target.Length;
            var d = new int[n + 1, m + 1];

            for (var i = 0; i <= n; i++) d[i, 0] = i;
            for (var j = 0; j <= m; j++) d[0, j] = j;

            for (var i = 1; i <= n; i++)
            {
                for (var j = 1; j <= m; j++)
                {
                    var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }

        public static int CalculateMatchScore(string query, string target)
        {
            var queryLower = query.ToLower();
            var targetLower = target.ToLower();

            if (queryLower == targetLower) return 100;

            if (targetLower.Contains(queryLower))
            {
                var baseScore = 90;
                var suffixLength = targetLower.Length - queryLower.Length;
                var suffixPenalty = Math.Min(suffixLength, 15);
                return baseScore - suffixPenalty;
            }

            var distance = LevenshteinDistance(queryLower, targetLower);
            var maxLength = Math.Max(query.Length, target.Length);
            var similarity = 1.0 - ((double)distance / maxLength);
            return (int)(similarity * 80);
        }

        public static List<ShaderMatch> FindBestMatches(string query, string[] shaderNames, string[] shaderPaths, int maxResults = 5)
        {
            var matches = new List<ShaderMatch>();

            for (var i = 0; i < shaderNames.Length; i++)
            {
                var score = CalculateMatchScore(query, shaderNames[i]);
                if (score > 30)
                {
                    matches.Add(new ShaderMatch
                    {
                        name = shaderNames[i],
                        path = shaderPaths[i],
                        matchScore = score
                    });
                }
            }

            matches.Sort((a, b) => b.matchScore.CompareTo(a.matchScore));
            return matches.Take(maxResults).ToList();
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

        // Timestamp cache for thread-safe access
        internal static readonly object _timestampLock = new object();

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
                Constants.Endpoints.CompileShader => HandleCompileShaderRequest(request),
                Constants.Endpoints.CompileAllShaders => HandleCompileAllShadersRequest(request),
                Constants.Endpoints.CompileShadersRegex => HandleCompileShadersRegexRequest(request),
                Constants.Endpoints.ShaderCompilationStatus => HandleShaderCompilationStatusRequest(request),
                _ => HandleNotFoundRequest(response)
            };
        }

        static string HandleCompileAndWaitRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleCompileAndWaitRequest");

            DateTime compileRequestTimeCopy;
            lock (_timestampLock)
            {
                _compileRequestTime = DateTime.Now;
                compileRequestTimeCopy = _compileRequestTime;
            }

            lock (_mainThreadActionQueue)
            {
                _mainThreadActionQueue.Enqueue(() => CompilationPipeline.RequestScriptCompilation());
            }

            var (success, message) = WaitForCompilationToStart(compileRequestTimeCopy, TimeSpan.FromSeconds(Constants.CompileTimeoutSeconds));
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

            var status = _isCompiling || EditorApplication.isCompiling ? "compiling" : "idle";

            DateTime lastCompileTimeCopy, compileRequestTimeCopy;
            lock (_timestampLock)
            {
                lastCompileTimeCopy = _lastCompileTime;
                compileRequestTimeCopy = _compileRequestTime;
            }

            var statusResponse = new CompileStatusResponse
            {
                status = status,
                isCompiling = _isCompiling || EditorApplication.isCompiling,
                lastCompilationTime = lastCompileTimeCopy.ToString("yyyy-MM-dd HH:mm:ss"),
                lastCompilationRequestTime = compileRequestTimeCopy.ToString("yyyy-MM-dd HH:mm:ss"),
                errors = _compilationErrors.ToArray()
            };
            return JsonUtility.ToJson(statusResponse);
        }

        static string HandleRunTestsRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleRunTestsRequest");

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
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleTestStatusRequest");

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

        static string HandleEditorStatusRequest()
        {
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleEditorStatusRequest");
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
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleMcpSettingsRequest");
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
                                NyamuLogger.LogError($"[Nyamu][Server] Failed to load Nyamu settings: {ex.Message}");
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
            NyamuLogger.LogDebug($"[Nyamu][Server] Entering HandleCancelTestsRequest");
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
                    NyamuLogger.LogInfo($"[Nyamu][Server] Test run cancellation requested for ID: {guidToCancel}");
                    return $"{{\"status\":\"ok\", \"message\":\"Test run cancellation requested for ID: {guidToCancel}\", \"guid\":\"{guidToCancel}\"}}";
                }
                else
                {
                    return $"{{\"status\":\"error\", \"message\":\"Failed to cancel test run with ID: {guidToCancel}. Test run may not exist or may not be cancellable.\", \"guid\":\"{guidToCancel}\"}}";
                }
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Error cancelling tests: {ex.Message}");
                return $"{{\"status\":\"error\", \"message\":\"Failed to cancel tests: {ex.Message}\"}}";
            }
        }

        static async Task<ShaderCompileResult> CompileShaderAtPathAsync(string shaderPath)
        {
            var startTime = DateTime.Now;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                return new ShaderCompileResult
                {
                    shaderName = Path.GetFileNameWithoutExtension(shaderPath),
                    shaderPath = shaderPath,
                    hasErrors = true,
                    errorCount = 1,
                    errors = new[] { new ShaderCompileError { message = "Failed to load shader asset", file = shaderPath } }
                };
            }

            ShaderUtil.ClearShaderMessages(shader);
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            // Async wait for compilation using TaskCompletionSource
            var timeout = DateTime.Now.AddSeconds(10);
            var tcs = new TaskCompletionSource<bool>();

            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if (!ShaderUtil.anythingCompiling || DateTime.Now >= timeout)
                {
                    EditorApplication.update -= callback;
                    tcs.TrySetResult(true);
                }
            };

            EditorApplication.update += callback;
            await tcs.Task;

            var compilationTime = (DateTime.Now - startTime).TotalSeconds;
            shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

            var messageCount = ShaderUtil.GetShaderMessageCount(shader);
            var errors = new List<ShaderCompileError>();
            var warnings = new List<ShaderCompileError>();

            for (var i = 0; i < messageCount; i++)
            {
                var msg = ShaderUtil.GetShaderMessages(shader)[i];
                var error = new ShaderCompileError
                {
                    message = msg.message,
                    messageDetails = msg.messageDetails,
                    file = msg.file,
                    line = msg.line,
                    platform = msg.platform.ToString()
                };

                if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    errors.Add(error);
                else if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)
                    warnings.Add(error);
            }

            var platformNames = new List<string> { "Unknown" };

            return new ShaderCompileResult
            {
                shaderName = shader.name,
                shaderPath = shaderPath,
                hasErrors = errors.Count > 0,
                hasWarnings = warnings.Count > 0,
                errorCount = errors.Count,
                warningCount = warnings.Count,
                errors = errors.ToArray(),
                warnings = warnings.ToArray(),
                compilationTime = compilationTime,
                targetPlatforms = platformNames.ToArray()
            };
        }

        // Synchronous version using polling (no deadlock on main thread)
        static ShaderCompileResult CompileShaderAtPath(string shaderPath)
        {
            var startTime = DateTime.Now;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                return new ShaderCompileResult
                {
                    shaderName = Path.GetFileNameWithoutExtension(shaderPath),
                    shaderPath = shaderPath,
                    hasErrors = true,
                    errorCount = 1,
                    errors = new[] { new ShaderCompileError { message = "Failed to load shader asset", file = shaderPath } }
                };
            }

            ShaderUtil.ClearShaderMessages(shader);
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            // Polling approach to avoid deadlock on main thread
            var timeout = DateTime.Now.AddSeconds(10);
            while (ShaderUtil.anythingCompiling && DateTime.Now < timeout)
                Thread.Sleep(50);

            var compilationTime = (DateTime.Now - startTime).TotalSeconds;
            shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

            var messageCount = ShaderUtil.GetShaderMessageCount(shader);
            var errors = new List<ShaderCompileError>();
            var warnings = new List<ShaderCompileError>();

            for (var i = 0; i < messageCount; i++)
            {
                var msg = ShaderUtil.GetShaderMessages(shader)[i];
                var error = new ShaderCompileError
                {
                    message = msg.message,
                    messageDetails = msg.messageDetails,
                    file = msg.file,
                    line = msg.line,
                    platform = msg.platform.ToString()
                };

                if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    errors.Add(error);
                else if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)
                    warnings.Add(error);
            }

            var platformNames = new List<string> { "Unknown" };

            return new ShaderCompileResult
            {
                shaderName = shader.name,
                shaderPath = shaderPath,
                hasErrors = errors.Count > 0,
                hasWarnings = warnings.Count > 0,
                errorCount = errors.Count,
                warningCount = warnings.Count,
                errors = errors.ToArray(),
                warnings = warnings.ToArray(),
                compilationTime = compilationTime,
                targetPlatforms = platformNames.ToArray()
            };
        }

        static CompileShaderResponse CompileSingleShader(string queryName)
        {
            try
            {
                var shaderGuids = AssetDatabase.FindAssets("t:Shader");
                var shaderNames = new List<string>();
                var shaderPaths = new List<string>();

                foreach (var guid in shaderGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (shader != null)
                    {
                        shaderNames.Add(shader.name);
                        shaderPaths.Add(path);
                    }
                }

                var matches = FuzzyMatcher.FindBestMatches(queryName, shaderNames.ToArray(), shaderPaths.ToArray(), 5);

                if (matches.Count == 0)
                {
                    return new CompileShaderResponse
                    {
                        status = "error",
                        message = $"No shaders found matching '{queryName}'",
                        allMatches = new ShaderMatch[0]
                    };
                }

                var bestMatch = matches[0];

                EditorUtility.DisplayProgressBar(
                    "Compiling Shader",
                    $"Compiling: {bestMatch.name}",
                    0.5f
                );

                var compileResult = CompileShaderAtPath(bestMatch.path);

                return new CompileShaderResponse
                {
                    status = compileResult.hasErrors ? "error" : "ok",
                    message = matches.Count > 1
                        ? $"Found {matches.Count} matches. Auto-selected best match: {bestMatch.name}"
                        : $"Compiled shader: {bestMatch.name}",
                    allMatches = matches.ToArray(),
                    bestMatch = bestMatch,
                    result = compileResult
                };
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Shader compilation failed: {ex.Message}");
                return new CompileShaderResponse
                {
                    status = "error",
                    message = $"Shader compilation failed: {ex.Message}"
                };
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static async Task<CompileAllShadersResponse> CompileAllShadersAsync()
        {
            var startTime = DateTime.Now;
            var results = new List<ShaderCompileResult>();

            try
            {
                var shaderGuids = AssetDatabase.FindAssets("t:Shader");
                NyamuLogger.LogInfo($"[Nyamu][Server] Compiling {shaderGuids.Length} shaders...");

                for (var i = 0; i < shaderGuids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(shaderGuids[i]);
                    NyamuLogger.LogInfo($"[Nyamu][Server] Compiling shader {i + 1}/{shaderGuids.Length}: {path}");

                    var result = await CompileShaderAtPathAsync(path);
                    results.Add(result);

                    // Yield to allow Unity to process events
                    await Task.Yield();
                }

                var totalTime = (DateTime.Now - startTime).TotalSeconds;
                var successCount = results.Count(r => !r.hasErrors);
                var failCount = results.Count(r => r.hasErrors);

                return new CompileAllShadersResponse
                {
                    status = failCount > 0 ? "warning" : "ok",
                    totalShaders = results.Count,
                    successfulCompilations = successCount,
                    failedCompilations = failCount,
                    totalCompilationTime = totalTime,
                    results = results.ToArray()
                };
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Compile all shaders failed: {ex.Message}");
                return new CompileAllShadersResponse
                {
                    status = "error",
                    totalShaders = 0,
                    successfulCompilations = 0,
                    failedCompilations = 0,
                    totalCompilationTime = 0,
                    results = new ShaderCompileResult[0]
                };
            }
        }

        // Synchronous version using polling (no deadlock on main thread)
        static CompileAllShadersResponse CompileAllShaders()
        {
            var startTime = DateTime.Now;
            var results = new List<ShaderCompileResult>();

            try
            {
                var shaderGuids = AssetDatabase.FindAssets("t:Shader");
                NyamuLogger.LogInfo($"[Nyamu][Server] Compiling {shaderGuids.Length} shaders...");

                for (var i = 0; i < shaderGuids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(shaderGuids[i]);
                    NyamuLogger.LogInfo($"[Nyamu][Server] Compiling shader {i + 1}/{shaderGuids.Length}: {path}");

                    var result = CompileShaderAtPath(path);
                    results.Add(result);

                    EditorUtility.DisplayProgressBar(
                        "Compiling Shaders",
                        $"Compiling shader {i + 1}/{shaderGuids.Length}: {Path.GetFileName(path)}",
                        (float)(i + 1) / shaderGuids.Length
                    );
                }

                var totalTime = (DateTime.Now - startTime).TotalSeconds;
                var successCount = results.Count(r => !r.hasErrors);
                var failCount = results.Count(r => r.hasErrors);

                return new CompileAllShadersResponse
                {
                    status = failCount > 0 ? "warning" : "ok",
                    totalShaders = results.Count,
                    successfulCompilations = successCount,
                    failedCompilations = failCount,
                    totalCompilationTime = totalTime,
                    results = results.ToArray()
                };
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Compile all shaders failed: {ex.Message}");
                return new CompileAllShadersResponse
                {
                    status = "error",
                    totalShaders = 0,
                    successfulCompilations = 0,
                    failedCompilations = 0,
                    totalCompilationTime = 0,
                    results = new ShaderCompileResult[0]
                };
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static async Task<CompileShadersRegexResponse> CompileShadersRegexAsync(string pattern)
        {
            try
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern);
                var allShaderGuids = AssetDatabase.FindAssets("t:Shader");
                var matchingShaders = new List<string>();

                foreach (var guid in allShaderGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (regex.IsMatch(path))
                        matchingShaders.Add(path);
                }

                if (matchingShaders.Count == 0)
                {
                    return new CompileShadersRegexResponse
                    {
                        status = "ok",
                        message = "No shaders matched the pattern",
                        pattern = pattern,
                        totalShaders = 0,
                        successfulCompilations = 0,
                        failedCompilations = 0,
                        totalCompilationTime = 0,
                        results = new ShaderCompileResult[0]
                    };
                }

                NyamuLogger.LogInfo($"[Nyamu][Server] Compiling {matchingShaders.Count} shaders matching pattern: {pattern}");

                var results = new List<ShaderCompileResult>();
                var startTime = DateTime.Now;
                var successCount = 0;
                var failCount = 0;

                for (var i = 0; i < matchingShaders.Count; i++)
                {
                    var shaderPath = matchingShaders[i];
                    NyamuLogger.LogInfo($"[Nyamu][Server] Compiling shader {i + 1}/{matchingShaders.Count}: {shaderPath}");

                    var result = await CompileShaderAtPathAsync(shaderPath);
                    results.Add(result);

                    if (result.hasErrors)
                        failCount++;
                    else
                        successCount++;

                    await Task.Yield();
                }

                var totalTime = (DateTime.Now - startTime).TotalSeconds;

                return new CompileShadersRegexResponse
                {
                    status = failCount > 0 ? "warning" : "ok",
                    message = $"Compiled {results.Count} shaders matching pattern",
                    pattern = pattern,
                    totalShaders = results.Count,
                    successfulCompilations = successCount,
                    failedCompilations = failCount,
                    totalCompilationTime = totalTime,
                    results = results.ToArray()
                };
            }
            catch (Exception ex)
            {
                return new CompileShadersRegexResponse
                {
                    status = "error",
                    message = $"Failed to compile shaders: {ex.Message}",
                    pattern = pattern,
                    totalShaders = 0,
                    successfulCompilations = 0,
                    failedCompilations = 0,
                    totalCompilationTime = 0,
                    results = new ShaderCompileResult[0]
                };
            }
        }

        // Synchronous version using polling (no deadlock on main thread)
        static CompileShadersRegexResponse CompileShadersRegex(string pattern)
        {
            try
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern);
                var allShaderGuids = AssetDatabase.FindAssets("t:Shader");
                var matchingShaders = new List<string>();

                foreach (var guid in allShaderGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (regex.IsMatch(path))
                        matchingShaders.Add(path);
                }

                if (matchingShaders.Count == 0)
                {
                    return new CompileShadersRegexResponse
                    {
                        status = "ok",
                        message = "No shaders matched the pattern",
                        pattern = pattern,
                        totalShaders = 0,
                        successfulCompilations = 0,
                        failedCompilations = 0,
                        totalCompilationTime = 0,
                        results = new ShaderCompileResult[0]
                    };
                }

                NyamuLogger.LogInfo($"[Nyamu][Server] Compiling {matchingShaders.Count} shaders matching pattern: {pattern}");

                var results = new List<ShaderCompileResult>();
                var startTime = DateTime.Now;
                var successCount = 0;
                var failCount = 0;

                // Initialize progress tracking
                lock (_shaderCompilationResultLock)
                {
                    _regexShadersPattern = pattern;
                    _regexShadersTotal = matchingShaders.Count;
                    _regexShadersCompleted = 0;
                    _regexShadersCurrentShader = "";
                }

                for (var i = 0; i < matchingShaders.Count; i++)
                {
                    var shaderPath = matchingShaders[i];
                    NyamuLogger.LogInfo($"[Nyamu][Server] Compiling shader {i + 1}/{matchingShaders.Count}: {shaderPath}");

                    // Update progress tracking
                    lock (_shaderCompilationResultLock)
                    {
                        _regexShadersCompleted = i;
                        _regexShadersCurrentShader = shaderPath;
                    }

                    var result = CompileShaderAtPath(shaderPath);
                    results.Add(result);

                    if (result.hasErrors)
                        failCount++;
                    else
                        successCount++;

                    EditorUtility.DisplayProgressBar(
                        "Compiling Shaders (Regex)",
                        $"Compiling shader {i + 1}/{matchingShaders.Count}: {Path.GetFileName(shaderPath)}",
                        (float)(i + 1) / matchingShaders.Count
                    );
                }

                var totalTime = (DateTime.Now - startTime).TotalSeconds;

                // Mark progress as complete
                lock (_shaderCompilationResultLock)
                {
                    _regexShadersCompleted = matchingShaders.Count;
                    _regexShadersCurrentShader = "";
                }

                return new CompileShadersRegexResponse
                {
                    status = failCount > 0 ? "warning" : "ok",
                    message = $"Compiled {results.Count} shaders matching pattern",
                    pattern = pattern,
                    totalShaders = results.Count,
                    successfulCompilations = successCount,
                    failedCompilations = failCount,
                    totalCompilationTime = totalTime,
                    results = results.ToArray()
                };
            }
            catch (Exception ex)
            {
                return new CompileShadersRegexResponse
                {
                    status = "error",
                    message = $"Failed to compile shaders: {ex.Message}",
                    pattern = pattern,
                    totalShaders = 0,
                    successfulCompilations = 0,
                    failedCompilations = 0,
                    totalCompilationTime = 0,
                    results = new ShaderCompileResult[0]
                };
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static string HandleCompileShaderRequest(HttpListenerRequest request)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileShaderRequest");

            lock (_shaderCompileLock)
            {
                if (_isCompilingShaders)
                    return "{\"status\":\"warning\",\"message\":\"Shader compilation already in progress.\"}";
                _isCompilingShaders = true;
            }

            lock (_shaderCompilationResultLock)
            {
                _lastSingleShaderResult = null;
                _lastAllShadersResult = null;
                _lastRegexShadersResult = null;
            }

            string shaderName = null;
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    var body = reader.ReadToEnd();
                    var requestData = JsonUtility.FromJson<CompileShaderRequest>(body);
                    shaderName = requestData?.shaderName;
                }
            }
            catch
            {
                lock (_shaderCompileLock) { _isCompilingShaders = false; }
                return "{\"status\":\"error\",\"message\":\"Invalid request body.\"}";
            }

            if (string.IsNullOrEmpty(shaderName))
            {
                lock (_shaderCompileLock) { _isCompilingShaders = false; }
                return "{\"status\":\"error\",\"message\":\"Shader name is required.\"}";
            }

            CompileShaderResponse response = null;
            lock (_mainThreadActionQueue)
            {
                _mainThreadActionQueue.Enqueue(() =>
                {
                    response = CompileSingleShader(shaderName);

                    lock (_shaderCompilationResultLock)
                    {
                        _lastSingleShaderResult = response;
                        _lastAllShadersResult = null;
                        _lastRegexShadersResult = null;
                        _lastShaderCompilationType = "single";
                        _lastShaderCompilationTime = DateTime.Now;
                    }

                    lock (_shaderCompileLock) { _isCompilingShaders = false; }
                });
            }

            var timeout = DateTime.Now.AddSeconds(30);
            while (response == null && DateTime.Now < timeout)
                Thread.Sleep(100);

            if (response != null)
                return JsonUtility.ToJson(response);

            return "{\"status\":\"error\",\"message\":\"Timeout.\"}";
        }

        static string HandleCompileAllShadersRequest(HttpListenerRequest _)
        {
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleCompileAllShadersRequest");

            lock (_shaderCompileLock)
            {
                if (_isCompilingShaders)
                    return "{\"status\":\"warning\",\"message\":\"Shader compilation already in progress.\"}";
                _isCompilingShaders = true;
            }

            lock (_shaderCompilationResultLock)
            {
                _lastSingleShaderResult = null;
                _lastAllShadersResult = null;
                _lastRegexShadersResult = null;
            }

            CompileAllShadersResponse response = null;
            lock (_mainThreadActionQueue)
            {
                _mainThreadActionQueue.Enqueue(() =>
                {
                    response = CompileAllShaders();

                    lock (_shaderCompilationResultLock)
                    {
                        _lastSingleShaderResult = null;
                        _lastAllShadersResult = response;
                        _lastRegexShadersResult = null;
                        _lastShaderCompilationType = "all";
                        _lastShaderCompilationTime = DateTime.Now;
                    }

                    lock (_shaderCompileLock) { _isCompilingShaders = false; }
                });
            }

            var timeout = DateTime.Now.AddSeconds(120);
            while (response == null && DateTime.Now < timeout)
                Thread.Sleep(100);

            if (response != null)
                return JsonUtility.ToJson(response);

            return "{\"status\":\"error\",\"message\":\"Timeout.\"}";
        }

        static string HandleCompileShadersRegexRequest(HttpListenerRequest request)
        {
            if (request.HttpMethod != "POST")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use POST.\"}";

            lock (_shaderCompileLock)
            {
                if (_isCompilingShaders)
                    return "{\"status\":\"warning\",\"message\":\"Shader compilation already in progress.\"}";
                _isCompilingShaders = true;
            }

            lock (_shaderCompilationResultLock)
            {
                _lastSingleShaderResult = null;
                _lastAllShadersResult = null;
                _lastRegexShadersResult = null;
            }

            CompileShadersRegexRequest requestData = null;
            try
            {
                var bodyText = new StreamReader(request.InputStream).ReadToEnd();
                requestData = JsonUtility.FromJson<CompileShadersRegexRequest>(bodyText);
            }
            catch
            {
                lock (_shaderCompileLock) { _isCompilingShaders = false; }
                return "{\"status\":\"error\",\"message\":\"Invalid request body.\"}";
            }

            if (string.IsNullOrEmpty(requestData?.pattern))
            {
                lock (_shaderCompileLock) { _isCompilingShaders = false; }
                return "{\"status\":\"error\",\"message\":\"Missing required parameter: pattern\"}";
            }

            // Check if async mode is requested
            if (requestData.async)
            {
                // Async mode: queue compilation and return immediately
                lock (_mainThreadActionQueue)
                {
                    _mainThreadActionQueue.Enqueue(() =>
                    {
                        var result = CompileShadersRegex(requestData.pattern);

                        lock (_shaderCompilationResultLock)
                        {
                            _lastSingleShaderResult = null;
                            _lastAllShadersResult = null;
                            _lastRegexShadersResult = result;
                            _lastShaderCompilationType = "regex";
                            _lastShaderCompilationTime = DateTime.Now;
                        }

                        lock (_shaderCompileLock) { _isCompilingShaders = false; }
                    });
                }

                return "{\"status\":\"ok\",\"message\":\"Shader compilation started.\"}";
            }

            // Blocking mode: wait for compilation to complete
            CompileShadersRegexResponse response = null;
            lock (_mainThreadActionQueue)
            {
                _mainThreadActionQueue.Enqueue(() =>
                {
                    response = CompileShadersRegex(requestData.pattern);

                    lock (_shaderCompilationResultLock)
                    {
                        _lastSingleShaderResult = null;
                        _lastAllShadersResult = null;
                        _lastRegexShadersResult = response;
                        _lastShaderCompilationType = "regex";
                        _lastShaderCompilationTime = DateTime.Now;
                    }

                    lock (_shaderCompileLock) { _isCompilingShaders = false; }
                });
            }

            var timeout = DateTime.Now.AddSeconds(120);
            while (response == null && DateTime.Now < timeout)
                Thread.Sleep(100);

            if (response != null)
                return JsonUtility.ToJson(response);

            return "{\"status\":\"error\",\"message\":\"Timeout.\"}";
        }

        static string HandleShaderCompilationStatusRequest(HttpListenerRequest request)
        {
            if (request.HttpMethod != "GET")
                return "{\"status\":\"error\",\"message\":\"Method not allowed. Use GET.\"}";

            lock (_shaderCompileLock)
            {
                var status = _isCompilingShaders ? "compiling" : "idle";

                CompileShaderResponse singleResult;
                CompileAllShadersResponse allResult;
                CompileShadersRegexResponse regexResult;
                string typeCopy;
                DateTime timeCopy;

                lock (_shaderCompilationResultLock)
                {
                    singleResult = _lastSingleShaderResult;
                    allResult = _lastAllShadersResult;
                    regexResult = _lastRegexShadersResult;
                    typeCopy = _lastShaderCompilationType;
                    timeCopy = _lastShaderCompilationTime;
                }

                var timeString = timeCopy.ToString("yyyy-MM-dd HH:mm:ss");

                if (singleResult != null)
                {
                    var response = new ShaderCompilationStatusResponse<CompileShaderResponse>
                    {
                        status = status,
                        isCompiling = _isCompilingShaders,
                        lastCompilationType = typeCopy,
                        lastCompilationTime = timeString,
                        lastCompilationResult = singleResult
                    };
                    return JsonUtility.ToJson(response);
                }

                if (allResult != null)
                {
                    var response = new ShaderCompilationStatusResponse<CompileAllShadersResponse>
                    {
                        status = status,
                        isCompiling = _isCompilingShaders,
                        lastCompilationType = typeCopy,
                        lastCompilationTime = timeString,
                        lastCompilationResult = allResult
                    };
                    return JsonUtility.ToJson(response);
                }

                if (regexResult != null)
                {
                    var response = new ShaderCompilationStatusResponse<CompileShadersRegexResponse>
                    {
                        status = status,
                        isCompiling = _isCompilingShaders,
                        lastCompilationType = typeCopy,
                        lastCompilationTime = timeString,
                        lastCompilationResult = regexResult
                    };

                    // Add progress info if currently compiling regex shaders
                    if (_isCompilingShaders && typeCopy == "regex")
                    {
                        lock (_shaderCompilationResultLock)
                        {
                            response.progress = new ShaderRegexProgressInfo
                            {
                                pattern = _regexShadersPattern,
                                totalShaders = _regexShadersTotal,
                                completedShaders = _regexShadersCompleted,
                                currentShader = _regexShadersCurrentShader
                            };
                        }
                    }

                    return JsonUtility.ToJson(response);
                }

                // No results available
                var noneResponse = new ShaderCompilationStatusResponse<NoneResult>
                {
                    status = status,
                    isCompiling = _isCompilingShaders,
                    lastCompilationType = typeCopy,
                    lastCompilationTime = timeString,
                    lastCompilationResult = new NoneResult()
                };
                return JsonUtility.ToJson(noneResponse);
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
            NyamuLogger.LogDebug("[Nyamu][Server] Entering HandleRefreshAssetsRequest");
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
                        NyamuLogger.LogError($"[Nyamu][Server] AssetDatabase.Refresh failed: {ex.Message}");
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
                NyamuLogger.LogError($"[Nyamu][Server] NyamuServer error: {ex.Message}");
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
                NyamuLogger.LogError($"[Nyamu][Server] Failed to start test execution: {ex.Message}");
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
                NyamuLogger.LogWarning("[Nyamu][Server] Timed out waiting for asset refresh to complete before running tests");
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

                    NyamuLogger.LogInfo("[Nyamu][Server] Overriding Enter Play Mode settings to disable domain reload for PlayMode tests");
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

                NyamuLogger.LogInfo($"[Nyamu][Server] Started test execution with ID: {_currentTestRunId}");
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Failed to start test execution: {ex.Message}");
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
            NyamuLogger.LogInfo($"[Nyamu][Server] Test run finished with status: {result.TestStatus}, ID: {Server._currentTestRunId}");

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

            lock (Server._timestampLock)
            {
                Server._lastTestTime = DateTime.Now;
            }

            // Save cache after test run completes
            Server.SaveTimestampsCache();

            // Mark as complete LAST to ensure results are available
            Server._isRunningTests = false;

            // Restore original Enter Play Mode settings if they were overridden
            if (_shouldRestorePlayModeSettings)
            {
                EditorSettings.enterPlayModeOptionsEnabled = _originalEnterPlayModeOptionsEnabled;
                EditorSettings.enterPlayModeOptions = _originalEnterPlayModeOptions;
                NyamuLogger.LogInfo("[Nyamu][Server] Restored original Enter Play Mode settings after PlayMode test completion");
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
            NyamuLogger.LogError($"[Nyamu][Server] Test execution error occurred: {errorDetails}");

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
