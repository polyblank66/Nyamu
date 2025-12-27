using System;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Nyamu.Core.StateManagers;
using Nyamu.Tools.Testing;

namespace Nyamu.TestExecution
{
    // Coordinates test execution with asset refresh
    public class TestExecutionService
    {
        readonly TestStateManager _testState;
        readonly AssetStateManager _assetState;
        readonly TestCallbacks _callbacks;

        public TestExecutionService(
            TestStateManager testState,
            AssetStateManager assetState,
            TestCallbacks callbacks)
        {
            _testState = testState;
            _assetState = assetState;
            _callbacks = callbacks;
        }

        public void StartTestExecutionWithRefreshWait(string mode, string filter, string filterRegex)
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
                    lock (_testState.Lock)
                    {
                        _testState.IsRunningTests = false;
                    }
                }
            }
        }

        void WaitForAssetRefreshCompletion()
        {
            // Wait for asset refresh to complete (similar to WaitForCompilationToStart but simpler)
            int maxWait = 30000; // 30 seconds max wait
            int waited = 0;
            const int sleepInterval = 100; // 100ms intervals

            while (waited < maxWait)
            {
                // Check both our flag and Unity's cached refresh state (thread-safe)
                bool refreshInProgress, unityIsUpdating;
                lock (_assetState.Lock)
                {
                    refreshInProgress = _assetState.IsRefreshing;
                    unityIsUpdating = _assetState.UnityIsUpdating;
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

        void StartTestExecution(string mode, string filter, string filterRegex)
        {
            _testState.TestResults = null;

            // Reset error state for new test execution
            _testState.TestExecutionError = null;
            _testState.HasTestExecutionError = false;

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
                _callbacks.SetOriginalPlayModeSettings(testMode == TestMode.PlayMode, originalEnterPlayModeOptionsEnabled, originalEnterPlayModeOptions);

                api.RegisterCallbacks(_callbacks);
                _testState.CurrentTestRunId = api.Execute(new ExecutionSettings(filterObj));
                apiExecuteCalled = true; // If we reach here, api.Execute was called successfully

                NyamuLogger.LogInfo($"[Nyamu][Server] Started test execution with ID: {_testState.CurrentTestRunId}");
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][Server] Failed to start test execution: {ex.Message}");
                _testState.TestResults = new TestResults
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
                    _testState.IsRunningTests = false;
                }
            }
        }
    }
}
