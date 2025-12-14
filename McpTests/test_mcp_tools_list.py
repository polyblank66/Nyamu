"""
Test MCP tools/list functionality
"""

import pytest
from mcp_client import MCPClient


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_tools_list_success(mcp_client, unity_state_manager):
    """Test successful tools list retrieval"""
    response = await mcp_client.list_tools()

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    result = response["result"]
    assert "tools" in result
    assert isinstance(result["tools"], list)
    assert len(result["tools"]) >= 2  # Should have at least compile_and_wait and run_tests


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_tools_list_contains_compilation_trigger(mcp_client, unity_state_manager):
    """Test that tools list contains compilation_trigger"""
    response = await mcp_client.list_tools()
    tools = response["result"]["tools"]

    compile_tool = next((tool for tool in tools if tool["name"] == "compilation_trigger"), None)
    assert compile_tool is not None

    # Check tool structure
    assert "name" in compile_tool
    assert "description" in compile_tool
    assert "inputSchema" in compile_tool

    assert compile_tool["name"] == "compilation_trigger"
    assert isinstance(compile_tool["description"], str)
    assert len(compile_tool["description"]) > 0

    # Check input schema
    schema = compile_tool["inputSchema"]
    assert schema["type"] == "object"
    assert "properties" in schema
    assert "timeout" in schema["properties"]


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_tools_list_contains_run_tests(mcp_client, unity_state_manager):
    """Test that tools list contains run_tests"""
    response = await mcp_client.list_tools()
    tools = response["result"]["tools"]

    test_tool = next((tool for tool in tools if tool["name"] == "run_tests"), None)
    assert test_tool is not None

    # Check tool structure
    assert test_tool["name"] == "run_tests"
    assert isinstance(test_tool["description"], str)
    assert len(test_tool["description"]) > 0

    # Check input schema
    schema = test_tool["inputSchema"]
    assert schema["type"] == "object"
    assert "properties" in schema
    assert "test_mode" in schema["properties"]
    assert "test_filter" in schema["properties"]
    assert "timeout" in schema["properties"]

    # Check test_mode enum values
    test_mode_prop = schema["properties"]["test_mode"]
    assert "enum" in test_mode_prop
    assert "EditMode" in test_mode_prop["enum"]
    assert "PlayMode" in test_mode_prop["enum"]


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_tools_list_schema_validation(unity_state_manager):
    """Test that all tools have valid schemas"""
    client = MCPClient()
    await client.start()

    try:
        response = await client.list_tools()
        tools = response["result"]["tools"]

        for tool in tools:
            # Each tool must have required fields
            assert "name" in tool
            assert "description" in tool
            assert "inputSchema" in tool

            # Name and description must be non-empty strings
            assert isinstance(tool["name"], str)
            assert len(tool["name"]) > 0
            assert isinstance(tool["description"], str)
            assert len(tool["description"]) > 0

            # Schema must be valid JSON Schema
            schema = tool["inputSchema"]
            assert "type" in schema
            assert schema["type"] == "object"

    finally:
        await client.stop()


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_tools_list_direct_request(unity_state_manager):
    """Test tools/list using direct request method"""
    client = MCPClient()
    await client.start()

    try:
        response = await client._send_request("tools/list")

        assert response["jsonrpc"] == "2.0"
        assert "result" in response
        assert "tools" in response["result"]
        assert isinstance(response["result"]["tools"], list)

    finally:
        await client.stop()