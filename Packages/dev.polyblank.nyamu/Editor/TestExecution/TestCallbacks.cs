using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using Nyamu.Core.StateManagers;
using Nyamu.Tools.Testing;

namespace Nyamu.TestExecution
{
    // Handles Unity Test Runner callbacks and result collection
    public class TestCallbacks : ICallbacks, IErrorCallbacks
    {
        readonly TestStateManager _state;
        readonly object _timestampLock;

        bool _shouldRestorePlayModeSettings;
        bool _originalEnterPlayModeOptionsEnabled;
        EnterPlayModeOptions _originalEnterPlayModeOptions;

        public TestCallbacks(TestStateManager state, object timestampLock)
        {
            _state = state;
            _timestampLock = timestampLock;
        }

        public void SetOriginalPlayModeSettings(bool shouldRestore, bool originalEnabled, EnterPlayModeOptions originalOptions)
        {
            _shouldRestorePlayModeSettings = shouldRestore;
            _originalEnterPlayModeOptionsEnabled = originalEnabled;
            _originalEnterPlayModeOptions = originalOptions;
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            // Reset error state when new test run starts
            _state.TestExecutionError = null;
            _state.HasTestExecutionError = false;

            // Initialize progress tracking
            int total = CountTests(testsToRun);
            NyamuLogger.LogInfo($"[Nyamu][TestCallbacks] RunStarted - Total tests: {total}");
            lock (_state.Lock)
            {
                _state.TestsTotal = total;
                _state.TestsCompleted = 0;
                _state.CurrentTestName = "";
            }
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            NyamuLogger.LogInfo($"[Nyamu][Server] Test run finished with status: {result.TestStatus}, ID: {_state.CurrentTestRunId}");

            var results = new List<TestResult>();
            CollectTestResults(result, results);


            // Update results first, then mark as complete
            _state.TestResults = new TestResults
            {
                totalTests = results.Count,
                passedTests = results.Count(r => r.outcome == "Passed"),
                failedTests = results.Count(r => r.outcome == "Failed"),
                skippedTests = results.Count(r => r.outcome == "Skipped"),
                duration = result.Duration,
                results = results.ToArray()
            };

            lock (_timestampLock)
            {
                _state.LastTestTime = DateTime.Now;
            }

            // Save cache after test run completes
            Server.SaveTimestampsCache();

            // Mark as complete LAST to ensure results are available
            _state.IsRunningTests = false;

            // Restore original Enter Play Mode settings if they were overridden
            if (_shouldRestorePlayModeSettings)
            {
                EditorSettings.enterPlayModeOptionsEnabled = _originalEnterPlayModeOptionsEnabled;
                EditorSettings.enterPlayModeOptions = _originalEnterPlayModeOptions;
                NyamuLogger.LogInfo("[Nyamu][Server] Restored original Enter Play Mode settings after PlayMode test completion");
            }

            // Clear progress tracking
            lock (_state.Lock)
            {
                _state.TestsTotal = 0;
                _state.TestsCompleted = 0;
                _state.CurrentTestName = "";
            }
        }

        public void TestStarted(ITestAdaptor test)
        {
            // Update progress tracking
            lock (_state.Lock)
            {
                // Only count actual tests (leaf nodes without children), not test suites or assemblies
                // This matches the logic in CountTests()
                if (!test.HasChildren)
                {
                    _state.TestsCompleted++;
                    _state.CurrentTestName = test.FullName;
                    NyamuLogger.LogInfo($"[Nyamu][TestCallbacks] TestStarted - {_state.TestsCompleted}/{_state.TestsTotal}: {test.FullName}");
                }
            }
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
            _state.TestExecutionError = errorDetails;
            _state.HasTestExecutionError = true;

            // Mark test execution as no longer running since it failed to start
            _state.IsRunningTests = false;
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

        int CountTests(ITestAdaptor test)
        {
            if (!test.HasChildren) return test.IsSuite ? 0 : 1;

            int count = 0;
            foreach (var child in test.Children)
                count += CountTests(child);
            return count;
        }
    }
}
