"""
Test specific refresh behavior - force vs regular refresh
"""

import pytest
import os
from mcp_client import MCPClient
from unity_helper import UnityHelper


@pytest.mark.mcp
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_force_refresh_vs_regular_refresh(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test the difference between force refresh and regular refresh for file deletions"""

    # Create a test file
    test_script_path = unity_helper.create_temp_script_in_assets("RefreshTestScript", "syntax")
    temp_files(test_script_path)

    # Regular refresh after file creation
    await unity_helper.assets_refresh_if_available(force=False)

    # First compilation should have errors
    response1 = await mcp_client.scripts_compile(timeout=30)
    content_text1 = response1["result"]["content"][0]["text"]
    assert "Compilation completed with errors:" in content_text1
    assert "RefreshTestScript.cs" in content_text1

    # Now delete the file manually (simulating the DeleteFileIssue scenario)
    os.remove(test_script_path)
    meta_file = test_script_path + ".meta"
    if os.path.exists(meta_file):
        os.remove(meta_file)

    # Try regular refresh first (should potentially still have issues)
    await unity_helper.assets_refresh_if_available(force=False)

    # Try compilation - might still fail with CS2001
    response2 = await mcp_client.scripts_compile(timeout=30)
    content_text2 = response2["result"]["content"][0]["text"]

    # If regular refresh didn't work, try force refresh
    if "CS2001" in content_text2 or "could not be found" in content_text2:
        print("Regular refresh didn't clear deleted file reference, trying force refresh...")

        # Force refresh should fix the issue
        await unity_helper.assets_refresh_if_available(force=True)

        # Now compilation should succeed
        response3 = await mcp_client.scripts_compile(timeout=30)
        content_text3 = response3["result"]["content"][0]["text"]
        assert "Compilation completed successfully with no errors." in content_text3
    else:
        # If regular refresh worked, that's fine too
        print("Regular refresh successfully cleared deleted file reference")
        assert "Compilation completed successfully with no errors." in content_text2


@pytest.mark.mcp
@pytest.mark.asyncio
async def test_assets_refresh_tool_parameters(mcp_client, unity_state_manager):
    """Test that the assets_refresh tool accepts force parameter correctly"""

    # Test regular refresh
    response1 = await mcp_client.assets_refresh(force=False)
    assert response1["jsonrpc"] == "2.0"
    assert "result" in response1
    content_text1 = response1["result"]["content"][0]["text"]
    assert "refresh" in content_text1.lower()

    # Test force refresh
    response2 = await mcp_client.assets_refresh(force=True)
    assert response2["jsonrpc"] == "2.0"
    assert "result" in response2
    content_text2 = response2["result"]["content"][0]["text"]
    assert "refresh" in content_text2.lower()


@pytest.mark.mcp
@pytest.mark.asyncio
async def test_mcp_tools_list_includes_force_parameter():
    """Test that tools/list includes the force parameter in assets_refresh"""

    client = MCPClient()
    await client.start()

    try:
        response = await client.list_tools()

        assert response["jsonrpc"] == "2.0"
        tools = response["result"]["tools"]

        # Find assets_refresh tool
        refresh_tool = None
        for tool in tools:
            if tool["name"] == "assets_refresh":
                refresh_tool = tool
                break

        assert refresh_tool is not None, "assets_refresh tool not found"

        # Check that force parameter is documented
        input_schema = refresh_tool["inputSchema"]
        assert "properties" in input_schema
        assert "force" in input_schema["properties"]

        force_param = input_schema["properties"]["force"]
        assert force_param["type"] == "boolean"
        assert "description" in force_param
        assert "ForceUpdate" in force_param["description"]

        # Check tool description mentions force usage
        description = refresh_tool["description"]
        assert "force=true" in description
        assert "deletions" in description.lower()

    finally:
        await client.stop()