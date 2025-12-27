"""
Test compilation for files inside TestModule assembly definition
"""

import pytest
import os
from mcp_client import MCPClient
from unity_helper import UnityHelper


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_modify_test_module_script_with_syntax_error(mcp_client, unity_helper, unity_state_manager):
    """Test modifying TestModuleScript.cs with syntax error"""
    test_module_script_path = unity_helper.get_test_module_script_path()

    # Modify TestModuleScript.cs with syntax error
    unity_helper.modify_file_with_error(test_module_script_path, "syntax")
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text
    assert "TestModuleScript.cs" in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_modify_test_module_script_with_missing_using(mcp_client, unity_helper, unity_state_manager):
    """Test modifying TestModuleScript.cs with missing using statement"""
    test_module_script_path = unity_helper.get_test_module_script_path()

    # Modify with missing using error
    unity_helper.modify_file_with_error(test_module_script_path, "missing_using")
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_modify_test_module_script_with_undefined_variable(mcp_client, unity_helper, unity_state_manager):
    """Test modifying TestModuleScript.cs with undefined variable"""
    test_module_script_path = unity_helper.get_test_module_script_path()

    # Modify with undefined variable error
    unity_helper.modify_file_with_error(test_module_script_path, "undefined_var")
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_new_file_in_test_module_with_syntax_error(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating new file in TestModule with syntax error"""
    # Create new script with syntax error in TestModule
    new_script_path = unity_helper.create_temp_script_in_test_module("TestModuleNewSyntax", "syntax")
    temp_files(new_script_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text
    assert "TestModuleNewSyntax.cs" in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_new_file_in_test_module_with_missing_using(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating new file in TestModule with missing using statement"""
    # Create new script with missing using error in TestModule
    new_script_path = unity_helper.create_temp_script_in_test_module("TestModuleNewMissing", "missing_using")
    temp_files(new_script_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_valid_file_in_test_module(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating valid file in TestModule"""
    # Create new valid script in TestModule
    new_script_path = unity_helper.create_temp_script_in_test_module("TestModuleValid")  # No error
    temp_files(new_script_path)
    await unity_helper.refresh_assets_if_available(force=True)

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should compile successfully
    assert "Compilation completed successfully with no errors." in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_multiple_errors_in_test_module(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test multiple errors in different files within TestModule"""
    # Create multiple scripts with errors in TestModule
    script1_path = unity_helper.create_temp_script_in_test_module("TestModuleError1", "syntax")
    script2_path = unity_helper.create_temp_script_in_test_module("TestModuleError2", "undefined_var")

    temp_files(script1_path)
    temp_files(script2_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors - Unity may not report all files simultaneously
    assert "Compilation completed with errors:" in content_text
    # At least one of the error files should be reported (Unity typically reports one file at a time)
    assert "TestModuleError1.cs" in content_text or "TestModuleError2.cs" in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_error_in_test_module_and_assets(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test errors in both TestModule and Assets folder"""
    # Create error in Assets
    assets_script_path = unity_helper.create_temp_script_in_assets("AssetsError", "syntax")
    temp_files(assets_script_path)

    # Create error in TestModule
    module_script_path = unity_helper.create_temp_script_in_test_module("TestModuleError", "missing_using")
    temp_files(module_script_path)

    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors - Unity may not report all assemblies simultaneously
    assert "Compilation completed with errors:" in content_text
    # At least one of the error files should be reported (Unity typically reports one assembly at a time)
    assert "AssetsError.cs" in content_text or "TestModuleError.cs" in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_fix_error_in_test_module(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test fixing error in TestModule and recompiling"""
    # Create script with error in TestModule
    error_script_path = unity_helper.create_temp_script_in_test_module("TestModuleFixable", "syntax")
    temp_files(error_script_path)
    await unity_helper.refresh_assets_if_available()

    # First compilation should have errors
    response1 = await mcp_client.compilation_trigger(timeout=30)
    content_text1 = response1["result"]["content"][0]["text"]
    assert "Compilation completed with errors:" in content_text1

    # Fix the script by creating a correct version
    unity_helper.create_temp_script_in_test_module("TestModuleFixable", None)  # No error
    await unity_helper.refresh_assets_if_available(force=True)

    # Give Unity a moment to process the file change
    import asyncio
    await asyncio.sleep(1)

    # Second compilation should be successful
    response2 = await mcp_client.compilation_trigger(timeout=30)
    content_text2 = response2["result"]["content"][0]["text"]
    assert "Compilation completed successfully with no errors." in content_text2


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.asyncio
async def test_test_module_asmdef_structure():
    """Test that TestModule.asmdef has proper structure"""
    unity_helper = UnityHelper()
    asmdef_path = unity_helper.get_test_module_asmdef_path()

    assert os.path.exists(asmdef_path), "TestModule.asmdef should exist"

    # Read and parse asmdef file
    import json
    with open(asmdef_path, 'r') as f:
        asmdef_content = json.load(f)

    # Should have required fields
    assert "name" in asmdef_content
    assert asmdef_content["name"] == "TestModule"


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_complex_error_in_test_module(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test complex compilation error in TestModule"""
    # Use the proper test framework to create script with multiple errors
    complex_script_path = unity_helper.create_temp_script_in_test_module("TestModuleComplex")
    temp_files(complex_script_path)

    # Override with complex error content
    complex_content = '''using UnityEngine;

public class TestModuleComplex
{
    public void TestMethod()
    {
        // Multiple errors
        undefinedVariable = "test";  // Undefined variable
        Debug.Log("Missing semicolon")  // Missing semicolon

        // Invalid method call
        NonExistentMethod();
    }
}'''

    with open(complex_script_path, 'w') as f:
        f.write(complex_content)

    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)
    content_text = response["result"]["content"][0]["text"]

    # Should contain multiple compilation errors
    assert "Compilation completed with errors:" in content_text
    assert "TestModuleComplex.cs" in content_text