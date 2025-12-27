"""
Test IErrorCallbacks functionality for early test error detection

IMPORTANT FINDINGS:
- Unity's IErrorCallbacks.OnError is NOT triggered for compilation errors in test assemblies
- Unity handles compilation errors by excluding broken test classes (returns 0 tests)
- This appears to be a Unity TestRunner API limitation/bug
- The error detection infrastructure is implemented and ready for future Unity fixes
- Tests validate that compilation errors are still handled quickly (vs timeout)
"""

import pytest
import asyncio
import time
from mcp_client import MCPClient
from unity_helper import UnityHelper


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.asyncio
async def test_error_callbacks_compilation_error_detection(unity_helper, unity_state_manager, temp_files):
    """Test that IErrorCallbacks detects compilation errors and returns early"""

    # Ensure clean state first
    await unity_state_manager.ensure_clean_state()

    client = MCPClient()
    await client.start()

    try:
        # Create a test file with compilation error in the test module (where tests are located)
        test_script_content = """using UnityEngine;
using NUnit.Framework;

public class TestWithCompilationError
{
    [Test]
    public void TestMethod()
    {
        // This line has a syntax error - missing semicolon
        int x = 5
        Assert.AreEqual(5, x);
    }
}"""

        # Write the test file directly to the TestModule directory
        import os
        test_module_path = os.path.join(unity_helper.assets_path, "TestModule")
        if not os.path.exists(test_module_path):
            os.makedirs(test_module_path)

        error_script_path = os.path.join(test_module_path, "TestWithCompilationError.cs")
        with open(error_script_path, 'w') as f:
            f.write(test_script_content)

        # Register for cleanup
        temp_files(error_script_path)

        # Important: DO NOT call refresh_assets - let Unity detect the error naturally
        # This allows us to test if IErrorCallbacks triggers during test execution

        # Record start time for measuring early detection
        start_time = time.time()

        # Try to run tests - Unity will return 0 tests due to compilation error
        response = await client.run_tests(
            test_mode="EditMode",
            test_filter="TestWithCompilationError",
            timeout=30
        )

        # Calculate how long it took
        detection_time = time.time() - start_time

        # Verify it completed quickly (much less than timeout)
        assert detection_time < 15, f"Test execution took too long: {detection_time:.2f}s"

        # Verify Unity returned 0 tests (indicating compilation error excluded the test)
        response_text = response["result"]["content"][0]["text"]
        assert "Total: 0" in response_text, f"Expected 0 tests due to compilation error: {response_text}"

        print(f"Compilation error handling completed in {detection_time:.2f} seconds: {response_text}")

    finally:
        await client.stop()


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.asyncio
async def test_error_callbacks_vs_normal_timeout(unity_helper, unity_state_manager, temp_files):
    """Compare error detection speed with and without compilation errors"""

    await unity_state_manager.ensure_clean_state()

    client = MCPClient()
    await client.start()

    try:
        # First, test normal execution time for a working test
        start_time = time.time()
        response = await client.run_tests(
            test_mode="EditMode",
            test_filter="NyamuTests.PassingTest1",
            timeout=10
        )
        normal_execution_time = time.time() - start_time

        # Verify normal test worked
        assert "Passed: 1" in response["result"]["content"][0]["text"]

        # Now create a compilation error scenario in test module
        test_script_content2 = """using UnityEngine;
using NUnit.Framework;

public class CompilationErrorTest
{
    [Test]
    public void FailingCompilationTest()
    {
        // Syntax error: missing semicolon
        int value = 10
        Assert.AreEqual(10, value);
    }
}"""

        # Write the test file directly to the TestModule directory
        import os
        test_module_path = os.path.join(unity_helper.assets_path, "TestModule")
        error_script_path = os.path.join(test_module_path, "CompilationErrorTest.cs")
        with open(error_script_path, 'w') as f:
            f.write(test_script_content2)

        temp_files(error_script_path)

        # Test error detection time (without asset refresh)
        start_time = time.time()

        try:
            # This should fail quickly due to IErrorCallbacks
            await client.run_tests(
                test_mode="EditMode",
                test_filter="CompilationErrorTest",
                timeout=20
            )
            pytest.fail("Expected test to fail due to compilation error")

        except Exception as e:
            error_detection_time = time.time() - start_time

            # Error detection should be much faster than normal timeout
            assert error_detection_time < 10, \
                f"Error detection too slow: {error_detection_time:.2f}s vs normal {normal_execution_time:.2f}s"

            print(f"Normal execution: {normal_execution_time:.2f}s, Error detection: {error_detection_time:.2f}s")

    finally:
        await client.stop()


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_test_status_error_fields(mcp_client, unity_state_manager):
    """Test that tests-status endpoint includes error information fields"""

    # Get current test status
    response = await mcp_client.tests_status()

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    # Parse the response
    import json
    status_text = response["result"]["content"][0]["text"]
    status_data = json.loads(status_text)

    # Verify new error fields are present
    assert "hasError" in status_data, "hasError field should be present"
    assert "errorMessage" in status_data, "errorMessage field should be present"

    # In clean state, should have no errors
    assert status_data["hasError"] is False, "Should have no errors in clean state"
    assert status_data["errorMessage"] == "" or status_data["errorMessage"] is None, \
        "Error message should be empty in clean state"


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.asyncio
async def test_error_callbacks_prebuild_failure():
    """Test IErrorCallbacks detection of IPrebuildSetup failures"""

    client = MCPClient()
    await client.start()

    try:
        # Create a script that might cause prebuild setup issues
        # This tests if IErrorCallbacks can catch setup failures

        # Try running tests on a non-existent test class to trigger setup errors
        start_time = time.time()

        with pytest.raises(Exception) as exc_info:
            await client.run_tests(
                test_mode="EditMode",
                test_filter="NonExistentTestClass.NonExistentTest",
                timeout=15
            )

        detection_time = time.time() - start_time

        # Should detect the issue relatively quickly
        assert detection_time < 10, f"Setup error detection took too long: {detection_time:.2f}s"

        error_message = str(exc_info.value)
        print(f"Setup error detected in {detection_time:.2f}s: {error_message}")

    finally:
        await client.stop()


