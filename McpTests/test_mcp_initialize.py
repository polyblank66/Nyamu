"""
Test MCP initialization
"""

import pytest
from mcp_client import MCPClient


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_mcp_initialize_success(unity_state_manager):
    """Test successful MCP initialization"""
    client = MCPClient()
    await client.start()

    try:
        response = await client.initialize()

        assert response["jsonrpc"] == "2.0"
        assert "result" in response

        result = response["result"]
        assert result["protocolVersion"] == "2024-11-05"
        assert "capabilities" in result
        assert "serverInfo" in result
        assert result["serverInfo"]["name"] == "NyamuServer"
        assert result["serverInfo"]["version"] == "1.0.0"

        # Check that capabilities contain expected tools
        capabilities = result["capabilities"]
        assert "tools" in capabilities

        tools = capabilities["tools"]
        assert "compilation_trigger" in tools
        assert "tests_run_single" in tools

    finally:
        await client.stop()


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_mcp_initialize_with_session_client(mcp_client, unity_state_manager):
    """Test MCP initialization using session client fixture"""
    response = await mcp_client.initialize()

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    result = response["result"]
    assert result["protocolVersion"] == "2024-11-05"
    assert "serverInfo" in result
    assert result["serverInfo"]["name"] == "NyamuServer"


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_mcp_initialize_invalid_protocol_version(unity_state_manager):
    """Test MCP initialization with invalid protocol version"""
    client = MCPClient()
    await client.start()

    try:
        # Send initialize without protocolVersion - this should return error in response
        try:
            response = await client._send_request("initialize", {})
            # If we get here, check the response has error
            assert response["jsonrpc"] == "2.0"
            assert "error" in response
            assert response["error"]["code"] == -32602
            assert "protocolVersion is required" in response["error"]["message"]
        except RuntimeError as e:
            # Error was raised, check it contains expected message
            assert "protocolVersion is required" in str(e)

    finally:
        await client.stop()


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_mcp_server_info_structure(unity_state_manager):
    """Test that server info has correct structure"""
    client = MCPClient()
    await client.start()

    try:
        response = await client.initialize()
        server_info = response["result"]["serverInfo"]

        # Check required fields
        assert "name" in server_info
        assert "version" in server_info
        assert isinstance(server_info["name"], str)
        assert isinstance(server_info["version"], str)
        assert len(server_info["name"]) > 0
        assert len(server_info["version"]) > 0

    finally:
        await client.stop()
