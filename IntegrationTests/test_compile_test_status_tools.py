"""
Test compile_status and test_status MCP tools
"""

import pytest
import requests
import json
from mcp_client import MCPClient


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_status_tool_exists(mcp_client, unity_state_manager):
    """Test that compilation_status tool exists in tools list"""
    response = await mcp_client.list_tools()
    tools = response["result"]["tools"]

    compile_status_tool = next((tool for tool in tools if tool["name"] == "scripts_compile_status"), None)
    assert compile_status_tool is not None

    # Check tool structure
    assert "name" in compile_status_tool
    assert "description" in compile_status_tool
    assert "inputSchema" in compile_status_tool

    assert compile_status_tool["name"] == "scripts_compile_status"
    assert isinstance(compile_status_tool["description"], str)
    assert len(compile_status_tool["description"]) > 0
    assert "without triggering compilation" in compile_status_tool["description"]

    # Check input schema
    schema = compile_status_tool["inputSchema"]
    assert schema["type"] == "object"
    assert "properties" in schema
    assert "required" in schema
    assert len(schema["required"]) == 0  # No required parameters


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_test_status_tool_exists(mcp_client, unity_state_manager):
    """Test that test_status tool exists in tools list"""
    response = await mcp_client.list_tools()
    tools = response["result"]["tools"]

    test_status_tool = next((tool for tool in tools if tool["name"] == "tests_run_status"), None)
    assert test_status_tool is not None

    # Check tool structure
    assert "name" in test_status_tool
    assert "description" in test_status_tool
    assert "inputSchema" in test_status_tool

    assert test_status_tool["name"] == "tests_run_status"
    assert isinstance(test_status_tool["description"], str)
    assert len(test_status_tool["description"]) > 0
    assert "without running tests" in test_status_tool["description"]

    # Check input schema
    schema = test_status_tool["inputSchema"]
    assert schema["type"] == "object"
    assert "properties" in schema
    assert "required" in schema
    assert len(schema["required"]) == 0  # No required parameters


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_status_call_success(mcp_client, unity_state_manager):
    """Test successful compile_status tool call"""
    response = await mcp_client.scripts_compile_status()

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
async def test_test_status_call_success(mcp_client, unity_state_manager):
    """Test successful test_status tool call"""
    response = await mcp_client.tests_run_status()

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
async def test_compile_status_response_structure(mcp_client, unity_state_manager):
    """Test that compile_status response has correct structure"""
    response = await mcp_client.scripts_compile_status()

    status_text = response["result"]["content"][0]["text"]
    status_data = json.loads(status_text)

    # Check required fields
    required_fields = ["status", "isCompiling", "lastCompilationTime", "lastCompilationRequestTime", "errors"]
    for field in required_fields:
        assert field in status_data

    # Check field types
    assert isinstance(status_data["status"], str)
    assert isinstance(status_data["isCompiling"], bool)
    assert isinstance(status_data["lastCompilationTime"], str)
    assert isinstance(status_data["lastCompilationRequestTime"], str)
    assert isinstance(status_data["errors"], list)


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_test_status_response_structure(mcp_client, unity_state_manager):
    """Test that test_status response has correct structure"""
    response = await mcp_client.tests_run_status()

    status_text = response["result"]["content"][0]["text"]
    status_data = json.loads(status_text)

    # Check required fields
    required_fields = ["status", "isRunning", "lastTestTime", "testResults", "testRunId"]
    for field in required_fields:
        assert field in status_data

    # Check field types
    assert isinstance(status_data["status"], str)
    assert isinstance(status_data["isRunning"], bool)
    assert isinstance(status_data["lastTestTime"], str)
    # testResults can be None or dict
    assert status_data["testResults"] is None or isinstance(status_data["testResults"], dict)
    # testRunId can be None or string
    assert status_data["testRunId"] is None or isinstance(status_data["testRunId"], str)


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_status_consistency_with_http_endpoint(mcp_client, unity_state_manager, unity_base_url):
    """Test that compile_status MCP tool matches HTTP endpoint"""
    # Get status via MCP tool
    mcp_response = await mcp_client.scripts_compile_status()
    mcp_data = json.loads(mcp_response["result"]["content"][0]["text"])

    # Get status via direct HTTP call
    http_response = requests.get(f"{unity_base_url}/compilation-status")
    http_data = http_response.json()

    # Should match exactly
    assert mcp_data == http_data


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_test_status_consistency_with_http_endpoint(mcp_client, unity_state_manager, unity_base_url):
    """Test that tests_status MCP tool matches HTTP endpoint"""
    # Get status via MCP tool
    mcp_response = await mcp_client.tests_run_status()
    mcp_data = json.loads(mcp_response["result"]["content"][0]["text"])

    # Get status via direct HTTP call
    http_response = requests.get(f"{unity_base_url}/tests-status")
    http_data = http_response.json()

    # Should match exactly
    assert mcp_data == http_data


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_status_idle_state(mcp_client, unity_state_manager):
    """Test compile_status when Unity is idle"""
    response = await mcp_client.scripts_compile_status()
    status_data = json.loads(response["result"]["content"][0]["text"])

    # When idle, should not be compiling
    assert status_data["status"] == "idle"
    assert status_data["isCompiling"] is False


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_test_status_idle_state(mcp_client, unity_state_manager):
    """Test test_status when Unity is idle"""
    response = await mcp_client.tests_run_status()
    status_data = json.loads(response["result"]["content"][0]["text"])

    # When idle, should not be running tests
    assert status_data["status"] == "idle"
    assert status_data["isRunning"] is False


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_status_vs_editor_status_consistency(mcp_client, unity_state_manager):
    """Test that compile_status isCompiling matches editor_status"""
    # Get both statuses
    compile_response = await mcp_client.scripts_compile_status()
    editor_response = await mcp_client.editor_status()

    compile_data = json.loads(compile_response["result"]["content"][0]["text"])
    editor_data = json.loads(editor_response["result"]["content"][0]["text"])

    # isCompiling should match
    assert compile_data["isCompiling"] == editor_data["isCompiling"]


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_test_status_vs_editor_status_consistency(mcp_client, unity_state_manager):
    """Test that test_status isRunning matches editor_status"""
    # Get both statuses
    test_response = await mcp_client.tests_run_status()
    editor_response = await mcp_client.editor_status()

    test_data = json.loads(test_response["result"]["content"][0]["text"])
    editor_data = json.loads(editor_response["result"]["content"][0]["text"])

    # isRunning should match isRunningTests
    assert test_data["isRunning"] == editor_data["isRunningTests"]
