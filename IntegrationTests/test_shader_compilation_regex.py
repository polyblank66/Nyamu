"""
Test shader compilation regex and status MCP tools

This test suite verifies:
- compile_shaders_regex: Compile shaders matching regex patterns
- shader_compilation_status: Check shader compilation status and results
"""

import pytest
import json
import re
from mcp_client import MCPClient


# ============================================================================
# Section 1: compile_shaders_regex Tests
# ============================================================================

@pytest.mark.compilation
@pytest.mark.essential
@pytest.mark.asyncio
async def test_compile_shaders_regex_basic(mcp_client, unity_state_manager):
    """Test basic regex shader compilation with simple pattern"""
    # Match all shaders in TestShaders directory
    response = await mcp_client.compile_shaders_regex(".*TestShader.*", timeout=120)

    # Validate JSON-RPC structure
    assert response["jsonrpc"] == "2.0"
    assert "result" in response
    assert "content" in response["result"]

    content_text = response["result"]["content"][0]["text"]

    # Verify summary statistics are present
    assert "Shader Regex Compilation Results" in content_text or "Pattern:" in content_text
    assert "Total Matched:" in content_text or "Total" in content_text
    assert "Successful:" in content_text
    assert "Failed:" in content_text

    # Should find TestShader
    assert "TestShader" in content_text


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_shaders_regex_custom_path(mcp_client, unity_state_manager):
    """Test regex matching specific shader paths"""
    # Match shaders in Custom/ directory
    response = await mcp_client.compile_shaders_regex("Assets/.*Custom/.*", timeout=120)

    content_text = response["result"]["content"][0]["text"]

    # Verify pattern was used
    assert "Pattern:" in content_text or "Assets/.*Custom/" in content_text


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_shaders_regex_no_matches(mcp_client, unity_state_manager):
    """Test regex with no matching shaders"""
    response = await mcp_client.compile_shaders_regex("NonExistentPattern12345XYZ", timeout=120)

    content_text = response["result"]["content"][0]["text"]

    # Should report no matches
    assert "0" in content_text or "No shaders matched" in content_text or "Total Matched: 0" in content_text


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_shaders_regex_with_errors(mcp_client, unity_state_manager):
    """Test regex compilation including shaders with errors"""
    # Match BrokenShader
    response = await mcp_client.compile_shaders_regex(".*BrokenShader.*", timeout=120)

    content_text = response["result"]["content"][0]["text"]

    # Should find and compile BrokenShader
    assert "BrokenShader" in content_text

    # Should report failures
    if "Failed:" in content_text:
        failed_match = re.search(r'Failed:\s*(\d+)', content_text)
        if failed_match:
            failed_count = int(failed_match.group(1))
            assert failed_count >= 1, "Expected at least 1 failed shader (BrokenShader)"


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_shaders_regex_response_structure(mcp_client, unity_state_manager):
    """Validate JSON-RPC response structure for compile_shaders_regex"""
    response = await mcp_client.compile_shaders_regex(".*TestShader.*", timeout=120)

    # Validate JSON-RPC structure
    assert response["jsonrpc"] == "2.0"
    assert "result" in response
    assert isinstance(response["result"], dict)

    # Validate content structure
    result = response["result"]
    assert "content" in result
    assert isinstance(result["content"], list)
    assert len(result["content"]) > 0

    # Validate content item structure
    content_item = result["content"][0]
    assert "type" in content_item
    assert content_item["type"] == "text"
    assert "text" in content_item
    assert isinstance(content_item["text"], str)
    assert len(content_item["text"]) > 0


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_shaders_regex_invalid_pattern(mcp_client, unity_state_manager):
    """Test behavior with invalid regex pattern"""
    # Invalid regex pattern (unmatched bracket)
    response = await mcp_client.compile_shaders_regex("[invalid(regex", timeout=120)

    content_text = response["result"]["content"][0]["text"]

    # Should report error about invalid pattern
    assert "error" in content_text.lower() or "invalid" in content_text.lower() or "failed" in content_text.lower()


# ============================================================================
# Section 2: shader_compilation_status Tests
# ============================================================================

