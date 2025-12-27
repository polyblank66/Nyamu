"""
Test compilation error handling for standalone scripts
"""

import pytest
import asyncio
from mcp_client import MCPClient
from unity_helper import UnityHelper


@pytest.mark.compilation
@pytest.mark.essential
@pytest.mark.structural
@pytest.mark.asyncio
async def test_syntax_error_in_test_script(mcp_client, unity_helper, temp_files):
    """Test compilation with syntax error in test script"""

    # Create temporary script with syntax error
    error_script_path = unity_helper.create_temp_script_in_assets(
        "SyntaxErrorTest",
        "syntax"
    )
    temp_files(error_script_path)

    # Refresh with force to ensure Unity detects the error
    await unity_helper.refresh_assets_if_available(force=True)

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should indicate compilation errors
    assert "Compilation completed with errors:" in content_text
    assert "SyntaxErrorTest.cs" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_missing_using_error(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test compilation with missing using statement"""
    test_script_path = unity_helper.get_test_script_path()

    # Modify with missing using error
    unity_helper.modify_file_with_error(test_script_path, "missing_using")
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should have compilation errors
    assert "Compilation completed with errors:" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_undefined_variable_error(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test compilation with undefined variable"""
    test_script_path = unity_helper.get_test_script_path()

    # Modify with undefined variable error
    unity_helper.modify_file_with_error(test_script_path, "undefined_var")
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should have compilation errors
    assert "Compilation completed with errors:" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_new_script_with_syntax_error(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test compilation with new script containing syntax error"""
    # Create new script with syntax error
    new_script_path = unity_helper.create_temp_script_in_assets("ErrorScript", "syntax")
    temp_files(new_script_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should have compilation errors
    assert "Compilation completed with errors:" in content_text
    assert "ErrorScript.cs" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_new_script_with_missing_using(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test compilation with new script missing using statement"""
    # Create new script with missing using error
    new_script_path = unity_helper.create_temp_script_in_assets("MissingUsingScript", "missing_using")
    temp_files(new_script_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should have compilation errors
    assert "Compilation completed with errors:" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_multiple_errors_in_different_scripts(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test compilation with errors in multiple scripts"""
    # Create multiple scripts with different errors
    script1_path = unity_helper.create_temp_script_in_assets("ErrorScript1", "syntax")
    script2_path = unity_helper.create_temp_script_in_assets("ErrorScript2", "undefined_var")

    temp_files(script1_path)
    temp_files(script2_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should have compilation errors - Unity may not report all files simultaneously
    assert "Compilation completed with errors:" in content_text
    # At least one of the error files should be reported
    assert "ErrorScript1.cs" in content_text or "ErrorScript2.cs" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_fix_compilation_error(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test fixing compilation error and recompiling"""
    # Create script with error
    error_script_path = unity_helper.create_temp_script_in_assets("FixableScript", "syntax")
    temp_files(error_script_path)
    await unity_helper.refresh_assets_if_available()

    # First compilation should have errors
    response1 = await mcp_client.compilation_trigger(timeout=30)
    content_text1 = response1["result"]["content"][0]["text"]
    assert "Compilation completed with errors:" in content_text1

    # Fix the script by creating a correct version
    unity_helper.create_temp_script_in_assets("FixableScript", None)  # No error
    await unity_helper.refresh_assets_if_available()

    # Second compilation should be successful
    response2 = await mcp_client.compilation_trigger(timeout=30)
    content_text2 = response2["result"]["content"][0]["text"]
    assert "Compilation completed successfully with no errors." in content_text2


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_compilation_error_details(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test that compilation errors contain proper details"""
    # Create script with known error using fixtures
    error_script_path = unity_helper.create_temp_script_in_assets("DetailedErrorScript", "syntax")
    temp_files(error_script_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation and check error details
    response = await mcp_client.compilation_trigger(timeout=30)
    content_text = response["result"]["content"][0]["text"]

    # Should contain file path and line information
    assert "DetailedErrorScript.cs:" in content_text
    # Should contain error message details
    assert "Compilation completed with errors:" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_empty_script_compilation(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test compilation of empty script (should be valid)"""
    # Create minimal valid script
    import os
    empty_script_path = os.path.join(unity_helper.assets_path, "EmptyScript.cs")

    with open(empty_script_path, 'w') as f:
        f.write("// Empty script - should compile fine")

    temp_files(empty_script_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should compile successfully
    assert "Compilation completed successfully with no errors." in content_text