"""
Integration tests for assets_refresh tool using official MCP library.

These tests verify that assets_refresh:
1. Properly refreshes Unity's asset database
2. Returns compilation error information in response
3. Shows last compilation status even when no new compilation occurs
4. Handles force refresh correctly for file deletions
"""

import pytest
import pytest_asyncio
import os
import asyncio
from pathlib import Path
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


@pytest_asyncio.fixture(scope="function")
async def mcp_session():
    """MCP client using official library"""
    # Use worker-specific project path from environment variable (set by conftest)
    import os
    worker_project_path = os.environ.get("NYAMU_WORKER_PROJECT_PATH")
    if worker_project_path:
        nyamu_bat_path = str(Path(worker_project_path) / ".nyamu" / "nyamu.bat")
    else:
        # Fallback to default path for serial mode
        test_file = Path(__file__)
        project_root = test_file.parent.parent
        nyamu_bat_path = str(project_root / "Nyamu.UnityTestProject" / ".nyamu" / "nyamu.bat")

    # Connect to Nyamu MCP server via nyamu.bat
    server_params = StdioServerParameters(
        command="cmd.exe",
        args=["/c", nyamu_bat_path]
    )

    async with stdio_client(server_params) as (stdio, write):
        async with ClientSession(stdio, write) as session:
            await session.initialize()
            yield session


@pytest.mark.essential
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_assets_refresh_tool_available(mcp_session):
    """Test that assets_refresh tool is available and has correct parameters

    Verifies the tool is exposed by the MCP server with expected schema.
    """
    # List all tools
    response = await mcp_session.list_tools()
    tools = response.tools

    # Find assets_refresh tool
    refresh_tool = next((t for t in tools if t.name == "assets_refresh"), None)
    assert refresh_tool is not None, "assets_refresh tool not found in tools list"

    # Verify tool has force parameter
    input_schema = refresh_tool.inputSchema
    assert "properties" in input_schema
    assert "force" in input_schema["properties"]
    assert input_schema["properties"]["force"]["type"] == "boolean"

    # Verify tool has timeout parameter
    assert "timeout" in input_schema["properties"]


@pytest.mark.essential
@pytest.mark.structural
@pytest.mark.asyncio
async def test_assets_refresh_basic(mcp_session):
    """Test basic assets_refresh without force parameter

    Verifies that assets_refresh returns compilation status information.
    """
    # Call assets_refresh with default parameters
    result = await mcp_session.call_tool("assets_refresh", {"force": False})

    # Extract text content
    assert hasattr(result, "content"), "Result should have content attribute"
    assert len(result.content) > 0, "Result content should not be empty"
    content_text = result.content[0].text

    # Verify response contains asset refresh completion message
    assert "Asset database refresh completed" in content_text

    # Verify compilation status is included (or explicitly stated no compilation)
    assert (
        "Compilation completed" in content_text
        or "Last compilation" in content_text
        or "no compilation triggered" in content_text
    )


@pytest.mark.structural
@pytest.mark.asyncio
async def test_assets_refresh_force(mcp_session, unity_helper, temp_files):
    """Test force refresh after file deletion

    Verifies that force=true properly handles file deletions without CS2001 errors.
    """
    # Create a temporary file
    script_path = unity_helper.create_temp_script_in_assets("TempScriptForce")
    temp_files(script_path)

    # First refresh to register the file
    await mcp_session.call_tool("assets_refresh", {"force": False})
    await asyncio.sleep(2)  # Wait for Unity to process

    # Delete the file
    if os.path.exists(script_path):
        os.remove(script_path)
    meta_path = script_path + ".meta"
    if os.path.exists(meta_path):
        os.remove(meta_path)

    # Force refresh after deletion
    result = await mcp_session.call_tool("assets_refresh", {"force": True})
    content_text = result.content[0].text

    # Verify no CS2001 errors (source file not found)
    assert "CS2001" not in content_text, "Force refresh should prevent CS2001 errors"
    assert "Asset database refresh completed" in content_text


@pytest.mark.essential
@pytest.mark.structural
@pytest.mark.asyncio
async def test_assets_refresh_returns_compilation_errors(mcp_session, unity_helper, temp_files):
    """Test that assets_refresh returns compilation error information

    This is a core requirement: assets_refresh should include compilation
    status in its response, eliminating the need for separate compilation_trigger calls.
    """
    # Create file with syntax error
    script_path = unity_helper.create_temp_script_in_assets("ErrorScript", "syntax")
    temp_files(script_path)

    # Call assets_refresh using official MCP library
    result = await mcp_session.call_tool("assets_refresh", {"force": False})

    # Extract text content
    content_text = result.content[0].text

    # Verify compilation error information is included
    assert "Compilation FAILED" in content_text or "Compilation completed with errors" in content_text, \
        "Response should indicate compilation failure"
    assert "ErrorScript.cs" in content_text, "Error should mention the script file"
    assert "error CS" in content_text, "Should contain C# error code"

    # Verify last compilation timestamp is present
    assert "Last compilation:" in content_text


@pytest.mark.structural
@pytest.mark.asyncio
async def test_assets_refresh_shows_last_compilation_status(mcp_session, unity_helper, temp_files):
    """Test that assets_refresh shows last compilation status even when no new compilation occurs

    Verifies the feature that shows compilation status from previous compilation
    when no new compilation is triggered during the current refresh.
    """
    # Create a file with error to trigger compilation
    script_path = unity_helper.create_temp_script_in_assets("StatusTestScript", "syntax")
    temp_files(script_path)

    # First refresh - triggers compilation
    result1 = await mcp_session.call_tool("assets_refresh", {"force": False})
    content1 = result1.content[0].text
    assert "Compilation FAILED" in content1 or "Compilation completed with errors" in content1

    # Second refresh - no file changes, should still show last compilation
    await asyncio.sleep(1)  # Brief wait
    result2 = await mcp_session.call_tool("assets_refresh", {"force": False})
    content2 = result2.content[0].text

    # Should show "Last compilation status:" when no new compilation
    assert "Last compilation" in content2, "Should show last compilation information"
    # Should still show the error from first compilation
    assert "StatusTestScript.cs" in content2 or "error CS" in content2


@pytest.mark.protocol
@pytest.mark.asyncio
async def test_assets_refresh_parameter_defaults(mcp_session):
    """Test that assets_refresh works with default parameters

    Verifies the tool handles missing optional parameters correctly.
    """
    # Call without explicit parameters (should use defaults)
    result = await mcp_session.call_tool("assets_refresh", {})

    content_text = result.content[0].text
    assert "Asset database refresh completed" in content_text


@pytest.mark.structural
@pytest.mark.asyncio
async def test_assets_refresh_with_timeout(mcp_session):
    """Test assets_refresh with custom timeout parameter

    Verifies that timeout parameter is accepted and handled.
    """
    # Call with custom timeout
    result = await mcp_session.call_tool("assets_refresh", {"force": False, "timeout": 90})

    content_text = result.content[0].text
    assert "Asset database refresh completed" in content_text