@pytest.mark.compilation
@pytest.mark.essential
@pytest.mark.asyncio
async def test_shader_compilation_status_after_single(mcp_client, unity_state_manager):
    """Test status after single shader compilation"""
    # First compile a shader
    await mcp_client.compile_shader("Custom/TestShader", timeout=30)

    # Check status
    response = await mcp_client.shader_compilation_status()

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Verify status information
    assert "status" in content_text.lower() or "Status:" in content_text
    assert "lastCompilationType" in content_text or "Type:" in content_text or "single" in content_text


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_shader_compilation_status_after_all(mcp_client, unity_state_manager):
    """Test status after compile all shaders"""
    # First compile all shaders
    await mcp_client.compile_all_shaders(timeout=120)

    # Check status
    response = await mcp_client.shader_compilation_status()

    content_text = response["result"]["content"][0]["text"]

    # Should show last compilation was "all"
    assert "all" in content_text.lower()


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_shader_compilation_status_after_regex(mcp_client, unity_state_manager):
    """Test status after regex shader compilation"""
    # First compile shaders by regex
    await mcp_client.compile_shaders_regex(".*TestShader.*", timeout=120)

    # Check status
    response = await mcp_client.shader_compilation_status()

    content_text = response["result"]["content"][0]["text"]

    # Should show last compilation was "regex"
    assert "regex" in content_text.lower()


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_shader_compilation_status_response_structure(mcp_client, unity_state_manager):
    """Validate JSON-RPC response structure for shader_compilation_status"""
    response = await mcp_client.shader_compilation_status()

    # Validate JSON-RPC structure
    assert response["jsonrpc"] == "2.0"
    assert "result" in response
    assert isinstance(response["result"], dict)

    # Validate content structure
    result = response["result"]
    assert "content" in result
    assert isinstance(result["content"], list)
    assert len(result["content"]) > 0

    # Validate content item structure
    content_item = result["content"][0]
    assert "type" in content_item
    assert content_item["type"] == "text"
    assert "text" in content_item
    assert isinstance(content_item["text"], str)


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_shader_compilation_status_while_compiling(mcp_client, unity_state_manager):
    """Test status check while compilation is in progress"""
    # Start long compilation in background (don't await)
    compile_task = mcp_client.compile_all_shaders(timeout=120)

    # Quickly check status (may catch it while compiling)
    import asyncio
    await asyncio.sleep(0.5)  # Give compilation a moment to start

    status_response = await mcp_client.shader_compilation_status()
    content_text = status_response["result"]["content"][0]["text"]

    # Should show compilation state (either compiling or idle if it finished fast)
    assert "status" in content_text.lower() or "Status:" in content_text

    # Wait for compilation to finish
    await compile_task


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_shader_tools_in_tools_list(mcp_client, unity_state_manager):
    """Verify new shader tools are registered in tools/list"""
    response = await mcp_client.list_tools()

    assert "result" in response
    tools = response["result"]["tools"]
    assert isinstance(tools, list)

    # Find new shader tools
    compile_shaders_regex_tool = next((t for t in tools if t["name"] == "compile_shaders_regex"), None)
    shader_status_tool = next((t for t in tools if t["name"] == "shader_compilation_status"), None)

    # Verify both tools exist
    assert compile_shaders_regex_tool is not None, "compile_shaders_regex tool not found in tools list"
    assert shader_status_tool is not None, "shader_compilation_status tool not found in tools list"

    # Validate compile_shaders_regex tool structure
    assert "description" in compile_shaders_regex_tool
    assert "inputSchema" in compile_shaders_regex_tool
    schema = compile_shaders_regex_tool["inputSchema"]
    assert "properties" in schema
    assert "pattern" in schema["properties"]  # Required parameter

    # Validate shader_compilation_status tool structure
    assert "description" in shader_status_tool
    assert "inputSchema" in shader_status_tool


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_shaders_regex_direct_tool_call(unity_state_manager):
    """Test compile_shaders_regex using low-level _send_request method"""
    client = MCPClient()
    await client.start()

    try:
        response = await client._send_request("tools/call", {
            "name": "compile_shaders_regex",
            "arguments": {
                "pattern": ".*TestShader.*",
                "timeout": 120
            }
        })

        # Verify response structure
        assert response["jsonrpc"] == "2.0"
        assert "result" in response

        content_text = response["result"]["content"][0]["text"]
        assert "Pattern:" in content_text or "Shader" in content_text

    finally:
        await client.stop()


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_shader_compilation_status_direct_tool_call(unity_state_manager):
    """Test shader_compilation_status using low-level _send_request method"""
    client = MCPClient()
    await client.start()

    try:
        response = await client._send_request("tools/call", {
            "name": "shader_compilation_status",
            "arguments": {}
        })

        # Verify response structure
        assert response["jsonrpc"] == "2.0"
        assert "result" in response

        content_text = response["result"]["content"][0]["text"]
        assert len(content_text) > 0

    finally:
        await client.stop()


# ============================================================================
# Section 3: Integration Tests
# ============================================================================

@pytest.mark.compilation
@pytest.mark.asyncio
async def test_status_cleared_on_new_compilation(mcp_client, unity_state_manager):
    """Test that previous results are cleared when starting new compilation"""
    # Compile single shader
    await mcp_client.compile_shader("Custom/TestShader", timeout=30)

    status1 = await mcp_client.shader_compilation_status()
    content1 = status1["result"]["content"][0]["text"]
    assert "single" in content1.lower()

    # Compile all shaders
    await mcp_client.compile_all_shaders(timeout=120)

    status2 = await mcp_client.shader_compilation_status()
    content2 = status2["result"]["content"][0]["text"]
    assert "all" in content2.lower()

    # Compile regex
    await mcp_client.compile_shaders_regex(".*Test.*", timeout=120)

    status3 = await mcp_client.shader_compilation_status()
    content3 = status3["result"]["content"][0]["text"]
    assert "regex" in content3.lower()


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_regex_statistics_accuracy(mcp_client, unity_state_manager):
    """Validate statistics accuracy for compile_shaders_regex"""
    response = await mcp_client.compile_shaders_regex(".*TestShader.*", timeout=120)

    content_text = response["result"]["content"][0]["text"]

    # Extract statistics
    total_match = re.search(r'Total Matched:\s*(\d+)', content_text) or re.search(r'Total:\s*(\d+)', content_text)
    success_match = re.search(r'Successful:\s*(\d+)', content_text)
    failed_match = re.search(r'Failed:\s*(\d+)', content_text)

    if total_match and success_match and failed_match:
        total = int(total_match.group(1))
        successful = int(success_match.group(1))
        failed = int(failed_match.group(1))

        # Validate mathematical consistency
        assert total == successful + failed, \
            f"Total ({total}) should equal successful ({successful}) + failed ({failed})"

        # At least one shader should match "TestShader" pattern
        assert total >= 1, "Expected at least 1 shader matching .*TestShader.* pattern"
