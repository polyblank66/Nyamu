using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using Nyamu.TestExecution;

namespace Nyamu.Tools.Testing
{
    // Tool for running all Unity tests in a mode (EditMode or PlayMode)
    public class TestsRunAllTool : INyamuTool<TestsRunAllRequest, TestsRunAllResponse>
    {
        public string Name => "tests_run_all";

        public Task<TestsRunAllResponse> ExecuteAsync(
            TestsRunAllRequest request,
            IExecutionContext context)
        {
            var state = context.TestState;

            // Check if tests are already running
            lock (state.Lock)
            {
                if (state.IsRunningTests)
                {
                    return Task.FromResult(new TestsRunAllResponse
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
                TestExecutionService.StartTestExecutionWithRefreshWait(mode, null, null)
            );

            return Task.FromResult(new TestsRunAllResponse
            {
                status = "ok",
                message = "Test execution started."
            });
        }
    }
}
