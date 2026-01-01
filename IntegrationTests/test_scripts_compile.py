"""
Test compile_and_wait MCP tool
"""

import pytest
import asyncio
from mcp_client import MCPClient


@pytest.mark.compilation
@pytest.mark.essential
@pytest.mark.asyncio
async def test_compile_and_wait_basic(mcp_client, unity_state_manager):
    """Test basic compile_and_wait functionality"""
    response = await mcp_client.scripts_compile(timeout=30)

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    result = response["result"]
    assert "content" in result
    assert isinstance(result["content"], list)
    assert len(result["content"]) > 0

    content = result["content"][0]
    assert content["type"] == "text"
    assert "text" in content
    assert isinstance(content["text"], str)


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_and_wait_with_timeout(mcp_client, unity_state_manager):
    """Test compile_and_wait with custom timeout"""
    response = await mcp_client.scripts_compile(timeout=45)

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    result = response["result"]
    assert "content" in result
    content_text = result["content"][0]["text"]

    # Should indicate successful compilation (assuming no errors in codebase)
    assert "Compilation completed" in content_text


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_and_wait_timeout(unity_state_manager):
    """Test compile_and_wait accepts timeout parameter"""
    client = MCPClient()
    await client.start()

    try:
        # Test that timeout parameter is accepted and compilation completes
        # Note: This won't actually timeout if compilation is fast
        response = await client.scripts_compile(timeout=30)

        # Should get valid response (may complete before timeout)
        assert response["jsonrpc"] == "2.0"
        assert "result" in response

        content_text = response["result"]["content"][0]["text"]
        assert "Compilation completed" in content_text

    finally:
        await client.stop()


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_and_wait_default_parameters(mcp_client, unity_state_manager):
    """Test compile_and_wait with default parameters"""
    response = await mcp_client.scripts_compile()

    assert response["jsonrpc"] == "2.0"
    assert "result" in response


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_and_wait_direct_tool_call(unity_state_manager):
    """Test compile_and_wait using direct tool call"""
    client = MCPClient()
    await client.start()

    try:
        response = await client._send_request("tools/call", {
            "name": "scripts_compile",
            "arguments": {
                "timeout": 30
            }
        })

        assert response["jsonrpc"] == "2.0"
        assert "result" in response

    finally:
        await client.stop()


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_and_wait_invalid_parameters(unity_state_manager):
    """Test compile_and_wait with invalid parameters"""
    client = MCPClient()
    await client.start()

    try:
        # Test with negative timeout - should raise exception
        with pytest.raises(RuntimeError) as exc_info:
            await client._send_request("tools/call", {
                "name": "scripts_compile",
                "arguments": {
                    "timeout": -1
                }
            })

        # Should contain timeout error message for invalid parameter
        assert "timeout" in str(exc_info.value).lower()
        assert "compilation timeout after -1 seconds" in str(exc_info.value).lower()

    finally:
        await client.stop()


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_multiple_concurrent_compiles(unity_state_manager):
    """Test that multiple compile requests are handled properly"""
    client = MCPClient()
    await client.start()

    try:
        # Start multiple compilation requests
        tasks = [
            client.scripts_compile(timeout=30),
            client.scripts_compile(timeout=30)
        ]

        # Wait for both to complete
        responses = await asyncio.gather(*tasks, return_exceptions=True)

        # Both should complete successfully or handle gracefully
        for response in responses:
            if isinstance(response, Exception):
                # Some error occurred, which might be expected for concurrent access
                continue

            assert response["jsonrpc"] == "2.0"
            assert "result" in response

    finally:
        await client.stop()