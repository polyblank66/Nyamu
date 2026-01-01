"""
Test run_tests MCP tool - Updated for new test endpoints
"""

import pytest
from mcp_client import MCPClient

@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_edit_mode(mcp_client, unity_state_manager):
    """Test running EditMode tests using tests_run_all"""
    response = await mcp_client.tests_run_all(test_mode="EditMode", timeout=60)

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    result = response["result"]
    assert "content" in result
    assert isinstance(result["content"], list)
    assert len(result["content"]) > 0

    content = result["content"][0]
    assert content["type"] == "text"
    assert "text" in content

    text = content["text"]
    assert "Test Results:" in text

@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_play_mode(mcp_client, unity_state_manager):
    """Test running PlayMode tests using tests_run_all"""
    response = await mcp_client.tests_run_all(test_mode="PlayMode", timeout=60)

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    result = response["result"]
    content_text = result["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text

@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_with_filter(mcp_client, unity_state_manager):
    """Test running tests with regex filter using tests_run_regex"""
    response = await mcp_client.tests_run_regex(
        test_mode="EditMode",
        test_filter_regex=".*TestSample.*",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_default_parameters(mcp_client, unity_state_manager):
    """Test tests_run_all with default parameters"""
    # Ensure Unity is fully ready for test execution
    print("Ensuring Unity is in clean state before running tests...")
    await unity_state_manager.ensure_clean_state()

    # Give Unity additional time to be ready for test execution
    import asyncio
    await asyncio.sleep(2.0)

    # This will run EditMode tests by default with longer timeout
    response = await mcp_client.tests_run_all(timeout=60)

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_invalid_mode(mcp_client, unity_state_manager):
    """Test tests_run_all with invalid test mode"""
    # Use tests_run_all method which has retry logic instead of direct _send_request
    response = await mcp_client.tests_run_all(test_mode="InvalidMode", timeout=30)

    # Should handle gracefully - either error or default to valid mode
    assert response["jsonrpc"] == "2.0"

@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_timeout(unity_state_manager):
    """Test tests_run_all with very short timeout"""
    client = MCPClient()
    await client.start()

    try:
        # Test with very short timeout - may timeout or complete quickly depending on Unity state
        # The test runner might complete faster than expected in some cases
        try:
            response = await client.tests_run_all(timeout=1)
            # If it succeeds, it just means the test runner was very fast
            assert response["jsonrpc"] == "2.0"
            print("Test completed within timeout (test runner was fast)")
        except RuntimeError as exc_info:
            # If it times out, verify the error message
            assert "timeout" in str(exc_info).lower() or "unity server timeout" in str(exc_info).lower()
            print(f"Test timed out as expected: {exc_info}")

    finally:
        await client.stop()

@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_direct_tool_call(unity_state_manager):
    """Test tests_run_all using direct tool call with retry logic"""
    client = MCPClient()
    await client.start()

    try:
        # Use tests_run_all method which has retry logic for -32603 errors
        response = await client.tests_run_all(
            test_mode="EditMode",
            timeout=30
        )

        assert response["jsonrpc"] == "2.0"
        assert "result" in response

    finally:
        await client.stop()

@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_results_format(unity_state_manager):
    """Test that test results have expected format"""
    client = MCPClient()
    await client.start()

    try:
        response = await client.tests_run_all(test_mode="EditMode", timeout=60)

        assert response["jsonrpc"] == "2.0"
        assert "result" in response

        content_text = response["result"]["content"][0]["text"]

        # Should contain test statistics
        assert "Total:" in content_text or "Test Results:" in content_text
        assert "Passed:" in content_text or "Failed:" in content_text or "Skipped:" in content_text

    finally:
        await client.stop()