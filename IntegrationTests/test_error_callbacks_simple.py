"""
Test the error detection infrastructure for IErrorCallbacks
"""

import pytest
import asyncio
import time
from mcp_client import MCPClient


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_error_fields_in_test_status(mcp_client):
    """Test that tests-status endpoint includes error fields"""

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
    assert status_data["hasError"] is False
    assert status_data["errorMessage"] == "" or status_data["errorMessage"] is None


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.asyncio
async def test_compilation_error_fast_detection(unity_helper, unity_state_manager, temp_files):
    """Test that compilation errors are detected quickly (not via IErrorCallbacks, but via 0 results)"""

    await unity_state_manager.ensure_clean_state()

    client = MCPClient()
    await client.start()

    try:
        # Create a test file with compilation error
        test_script_content = """using NUnit.Framework;

public class FastErrorDetectionTest
{
    [Test]
    public void TestWithSyntaxError()
    {
        int value = 42  // Missing semicolon
        Assert.AreEqual(42, value);
    }
}"""

        # Write to TestModule directory
        import os
        test_module_path = os.path.join(unity_helper.assets_path, "TestModule")
        if not os.path.exists(test_module_path):
            os.makedirs(test_module_path)

        error_script_path = os.path.join(test_module_path, "FastErrorDetectionTest.cs")
        with open(error_script_path, 'w') as f:
            f.write(test_script_content)

        temp_files(error_script_path)

        # Record start time
        start_time = time.time()

        # Run test - should complete quickly with 0 results
        response = await client.run_tests(
            test_mode="EditMode",
            test_filter="FastErrorDetectionTest",
            timeout=20
        )

        detection_time = time.time() - start_time

        # Should complete much faster than timeout
        assert detection_time < 10, f"Should complete quickly: {detection_time:.2f}s"

        # Should return 0 tests due to compilation error
        response_text = response["result"]["content"][0]["text"]
        assert "Total: 0" in response_text

        print(f"Fast compilation error detection: {detection_time:.2f}s")

    finally:
        await client.stop()


@pytest.mark.mcp
@pytest.mark.asyncio
async def test_error_infrastructure_ready():
    """Test that the error detection infrastructure is properly implemented"""

    # This test verifies our IErrorCallbacks implementation exists and compiles
    # Even if Unity doesn't trigger it for common compilation errors

    client = MCPClient()
    await client.start()

    try:
        # Check that test status includes error fields
        status_response = await client.test_status()

        import json
        status_data = json.loads(status_response["result"]["content"][0]["text"])

        # Verify error infrastructure is in place
        required_fields = ["hasError", "errorMessage", "status", "isRunning"]
        for field in required_fields:
            assert field in status_data, f"Missing required field: {field}"

        # Verify error fields are properly initialized
        assert isinstance(status_data["hasError"], bool)
        assert isinstance(status_data["errorMessage"], (str, type(None)))

        print("Error detection infrastructure verified")

    finally:
        await client.stop()


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.asyncio
async def test_normal_vs_error_execution_speed():
    """Compare normal execution vs compilation error handling speed"""

    client = MCPClient()
    await client.start()

    try:
        # Test normal execution
        start_time = time.time()
        normal_response = await client.tests_run_single(
            test_mode="EditMode",
            test_name="NyamuTests.PassingTest1",
            timeout=15
        )
        normal_time = time.time() - start_time

        # Verify normal test passed
        assert "Passed: 1" in normal_response["result"]["content"][0]["text"]

        # Test with non-existent class (should be fast)
        start_time = time.time()
        error_response = await client.tests_run_single(
            test_mode="EditMode",
            test_name="NonExistentTestClass.NonExistentMethod",
            timeout=15
        )
        error_time = time.time() - start_time

        # Both should complete quickly
        assert normal_time < 10, f"Normal test too slow: {normal_time:.2f}s"
        assert error_time < 10, f"Error case too slow: {error_time:.2f}s"

        # Error case should return 0 tests
        assert "Total: 0" in error_response["result"]["content"][0]["text"]

        print(f"Normal: {normal_time:.2f}s, Error case: {error_time:.2f}s")

    finally:
        await client.stop()
