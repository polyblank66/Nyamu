"""
Test editor_status tool functionality
"""

import pytest
import requests
import json
import asyncio
from mcp_client import MCPClient


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_editor_status_tool_exists(mcp_client, unity_state_manager):
    """Test that editor_status tool exists in tools list"""
    response = await mcp_client.list_tools()
    tools = response["result"]["tools"]

    editor_status_tool = next((tool for tool in tools if tool["name"] == "editor_status"), None)
    assert editor_status_tool is not None

    # Check tool structure
    assert "name" in editor_status_tool
    assert "description" in editor_status_tool
    assert "inputSchema" in editor_status_tool

    assert editor_status_tool["name"] == "editor_status"
    assert isinstance(editor_status_tool["description"], str)
    assert len(editor_status_tool["description"]) > 0

    # Check input schema
    schema = editor_status_tool["inputSchema"]
    assert schema["type"] == "object"
    assert "properties" in schema
    assert "required" in schema
    assert len(schema["required"]) == 0  # No required parameters


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_editor_status_call_success(mcp_client, unity_state_manager):
    """Test successful editor_status tool call"""
    response = await mcp_client.editor_status()

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    result = response["result"]
    assert "content" in result
    assert isinstance(result["content"], list)
    assert len(result["content"]) == 1
    assert result["content"][0]["type"] == "text"

    # Parse the status text
    status_text = result["content"][0]["text"]
    assert isinstance(status_text, str)
    assert len(status_text) > 0

    # Should be valid JSON
    status_data = json.loads(status_text)
    assert isinstance(status_data, dict)


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_editor_status_response_structure(mcp_client, unity_state_manager):
    """Test that editor_status response has correct structure"""
    response = await mcp_client.editor_status()

    # Should contain status information about compilation, testing, and play mode
    status_text = response["result"]["content"][0]["text"]
    status_data = json.loads(status_text)

    required_fields = ["isCompiling", "isRunningTests", "isPlaying"]
    for field in required_fields:
        assert field in status_data
        assert isinstance(status_data[field], bool)


@pytest.mark.mcp
@pytest.mark.protocol
def test_editor_status_endpoint_direct(unity_base_url):
    """Test editor-status HTTP endpoint directly"""
    response = requests.get(f"{unity_base_url}/editor-status")

    assert response.status_code == 200
    assert response.headers["content-type"] == "application/json"

    data = response.json()
    assert "isCompiling" in data
    assert "isRunningTests" in data
    assert "isPlaying" in data

    # Check field types
    assert isinstance(data["isCompiling"], bool)
    assert isinstance(data["isRunningTests"], bool)
    assert isinstance(data["isPlaying"], bool)


@pytest.mark.mcp
@pytest.mark.protocol
def test_editor_status_headers(unity_base_url):
    """Test that editor-status endpoint returns proper headers"""
    response = requests.get(f"{unity_base_url}/editor-status")

    # Check CORS headers
    assert response.headers.get("access-control-allow-origin") == "*"
    assert response.headers.get("access-control-allow-methods") is not None
    assert response.headers.get("access-control-allow-headers") is not None


@pytest.mark.mcp
@pytest.mark.protocol
def test_editor_status_idle_state(unity_base_url):
    """Test editor_status when Unity is idle"""
    response = requests.get(f"{unity_base_url}/editor-status")
    data = response.json()

    # When idle, compilation and tests should not be running
    # Play mode state depends on current Unity state
    assert isinstance(data["isCompiling"], bool)
    assert isinstance(data["isRunningTests"], bool)
    assert isinstance(data["isPlaying"], bool)

    # In normal idle state, these should be False
    assert data["isCompiling"] is False
    assert data["isRunningTests"] is False


@pytest.mark.mcp
@pytest.mark.protocol
def test_editor_status_multiple_requests(unity_base_url):
    """Test multiple consecutive requests to editor_status"""
    responses = []

    for i in range(3):
        response = requests.get(f"{unity_base_url}/editor-status")
        assert response.status_code == 200
        responses.append(response.json())

    # All responses should have valid structure
    for data in responses:
        assert "isCompiling" in data
        assert "isRunningTests" in data
        assert "isPlaying" in data
        assert isinstance(data["isCompiling"], bool)
        assert isinstance(data["isRunningTests"], bool)
        assert isinstance(data["isPlaying"], bool)


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_editor_status_during_compilation(unity_state_manager):
    """Test editor_status during compilation"""
    client1 = MCPClient()
    client2 = MCPClient()
    await client1.start()
    await client2.start()

    try:
        # Start compilation with client1
        compile_task = asyncio.create_task(
            client1.scripts_compile(timeout=30)
        )

        # Wait briefly for compilation to potentially start
        await asyncio.sleep(0.5)

        # Check status with client2 (separate client to avoid stream conflicts)
        status_response = await client2.editor_status()
        status_text = status_response["result"]["content"][0]["text"]
        status_data = json.loads(status_text)

        # Verify the response structure is correct
        assert isinstance(status_data["isCompiling"], bool)
        assert isinstance(status_data["isRunningTests"], bool)
        assert isinstance(status_data["isPlaying"], bool)

        # Clean up the compilation task
        try:
            await asyncio.wait_for(compile_task, timeout=10)
        except asyncio.TimeoutError:
            compile_task.cancel()
            try:
                await compile_task
            except asyncio.CancelledError:
                pass

    finally:
        await client1.stop()
        await client2.stop()

    # Note: Compilation might complete too quickly to catch in progress,
    # but the response structure should always be correct


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_editor_status_consistency_with_compile_status(mcp_client, unity_state_manager, unity_base_url):
    """Test that editor_status isCompiling matches compile_status"""
    # Get both statuses
    editor_status = await mcp_client.editor_status()
    compile_status_response = requests.get(f"{unity_base_url}/compilation-status")

    editor_data = json.loads(editor_status["result"]["content"][0]["text"])
    compile_data = compile_status_response.json()

    # isCompiling should match between the two endpoints
    assert editor_data["isCompiling"] == compile_data["isCompiling"]


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_editor_status_consistency_with_test_status(mcp_client, unity_state_manager, unity_base_url):
    """Test that editor_status isRunningTests matches tests_status"""
    # Get both statuses
    editor_status = await mcp_client.editor_status()
    test_status_response = requests.get(f"{unity_base_url}/tests-status")

    editor_data = json.loads(editor_status["result"]["content"][0]["text"])
    test_data = test_status_response.json()

    # isRunningTests should match between the two endpoints
    assert editor_data["isRunningTests"] == test_data["isRunning"]