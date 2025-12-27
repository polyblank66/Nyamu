using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using Nyamu.Tools.Testing;

namespace Nyamu.TestExecution
{
    // Handles Unity Test Runner callbacks and result collection
    public class TestCallbacks : ICallbacks, IErrorCallbacks
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
