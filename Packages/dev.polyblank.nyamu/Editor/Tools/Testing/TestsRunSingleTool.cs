using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.Testing
{
    // Tool for running a single Unity test by name
    public class TestsRunSingleTool : INyamuTool<TestsRunSingleRequest, TestsRunSingleResponse>
    {
        public string Name => "tests_run_single";

        public Task<TestsRunSingleResponse> ExecuteAsync(
            TestsRunSingleRequest request,
            IExecutionContext context)
        {
            if (string.IsNullOrEmpty(request.testName))
            {
                return Task.FromResult(new TestsRunSingleResponse
                {
                    status = "error",
                    message = "test_name parameter is required for tests-run-single endpoint."
                });
            }

            var state = context.TestState;

            // Check if tests are already running
            lock (state.Lock)
            {
                if (state.IsRunningTests)
                {
                    return Task.FromResult(new TestsRunSingleResponse
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
                Server.StartTestExecutionWithRefreshWait(mode, request.testName, null)
            );

            return Task.FromResult(new TestsRunSingleResponse
            {
                status = "ok",
                message = "Test execution started."
            });
        }
    }
}