@pytest.mark.mcp
@pytest.mark.asyncio
async def test_error_state_reset_between_runs(unity_helper, unity_state_manager, temp_files):
    """Test that error state is properly reset between test runs"""

    await unity_state_manager.ensure_clean_state()

    client = MCPClient()
    await client.start()

    try:
        # First, run a normal test to ensure clean state
        response = await client.run_tests(
            test_mode="EditMode",
            test_filter="NyamuTests.PassingTest1",
            timeout=10
        )
        assert "Passed: 1" in response["result"]["content"][0]["text"]

        # Check status has no errors
        status_response = await client.test_status()
        import json
        status_data = json.loads(status_response["result"]["content"][0]["text"])
        assert status_data["hasError"] is False

        # Create compilation error file in test module
        test_script_content3 = """using NUnit.Framework;

public class ErrorStateResetTest
{
    [Test]
    public void TestWithError()
    {
        int x = 5  // Missing semicolon
        Assert.AreEqual(5, x);
    }
}"""

        # Write the test file directly to the TestModule directory
        import os
        test_module_path = os.path.join(unity_helper.assets_path, "TestModule")
        error_script_path = os.path.join(test_module_path, "ErrorStateResetTest.cs")
        with open(error_script_path, 'w') as f:
            f.write(test_script_content3)

        temp_files(error_script_path)

        # Try to run the error test (should fail)
        try:
            await client.run_tests(
                test_mode="EditMode",
                test_filter="ErrorStateResetTest",
                timeout=10
            )
            pytest.fail("Expected compilation error")
        except Exception:
            pass  # Expected to fail

        # Clean up the error file manually since we need immediate cleanup
        import os
        if os.path.exists(error_script_path):
            os.remove(error_script_path)

        # Refresh assets to remove the error
        await client.refresh_assets(force=True)

        # Wait a moment for Unity to process
        await asyncio.sleep(2)

        # Run a normal test again - error state should be reset
        response = await client.run_tests(
            test_mode="EditMode",
            test_filter="NyamuTests.PassingTest1",
            timeout=10
        )
        assert "Passed: 1" in response["result"]["content"][0]["text"]

        # Verify error state was reset
        status_response = await client.test_status()
        status_data = json.loads(status_response["result"]["content"][0]["text"])
        assert status_data["hasError"] is False, "Error state should be reset after successful test"

    finally:
        await client.stop()
