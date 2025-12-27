"""
Test compilation error handling when creating new files with errors
"""

import pytest
import os
from mcp_client import MCPClient
from unity_helper import UnityHelper


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_new_file_syntax_error(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating a new file with syntax error"""
    # Create new script with syntax error
    new_script_path = unity_helper.create_temp_script_in_assets("NewSyntaxError", "syntax")
    temp_files(new_script_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text
    assert "NewSyntaxError.cs" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_new_file_missing_using(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating a new file with missing using statement"""
    # Create new script with missing using error
    new_script_path = unity_helper.create_temp_script_in_assets("NewMissingUsing", "missing_using")
    temp_files(new_script_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_new_file_undefined_variable(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating a new file with undefined variable"""
    # Create new script with undefined variable error
    new_script_path = unity_helper.create_temp_script_in_assets("NewUndefinedVar", "undefined_var")
    temp_files(new_script_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_multiple_new_files_with_errors(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating multiple new files with different errors"""
    # Create multiple new scripts with different errors
    script1_path = unity_helper.create_temp_script_in_assets("MultiError1", "syntax")
    script2_path = unity_helper.create_temp_script_in_assets("MultiError2", "missing_using")
    script3_path = unity_helper.create_temp_script_in_assets("MultiError3", "undefined_var")

    temp_files(script1_path)
    temp_files(script2_path)
    temp_files(script3_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text
    # At least one error file should be reported (Unity may not report all errors simultaneously)
    assert "MultiError1.cs" in content_text or "MultiError2.cs" in content_text or "MultiError3.cs" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_valid_new_file(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating a new valid file (should compile successfully)"""
    # Create new valid script
    new_script_path = unity_helper.create_temp_script_in_assets("ValidNewScript")  # No error
    temp_files(new_script_path)
    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should compile successfully
    assert "Compilation completed successfully with no errors." in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_and_delete_file_with_error(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating file with error, then deleting it"""
    # Create new script with error
    new_script_path = unity_helper.create_temp_script_in_assets("TempErrorScript", "syntax")
    temp_files(new_script_path)
    await unity_helper.refresh_assets_if_available()

    # First compilation should have errors
    response1 = await mcp_client.compilation_trigger(timeout=30)
    content_text1 = response1["result"]["content"][0]["text"]
    assert "Compilation completed with errors:" in content_text1

    # Remove the file and use force refresh to ensure Unity detects deletion
    os.remove(new_script_path)
    meta_file = new_script_path + ".meta"
    if os.path.exists(meta_file):
        os.remove(meta_file)

    # Use force refresh after file deletion to ensure Unity properly detects the removal
    await unity_helper.refresh_assets_if_available(force=True)

    # Second compilation should be successful (error file removed)
    response2 = await mcp_client.compilation_trigger(timeout=30)
    content_text2 = response2["result"]["content"][0]["text"]
    assert "Compilation completed successfully with no errors." in content_text2


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_file_in_subdirectory_with_error(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating file with error in subdirectory"""
    # Create subdirectory
    subdir_path = os.path.join(unity_helper.assets_path, "TestSubdir")
    os.makedirs(subdir_path, exist_ok=True)

    # Create script with error in subdirectory
    script_path = os.path.join(subdir_path, "SubdirErrorScript.cs")
    unity_helper.create_test_script_with_error(script_path, "syntax")

    temp_files(script_path)
    # Also register the subdirectory for cleanup
    temp_files(subdir_path)

    await unity_helper.refresh_assets_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text
    assert "SubdirErrorScript.cs" in content_text


@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_create_file_with_complex_error(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test creating file with more complex compilation error"""
    # Create script with complex error
    complex_script_path = unity_helper.create_temp_script_in_assets("ComplexErrorScript", "syntax")
    temp_files(complex_script_path)

    # Modify to have multiple errors
    complex_content = '''using UnityEngine;

public class ComplexErrorScript : MonoBehaviour
{
    void Start()
    {
        // Multiple errors in one file
        undefinedVariable = 42;  // Undefined variable
        Debug.Log("Missing semicolon")  // Missing semicolon

        // Type mismatch
        string number = 42;
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
    assert "ComplexErrorScript.cs" in content_text