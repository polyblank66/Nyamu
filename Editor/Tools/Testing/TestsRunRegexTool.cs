using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using Nyamu.TestExecution;

namespace Nyamu.Tools.Testing
{
    // Tool for running Unity tests matching a regex pattern
    public class TestsRunRegexTool : INyamuTool<TestsRunRegexRequest, TestsRunRegexResponse>
    {
        public string Name => "tests_run_regex";

        public Task<TestsRunRegexResponse> ExecuteAsync(
            TestsRunRegexRequest request,
            IExecutionContext context)
        {
            if (string.IsNullOrEmpty(request.testFilterRegex))
            {
                return Task.FromResult(new TestsRunRegexResponse
                {
                    status = "error",
                    message = "filter_regex parameter is required for tests-run-regex endpoint."
                });
            }

            var state = context.TestState;

            // Check if tests are already running
            lock (state.Lock)
            {
                if (state.IsRunningTests)
                {
                    return Task.FromResult(new TestsRunRegexResponse
                    {
                        status = "warning",
                        message = "Tests are already running. Please wait for current test run to complete."
                    });
                }

                // Mark test run as starting
                state.IsRunningTests = true;
            }

            var mode = request.testMode ?? "EditMode";

            // Enqueue test execution on main thread
            context.UnityExecutor.Enqueue(() =>
                context.TestExecutionService.StartTestExecutionWithRefreshWait(mode, null, request.testFilterRegex)
            );

            return Task.FromResult(new TestsRunRegexResponse
            {
                status = "ok",
                message = "Test execution started."
            });
        }
    }
}
