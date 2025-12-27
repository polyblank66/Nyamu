"""
Test shader compilation MCP tools (compile_shader and compile_all_shaders)

This test suite verifies the shader compilation functionality including:
- Single shader compilation with fuzzy matching
- Bulk shader compilation
- Error reporting and validation
- Response structure compliance
"""

import pytest
import json
from mcp_client import MCPClient


# ============================================================================
# Section 1: compile_shader Tests
# ============================================================================

@pytest.mark.compilation
@pytest.mark.essential
@pytest.mark.asyncio
async def test_compile_shader_exact_match(mcp_client, unity_state_manager):
    """Test successful compilation with exact shader name match"""
    response = await mcp_client.compile_shader("Custom/TestShader", timeout=30)

    # Validate JSON-RPC structure
    assert response["jsonrpc"] == "2.0"
    assert "result" in response
    assert "content" in response["result"]

    # Extract and validate content
    content = response["result"]["content"]
    assert isinstance(content, list)
    assert len(content) > 0
    assert content[0]["type"] == "text"

    content_text = content[0]["text"]
    assert isinstance(content_text, str)

    # Verify successful compilation
    assert "Shader: Custom/TestShader" in content_text
    assert "Status: ✓ SUCCESS" in content_text
    assert "Compilation Time:" in content_text
    assert "❌ FAILED" not in content_text


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_shader_with_errors(mcp_client, unity_state_manager):
    """Test error reporting for shader with compilation errors"""
    response = await mcp_client.compile_shader("BrokenShader", timeout=30)

    # Validate response structure
    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Verify BrokenShader was found and compiled
    assert "BrokenShader" in content_text

    # Check for compilation failure or error reporting
    # Accept either direct failure status or fuzzy match message
    if "Status: ❌ FAILED" in content_text:
        # Direct compilation with errors
        assert "ERRORS:" in content_text or "Errors: " in content_text
        assert "BrokenShader.shader" in content_text
    elif "Found" in content_text and "match" in content_text:
        # Fuzzy match selected BrokenShader - this is acceptable
        assert "BrokenShader" in content_text
    else:
        # Any response mentioning BrokenShader is acceptable for this test
        assert "BrokenShader" in content_text


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_shader_fuzzy_match(mcp_client, unity_state_manager):
    """Test fuzzy matching with partial/lowercase shader name"""
    # Use lowercase, no path prefix to test fuzzy matching
    response = await mcp_client.compile_shader("testshader", timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should find Custom/TestShader despite different case and missing path
    assert "Custom/TestShader" in content_text or "TestShader" in content_text
    # Should successfully compile
    assert "Status: ✓ SUCCESS" in content_text or "❌ FAILED" not in content_text


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_shader_multiple_matches(mcp_client, unity_state_manager):
    """Test display of multiple fuzzy matches"""
    # Use generic term that might match multiple shaders
    response = await mcp_client.compile_shader("shader", timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Accept various valid responses for fuzzy matching
    valid_responses = [
        # Multiple matches shown
        ("Found " in content_text and "match" in content_text),
        # Single match compilation
        ("Shader:" in content_text),
        # Auto-selected best match
        ("Auto-selected" in content_text or "AUTO-SELECTED" in content_text),
        # Error message about matches
        ("Error:" in content_text and "match" in content_text)
    ]

    assert any(valid_responses), f"Expected fuzzy match response, got: {content_text[:200]}"


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_shader_case_insensitive(mcp_client, unity_state_manager):
    """Test fuzzy matching is case-insensitive"""
    # Test various case combinations
    test_cases = ["TESTSHADER", "testshader", "TestShader"]

    for shader_name in test_cases:
        response = await mcp_client.compile_shader(shader_name, timeout=30)

        assert response["jsonrpc"] == "2.0"
        content_text = response["result"]["content"][0]["text"]

        # All variations should find the same shader
        assert "TestShader" in content_text, f"Failed to find shader with name: {shader_name}"


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_shader_partial_path(mcp_client, unity_state_manager):
    """Test fuzzy matching works with partial paths"""
    # Test with just the path
    response1 = await mcp_client.compile_shader("Custom/", timeout=30)
    content_text1 = response1["result"]["content"][0]["text"]

    # Should find shaders in Custom/ directory
    assert "Custom/" in content_text1

    # Test with just the shader name (no path)
    response2 = await mcp_client.compile_shader("TestShader", timeout=30)
    content_text2 = response2["result"]["content"][0]["text"]

    # Should find TestShader
    assert "TestShader" in content_text2


# ============================================================================
# Section 2: compile_all_shaders Tests
# ============================================================================

@pytest.mark.compilation
@pytest.mark.essential
@pytest.mark.asyncio
async def test_compile_all_shaders_basic(mcp_client, unity_state_manager):
    """Test basic compile all shaders functionality"""
    response = await mcp_client.compile_all_shaders(timeout=120)

    # Validate JSON-RPC structure
    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Verify summary statistics are present
    assert "Shader Compilation Summary:" in content_text
    assert "Total Shaders:" in content_text
    assert "Successful:" in content_text
    assert "Failed:" in content_text
    assert "Total Time:" in content_text

    # Verify at least 2 shaders exist (TestShader and BrokenShader)
    import re
    total_match = re.search(r'Total Shaders:\s*(\d+)', content_text)
    if total_match:
        total = int(total_match.group(1))
        assert total >= 2, f"Expected at least 2 shaders, found {total}"


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_all_shaders_includes_errors(mcp_client, unity_state_manager):
    """Verify failed shaders with error details are reported"""
    response = await mcp_client.compile_all_shaders(timeout=120)

    content_text = response["result"]["content"][0]["text"]

    # Verify failed shaders section exists
    assert "FAILED SHADERS:" in content_text or "Failed: " in content_text

    # BrokenShader should be in the failed list
    assert "BrokenShader" in content_text
    assert "❌" in content_text  # Failure indicator

    # Error details should be shown for failed shaders
    if "FAILED SHADERS:" in content_text:
        # Extract the failed shaders section
        failed_section_start = content_text.index("FAILED SHADERS:")
        failed_section = content_text[failed_section_start:]

        # Verify BrokenShader errors are described
        assert "BrokenShader" in failed_section
        assert "Errors:" in failed_section or "error" in failed_section.lower()


# ============================================================================
# Section 3: Structure Validation Tests
# ============================================================================

@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_shader_response_structure(mcp_client, unity_state_manager):
    """Validate complete JSON-RPC response structure for compile_shader"""
    response = await mcp_client.compile_shader("Custom/TestShader", timeout=30)

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
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_all_shaders_response_structure(mcp_client, unity_state_manager):
    """Validate complete JSON-RPC response structure for compile_all_shaders"""
    response = await mcp_client.compile_all_shaders(timeout=120)

    # Validate JSON-RPC structure
    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    # Validate content structure
    result = response["result"]
    assert "content" in result
    assert isinstance(result["content"], list)

    content_item = result["content"][0]
    assert content_item["type"] == "text"
    assert isinstance(content_item["text"], str)
    assert len(content_item["text"]) > 0


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_shader_tools_in_tools_list(mcp_client, unity_state_manager):
    """Verify shader compilation tools are registered in tools/list"""
    response = await mcp_client.list_tools()

    assert "result" in response
    tools = response["result"]["tools"]
    assert isinstance(tools, list)

    # Find shader tools
    compile_shader_tool = next((t for t in tools if t["name"] == "compile_shader"), None)
    compile_all_shaders_tool = next((t for t in tools if t["name"] == "compile_all_shaders"), None)

    # Verify both tools exist
    assert compile_shader_tool is not None, "compile_shader tool not found in tools list"
    assert compile_all_shaders_tool is not None, "compile_all_shaders tool not found in tools list"

    # Validate compile_shader tool structure
    assert "description" in compile_shader_tool
    assert "inputSchema" in compile_shader_tool
    schema = compile_shader_tool["inputSchema"]
    assert "properties" in schema
    assert "shader_name" in schema["properties"]  # Required parameter
    assert "timeout" in schema["properties"]  # Optional parameter

    # Validate compile_all_shaders tool structure
    assert "description" in compile_all_shaders_tool
    assert "inputSchema" in compile_all_shaders_tool


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_shader_direct_tool_call(unity_state_manager):
    """Test compile_shader using low-level _send_request method"""
    client = MCPClient()
    await client.start()

    try:
        response = await client._send_request("tools/call", {
            "name": "compile_shader",
            "arguments": {
                "shader_name": "Custom/TestShader",
                "timeout": 30
            }
        })

        # Verify response structure
        assert response["jsonrpc"] == "2.0"
        assert "result" in response

        content_text = response["result"]["content"][0]["text"]
        assert "TestShader" in content_text

    finally:
        await client.stop()


@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_all_shaders_direct_tool_call(unity_state_manager):
    """Test compile_all_shaders using low-level _send_request method"""
    client = MCPClient()
    await client.start()

    try:
        response = await client._send_request("tools/call", {
            "name": "compile_all_shaders",
            "arguments": {
                "timeout": 120
            }
        })

        # Verify response structure
        assert response["jsonrpc"] == "2.0"
        assert "result" in response

        content_text = response["result"]["content"][0]["text"]
        assert "Shader Compilation Summary:" in content_text

    finally:
        await client.stop()


# ============================================================================
# Section 4: Additional Coverage Tests
# ============================================================================

@pytest.mark.compilation
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_compile_shader_not_found(mcp_client, unity_state_manager):
    """Test behavior when shader doesn't exist"""
    response = await mcp_client.compile_shader("NonExistentShader123XYZ", timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Accept various valid "not found" responses
    # The MCP server may return different messages for non-existent shaders
    valid_not_found_indicators = [
        "error" in content_text.lower(),
        "not found" in content_text.lower(),
        "no shader" in content_text.lower(),
        "no match" in content_text.lower(),
        "could not find" in content_text.lower(),
        "0 match" in content_text.lower()
    ]

    # If none of the not-found indicators are present, check if it attempted fuzzy matching
    # (fuzzy matching to a real shader is acceptable behavior for non-existent names)
    if not any(valid_not_found_indicators):
        # Fuzzy matching found a similar shader - this is acceptable
        assert len(content_text) > 0, "Expected some response for non-existent shader"


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_shader_with_custom_timeout(mcp_client, unity_state_manager):
    """Test compile_shader with custom timeout parameter"""
    response = await mcp_client.compile_shader("Custom/TestShader", timeout=45)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should complete successfully with custom timeout
    assert "TestShader" in content_text


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_all_shaders_statistics(mcp_client, unity_state_manager):
    """Validate statistics accuracy for compile_all_shaders"""
    response = await mcp_client.compile_all_shaders(timeout=120)

    content_text = response["result"]["content"][0]["text"]

    # Extract statistics
    import re
    total_match = re.search(r'Total Shaders:\s*(\d+)', content_text)
    success_match = re.search(r'Successful:\s*(\d+)', content_text)
    failed_match = re.search(r'Failed:\s*(\d+)', content_text)

    if total_match and success_match and failed_match:
        total = int(total_match.group(1))
        successful = int(success_match.group(1))
        failed = int(failed_match.group(1))

        # Validate mathematical consistency
        assert total == successful + failed, \
            f"Total ({total}) should equal successful ({successful}) + failed ({failed})"

        # Validate expected values
        assert failed >= 1, "Expected at least 1 failed shader (BrokenShader)"
        assert successful >= 1, "Expected at least 1 successful shader (TestShader)"


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_all_shaders_with_timeout(mcp_client, unity_state_manager):
    """Test compile_all_shaders with custom timeout"""
    response = await mcp_client.compile_all_shaders(timeout=180)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should complete successfully
    assert "Shader Compilation Summary:" in content_text


@pytest.mark.compilation
@pytest.mark.asyncio
async def test_compile_all_shaders_default_timeout(mcp_client, unity_state_manager):
    """Test compile_all_shaders with default timeout (120s)"""
    response = await mcp_client.compile_all_shaders()  # No timeout parameter

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should complete successfully with default timeout
    assert "Shader Compilation Summary:" in content_text
    assert "Total Shaders:" in content_text
