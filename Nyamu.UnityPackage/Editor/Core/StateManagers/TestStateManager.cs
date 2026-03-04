using System;
using Nyamu.Tools.Testing;
using Nyamu.TestExecution;

namespace Nyamu.Core.StateManagers
{
    // Manages test execution state with thread-safe access
    public class TestStateManager
    {
        private readonly object _lock = new();
        private bool _isRunningTests;
        private DateTime _lastTestTime = DateTime.MinValue;
        private TestResults _testResults;
        private string _currentTestRunId;
        private TestCallbacks _testCallbacks;
        private string _testExecutionError;
        private bool _hasTestExecutionError;

        // Test execution progress tracking
        private int _testsTotal;
        private int _testsCompleted;
        private string _currentTestName = "";

        public object Lock => _lock;

        public bool IsRunningTests
        {
            get { lock (_lock) return _isRunningTests; }
            set { lock (_lock) _isRunningTests = value; }
        }

        public DateTime LastTestTime
        {
            get { lock (_lock) return _lastTestTime; }
            set { lock (_lock) _lastTestTime = value; }
        }

        public TestResults TestResults
        {
            get { lock (_lock) return _testResults; }
            set { lock (_lock) _testResults = value; }
        }

        public string CurrentTestRunId
        {
            get { lock (_lock) return _currentTestRunId; }
            set { lock (_lock) _currentTestRunId = value; }
        }

        public TestCallbacks TestCallbacks
        {
            get { lock (_lock) return _testCallbacks; }
            set { lock (_lock) _testCallbacks = value; }
        }

        public string TestExecutionError
        {
            get { lock (_lock) return _testExecutionError; }
            set { lock (_lock) _testExecutionError = value; }
        }

        public bool HasTestExecutionError
        {
            get { lock (_lock) return _hasTestExecutionError; }
            set { lock (_lock) _hasTestExecutionError = value; }
        }

        // Test execution progress (thread-safe access)
        public int TestsTotal
        {
            get { lock (_lock) return _testsTotal; }
            set { lock (_lock) _testsTotal = value; }
        }

        public int TestsCompleted
        {
            get { lock (_lock) return _testsCompleted; }
            set { lock (_lock) _testsCompleted = value; }
        }

        public string CurrentTestName
        {
            get { lock (_lock) return _currentTestName; }
            set { lock (_lock) _currentTestName = value; }
        }
    }
}
