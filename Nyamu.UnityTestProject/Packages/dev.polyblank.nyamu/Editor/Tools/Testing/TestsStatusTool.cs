using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.Testing
{
    // Tool for retrieving test execution status without running tests
    public class TestsStatusTool : INyamuTool<TestsStatusRequest, TestStatusResponse>
    {
        public string Name => "tests_status";

        public Task<TestStatusResponse> ExecuteAsync(
            TestsStatusRequest request,
            IExecutionContext context)
        {
            var state = context.TestState;

            string status;
            bool isRunning;
            System.DateTime lastTestTime;
            TestResults testResults;
            string testRunId;
            bool hasError;
            string errorMessage;
            TestProgressInfo progressInfo = null;

            lock (state.Lock)
            {
                isRunning = state.IsRunningTests;
                lastTestTime = state.LastTestTime;
                testResults = state.TestResults;
                testRunId = state.CurrentTestRunId;
                hasError = state.HasTestExecutionError;
                errorMessage = state.TestExecutionError;

                // Get progress info if currently running tests
                if (isRunning && state.TestsTotal > 0)
                {
                    progressInfo = new TestProgressInfo
                    {
                        totalTests = state.TestsTotal,
                        completedTests = state.TestsCompleted,
                        currentTest = state.CurrentTestName
                    };
                }
            }

            status = isRunning ? "running" : "idle";

            var response = new TestStatusResponse
            {
                status = status,
                isRunning = isRunning,
                lastTestTime = lastTestTime.ToString("yyyy-MM-dd HH:mm:ss"),
                testResults = testResults,
                testRunId = testRunId,
                hasError = hasError,
                errorMessage = errorMessage,
                progress = progressInfo
            };

            return Task.FromResult(response);
        }
    }
}
