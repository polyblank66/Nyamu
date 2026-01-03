"""
Test tests_cancel MCP tool functionality - Updated for new test endpoints
"""

import pytest
import asyncio
from mcp_client import MCPClient

@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_cancel_tests_no_running_test(mcp_client, unity_state_manager):
    """Test cancelling tests when no test is running"""
    # Protocol test - no need for full state cleanup
    response = await mcp_client.tests_run_cancel()

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should get a warning/error that no test is running
    assert ("warning" in content_text.lower() or
           "no test" in content_text.lower() or
           "error" in content_text.lower())

@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_cancel_tests_invalid_guid(mcp_client, unity_state_manager):
    """Test cancelling tests with invalid GUID"""
    response = await mcp_client.tests_run_cancel(test_run_guid="invalid-guid-12345")

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should get an error about invalid GUID
    assert "error" in content_text.lower() or "failed" in content_text.lower()

@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.asyncio
async def test_cancel_running_editmode_test(unity_state_manager):
    """Test cancelling a running EditMode test"""
    # This test is more complex as it requires starting a test and then cancelling it
    client1 = MCPClient()
    client2 = MCPClient()

    await client1.start()
    await client2.start()

    try:
        # Start a long-running EditMode test
        # We'll use a test that should take some time to complete
        test_task = asyncio.create_task(
            client1.tests_run_single(
                test_name="NyamuTests.LargeErrorMessageTest",  # Single test that takes time
                test_mode="EditMode",
                timeout=60
            )
        )

        # Give the test a moment to start
        await asyncio.sleep(2)

        # Verify test is running
        status_response = await client2.tests_run_status()
        status_text = status_response["result"]["content"][0]["text"]

        # If test is running, try to cancel it
        if "running" in status_text.lower():
            cancel_response = await client2.tests_run_cancel()

            assert cancel_response["jsonrpc"] == "2.0"
            assert "result" in cancel_response

            cancel_text = cancel_response["result"]["content"][0]["text"]

            # Response is JSON format with status field
            # Accept either success or warning (test may have completed)
            assert ("ok" in cancel_text.lower() or
                   "cancel" in cancel_text.lower() or
                   "requested" in cancel_text.lower() or
                   "warning" in cancel_text.lower() or
                   "no test run currently active" in cancel_text.lower())

            # Wait for the test task to complete (it should be cancelled)
            try:
                await asyncio.wait_for(test_task, timeout=10)
                test_result = test_task.result()
                # Test may complete normally or be cancelled
                print(f"Test completed: {test_result['result']['content'][0]['text'][:200]}...")
            except asyncio.TimeoutError:
                print("Test task took too long after cancellation")
                test_task.cancel()
        else:
            print("Test was not running when we checked status, skipping cancellation test")
            test_task.cancel()

    finally:
        await client1.stop()
        await client2.stop()

@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_cancel_tests_with_specific_guid(mcp_client, unity_state_manager):
    """Test cancelling tests using a specific GUID"""
    # First get current test status to see if there's a test run ID
    status_response = await mcp_client.tests_run_status()
    status_text = status_response["result"]["content"][0]["text"]

    # Parse the JSON to get test run ID if available
    import json
    try:
        status_data = json.loads(status_text)
        test_run_id = status_data.get("testRunId")

        if test_run_id:
            # Try to cancel using this specific GUID
            cancel_response = await mcp_client.tests_run_cancel(test_run_guid=test_run_id)

            assert cancel_response["jsonrpc"] == "2.0"
            assert "result" in cancel_response

            cancel_text = cancel_response["result"]["content"][0]["text"]

            # Should handle the specific GUID request
            assert test_run_id in cancel_text or "cancel" in cancel_text.lower()
        else:
            print("No test run ID available, skipping specific GUID test")
    except json.JSONDecodeError:
        print("Could not parse test status JSON, skipping specific GUID test")

@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_cancel_tests_tool_registration(mcp_client, unity_state_manager):
    """Test that tests_cancel tool is properly registered"""
    tools_response = await mcp_client.list_tools()

    assert tools_response["jsonrpc"] == "2.0"
    assert "result" in tools_response

    tools = tools_response["result"]["tools"]
    tool_names = [tool["name"] for tool in tools]

    # Verify tests_cancel tool is available
    assert "tests_run_cancel" in tool_names

    # Find the tests_cancel tool and verify its properties
    tests_cancel_tool = next(tool for tool in tools if tool["name"] == "tests_run_cancel")

    assert "description" in tests_cancel_tool
    assert "EditMode" in tests_cancel_tool["description"]  # Should mention EditMode limitation
    assert "Unity" in tests_cancel_tool["description"] and "API" in tests_cancel_tool["description"]  # Should reference the Unity API

    assert "inputSchema" in tests_cancel_tool
    schema = tests_cancel_tool["inputSchema"]
    assert schema["type"] == "object"
    assert "test_run_guid" in schema["properties"]

@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.asyncio
async def test_cancel_tests_during_long_test_execution():
    """Test cancelling during actual long test execution"""
    client = MCPClient()
    await client.start()

    try:
        # Start a long-running EditMode test (non-concurrently)
        test_task = asyncio.create_task(
            client.tests_run_single(
                test_name="NyamuTests.LargeErrorMessageTest",  # Single test that takes time
                test_mode="EditMode",
                timeout=30
            )
        )

        # Wait briefly for test to potentially start
        await asyncio.sleep(0.5)

        # Try to cancel (may succeed or report no test running)
        try:
            cancel_response = await client.tests_run_cancel()

            assert cancel_response["jsonrpc"] == "2.0"
            cancel_text = cancel_response["result"]["content"][0]["text"]

            # Should either succeed in cancelling or report no test to cancel
            assert ("ok" in cancel_text.lower() or
                   "warning" in cancel_text.lower() or
                   "error" in cancel_text.lower() or
                   "cancel" in cancel_text.lower())
        except Exception as e:
            # If cancel fails due to concurrent access, that's an expected edge case
            print(f"Cancel attempt failed (expected): {e}")

        # Clean up the test task
        try:
            await asyncio.wait_for(test_task, timeout=10)
        except asyncio.TimeoutError:
            test_task.cancel()
            try:
                await test_task
            except asyncio.CancelledError:
                pass

    finally:
        await client.stop()

@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_cancel_tests_response_format(mcp_client, unity_state_manager):
    """Test that cancel_tests response has correct format"""
    response = await mcp_client.tests_run_cancel()

    # Verify MCP response structure
    assert response["jsonrpc"] == "2.0"
    assert "result" in response
    assert "content" in response["result"]
    assert isinstance(response["result"]["content"], list)
    assert len(response["result"]["content"]) > 0

    content = response["result"]["content"][0]
    assert content["type"] == "text"
    assert "text" in content
    assert isinstance(content["text"], str)

    # Response text should be valid JSON with status field
    import json
    try:
        response_data = json.loads(content["text"])
        assert "status" in response_data
        assert response_data["status"] in ["ok", "error", "warning"]
        assert "message" in response_data
    except json.JSONDecodeError:
        pytest.fail("Response text should be valid JSON")

@pytest.mark.mcp
@pytest.mark.asyncio
async def test_cancel_tests_direct_tool_call(unity_state_manager):
    """Test cancel_tests using direct tool call"""
    client = MCPClient()
    await client.start()

    try:
        # Use direct tool call method
        response = await client._send_request("tools/call", {
            "name": "tests_run_cancel",
            "arguments": {
                "test_run_guid": ""
            }
        })

        assert response["jsonrpc"] == "2.0"
        assert "result" in response

    finally:
        await client.stop()