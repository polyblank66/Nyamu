using System;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using UnityEditor.TestTools.TestRunner.Api;

namespace Nyamu.Tools.Testing
{
    // Tool for cancelling running Unity test execution
    public class TestsCancelTool : INyamuTool<TestsCancelRequest, TestsCancelResponse>
    {
        public string Name => "tests_cancel";

        public Task<TestsCancelResponse> ExecuteAsync(
            TestsCancelRequest request,
            IExecutionContext context)
        {
            try
            {
                var state = context.TestState;

                // Use provided guid or current test run ID
                var guidToCancel = !string.IsNullOrEmpty(request.testRunGuid)
                    ? request.testRunGuid
                    : state.CurrentTestRunId;

                if (string.IsNullOrEmpty(guidToCancel))
                {
                    // Check if tests are running without a stored GUID (edge case)
                    lock (state.Lock)
                    {
                        if (state.IsRunningTests)
                        {
                            return Task.FromResult(new TestsCancelResponse
                            {
                                status = "warning",
                                message = "Test run is active but no GUID available for cancellation. Provide explicit guid parameter.",
                                guid = ""
                            });
                        }
                    }

                    return Task.FromResult(new TestsCancelResponse
                    {
                        status = "error",
                        message = "No test run to cancel. Either provide a guid parameter or start a test run first.",
                        guid = ""
                    });
                }

                // Check if we have a test running first
                lock (state.Lock)
                {
                    if (!state.IsRunningTests && guidToCancel == state.CurrentTestRunId)
                    {
                        return Task.FromResult(new TestsCancelResponse
                        {
                            status = "warning",
                            message = "No test run currently active.",
                            guid = guidToCancel
                        });
                    }
                }

                // Try to cancel the test run using Unity's TestRunnerApi
                bool cancelResult = TestRunnerApi.CancelTestRun(guidToCancel);

                if (cancelResult)
                {
                    return Task.FromResult(new TestsCancelResponse
                    {
                        status = "ok",
                        message = $"Test run cancellation requested for ID: {guidToCancel}",
                        guid = guidToCancel
                    });
                }
                else
                {
                    return Task.FromResult(new TestsCancelResponse
                    {
                        status = "error",
                        message = $"Failed to cancel test run with ID: {guidToCancel}. Test run may not exist or may not be cancellable.",
                        guid = guidToCancel
                    });
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(new TestsCancelResponse
                {
                    status = "error",
                    message = $"Failed to cancel tests: {ex.Message}",
                    guid = ""
                });
            }
        }
    }
}
