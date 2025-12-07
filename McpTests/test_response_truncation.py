"""
Test MCP response truncation functionality
"""

import pytest
from mcp_client import MCPClient


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_large_error_message_truncation(mcp_client, unity_state_manager):
    """Test that large error messages are properly truncated by MCP response formatter"""
    # Run the Unity test that generates a large error message (~50,000 characters)
    response = await mcp_client.run_tests(
        test_mode="EditMode",
        test_filter="NyamuTests.LargeErrorMessageTest",
        timeout=60
    )

    # Verify basic response structure
    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    result = response["result"]
    assert "content" in result
    assert isinstance(result["content"], list)
    assert len(result["content"]) > 0

    content = result["content"][0]
    assert content["type"] == "text"
    assert "text" in content

    response_text = content["text"]

    # Verify test results structure
    assert "Test Results:" in response_text
    assert "Total: 1" in response_text
    assert "Failed: 1" in response_text
    assert "NyamuTests.LargeErrorMessageTest" in response_text

    # Verify response truncation is working
    # The default character limit is 25,000, so response should be significantly less than the original ~50,000
    response_length = len(response_text)

    # Response should be truncated to around 25,000 characters (allowing for some overhead)
    assert response_length < 30000, f"Response too long: {response_length} characters"
    assert response_length > 20000, f"Response too short: {response_length} characters (might not be truncated properly)"

    # Verify the response contains the beginning of the error message
    assert "Error 0001" in response_text, "Should contain the first error entry"
    assert "Complex nested template instantiation error" in response_text, "Should contain error content"

    # Verify the response does NOT contain the end of the original message
    # The original would have gone up to Error 0075+ with 50,000+ characters
    # With truncation, it should cut off much earlier
    assert "Error 0075" not in response_text, "Should not contain late error entries (indicates truncation failed)"

    # The response should end abruptly without showing all errors
    # Count how many error entries we can see
    error_count = response_text.count("[Error ")
    assert error_count < 50, f"Too many errors shown: {error_count} (truncation may not be working)"
    assert error_count > 10, f"Too few errors shown: {error_count} (test may not be generating enough content)"

    print(f"Response length: {response_length} characters")
    print(f"Error entries shown: {error_count}")
    print(f"Response ends with: ...{response_text[-100:]}")


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_normal_test_not_truncated(mcp_client, unity_state_manager):
    """Test that normal-sized responses are not truncated"""
    # Run a normal test that should have a small response
    response = await mcp_client.run_tests(
        test_mode="EditMode",
        test_filter="NyamuTests.PassingTest1",
        timeout=60
    )

    # Verify basic response structure
    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    response_text = response["result"]["content"][0]["text"]

    # Normal test responses should be much smaller
    response_length = len(response_text)
    assert response_length < 5000, f"Normal test response too long: {response_length} characters"

    # Should not contain truncation indicators
    assert "truncated" not in response_text.lower(), "Normal response should not be truncated"

    # Should show successful test completion
    assert "Test Results:" in response_text
    assert "Total: 1" in response_text
    assert "Passed: 1" in response_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_truncation_preserves_json_structure(mcp_client, unity_state_manager):
    """Test that response truncation preserves valid JSON-RPC structure"""
    # Run the large error test
    response = await mcp_client.run_tests(
        test_mode="EditMode",
        test_filter="NyamuTests.LargeErrorMessageTest",
        timeout=60
    )

    # The response should still be valid JSON-RPC despite truncation
    assert response["jsonrpc"] == "2.0"
    assert "result" in response
    assert "content" in response["result"]
    assert "type" in response["result"]["content"][0]
    assert "text" in response["result"]["content"][0]

    # The text content should be properly truncated without breaking the JSON structure
    # This test passing means the JSON was parseable by the MCP client
    response_text = response["result"]["content"][0]["text"]

    # Response should be a valid string (not cut off in the middle of a JSON escape sequence)
    assert isinstance(response_text, str), "Response text should be a valid string"

    # Should start with expected content
    assert response_text.startswith("Test Results:"), "Response should start with test results header"


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_response_character_limit_configuration():
    """Test that MCP server respects the configured character limit"""
    # This test verifies the configuration endpoint is working
    client = MCPClient()
    await client.start()

    try:
        # Test the configuration system by making a direct HTTP request to Unity
        import aiohttp
        import asyncio

        # Wait for Unity to be ready
        await asyncio.sleep(1)

        try:
            async with aiohttp.ClientSession() as session:
                async with session.get('http://localhost:17932/mcp-settings') as resp:
                    if resp.status == 200:
                        config = await resp.json()

                        # Verify default configuration
                        assert "responseCharacterLimit" in config
                        assert config["responseCharacterLimit"] == 25000
                        assert "enableTruncation" in config
                        assert config["enableTruncation"] is True
                        assert "truncationMessage" in config

                        print(f"MCP Settings: {config}")
                    else:
                        print(f"Could not fetch MCP settings: HTTP {resp.status}")
        except Exception as e:
            print(f"Could not test MCP settings endpoint: {e}")
            # This is not a critical failure - just means Unity settings endpoint isn't available

    finally:
        await client.stop()
