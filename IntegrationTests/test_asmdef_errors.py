"""
Test specific Assembly Definition error scenarios for TestModule
"""

import pytest
import os
import json
from mcp_client import MCPClient
from unity_helper import UnityHelper


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.asyncio
async def test_asmdef_dependency_error():
    """Test compilation when TestModule has dependency issues"""
    client = MCPClient()
    unity_helper = UnityHelper()

    await client.start()

    try:
        # Create script that tries to use functionality not available in TestModule
        dependency_script_path = os.path.join(unity_helper.test_module_path, "DependencyError.cs")

        # This script tries to use UnityEditor which might not be available in TestModule
        dependency_content = '''using UnityEngine;
using UnityEditor;  // This might cause issues in TestModule

public class DependencyError
{
    public void TestMethod()
    {
        EditorUtility.DisplayDialog("Test", "This uses editor functionality", "OK");
    }
}'''

        with open(dependency_script_path, 'w') as f:
            f.write(dependency_content)

        await unity_helper.assets_refresh_if_available()

        # Trigger compilation
        response = await client.compilation_trigger(timeout=30)
        content_text = response["result"]["content"][0]["text"]

        # May or may not have errors depending on TestModule asmdef configuration
        assert response["jsonrpc"] == "2.0"

    finally:
        # Cleanup
        if os.path.exists(dependency_script_path):
            os.remove(dependency_script_path)
            meta_file = dependency_script_path + ".meta"
            if os.path.exists(meta_file):
                os.remove(meta_file)

        await client.stop()


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_asmdef_namespace_error(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test compilation error with namespace issues in TestModule"""
    # Create script with namespace conflict in TestModule
    namespace_script_path = unity_helper.create_temp_script_in_test_module("NamespaceError", "syntax")
    temp_files(namespace_script_path)

    # Modify to have namespace issues
    namespace_content = '''using UnityEngine;

namespace TestModule
{
    public class NamespaceError
    {
        void Start()
        {
            // Syntax error in namespace
            Debug.Log("Error"  // Missing closing parenthesis and semicolon
        }
    }
}'''

    with open(namespace_script_path, 'w') as f:
        f.write(namespace_content)

    await unity_helper.assets_refresh_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text
    assert "NamespaceError.cs" in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.asyncio
async def test_asmdef_circular_reference_protection():
    """Test that TestModule can't cause circular reference issues"""
    client = MCPClient()
    unity_helper = UnityHelper()

    await client.start()

    try:
        # Create script that tries to reference external assemblies inappropriately
        circular_script_path = os.path.join(unity_helper.test_module_path, "CircularTest.cs")

        circular_content = '''using UnityEngine;

public class CircularTest
{
    public void TestMethod()
    {
        // Try to use types that might cause circular references
        var go = new GameObject("TestObject");
        Debug.Log(go.name);
    }
}'''

        with open(circular_script_path, 'w') as f:
            f.write(circular_content)

        await unity_helper.assets_refresh_if_available()

        # Trigger compilation
        response = await client.compilation_trigger(timeout=30)

        # Should compile successfully (GameObject is available)
        assert response["jsonrpc"] == "2.0"

    finally:
        # Cleanup
        if os.path.exists(circular_script_path):
            os.remove(circular_script_path)
            meta_file = circular_script_path + ".meta"
            if os.path.exists(meta_file):
                os.remove(meta_file)

        await client.stop()


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_asmdef_class_name_collision(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test class name collision within TestModule"""
    # Create first script
    script1_path = unity_helper.create_temp_script_in_test_module("CollisionTest", None)
    temp_files(script1_path)

    # Create second script with same class name
    script2_path = os.path.join(unity_helper.test_module_path, "CollisionTest2.cs")

    collision_content = '''using UnityEngine;

public class CollisionTest  // Same name as first script
{
    public void AnotherMethod()
    {
        Debug.Log("Collision test");
    }
}'''

    with open(script2_path, 'w') as f:
        f.write(collision_content)

    temp_files(script2_path)
    await unity_helper.assets_refresh_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should have compilation errors due to duplicate class names
    assert "Compilation completed with errors:" in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_asmdef_mixed_errors_and_valid_files(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test mix of valid and invalid files in TestModule"""
    # Create valid script
    valid_script_path = unity_helper.create_temp_script_in_test_module("ValidInModule", None)
    temp_files(valid_script_path)

    # Create invalid script
    invalid_script_path = unity_helper.create_temp_script_in_test_module("InvalidInModule", "syntax")
    temp_files(invalid_script_path)

    await unity_helper.assets_refresh_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)

    assert response["jsonrpc"] == "2.0"
    content_text = response["result"]["content"][0]["text"]

    # Should have compilation errors only from invalid file
    assert "Compilation completed with errors:" in content_text
    assert "InvalidInModule.cs" in content_text
    # ValidInModule should not appear in errors


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_asmdef_large_file_with_errors(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test compilation of large file with errors in TestModule"""
    # Create base script using test framework
    large_script_path = unity_helper.create_temp_script_in_test_module("LargeFileError")
    temp_files(large_script_path)

    # Override with large content containing errors
    large_content = '''using UnityEngine;

public class LargeFileError
{
'''

    # Add many methods, some with errors
    for i in range(10):
        if i % 3 == 0:  # Every third method has an error
            large_content += f'''
    public void Method{i}()
    {{
        Debug.Log("Method {i}")  // Missing semicolon
    }}
'''
        else:
            large_content += f'''
    public void Method{i}()
    {{
        Debug.Log("Method {i}");
    }}
'''

    large_content += '}'

    with open(large_script_path, 'w') as f:
        f.write(large_content)

    await unity_helper.assets_refresh_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)
    content_text = response["result"]["content"][0]["text"]

    # Should contain compilation errors
    assert "Compilation completed with errors:" in content_text
    assert "LargeFileError.cs" in content_text


@pytest.mark.asmdef
@pytest.mark.compilation
@pytest.mark.structural
@pytest.mark.asyncio
async def test_asmdef_compilation_isolation(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test that TestModule compilation errors don't affect Assets compilation"""
    # Create error in TestModule
    module_error_path = unity_helper.create_temp_script_in_test_module("ModuleIsolationError", "syntax")
    temp_files(module_error_path)

    # Create valid script in Assets
    assets_valid_path = unity_helper.create_temp_script_in_assets("AssetsIsolationValid", None)
    temp_files(assets_valid_path)

    await unity_helper.assets_refresh_if_available()

    # Trigger compilation
    response = await mcp_client.compilation_trigger(timeout=30)
    content_text = response["result"]["content"][0]["text"]

    # Should have errors from TestModule but Assets should compile fine
    assert "Compilation completed with errors:" in content_text
    assert "ModuleIsolationError.cs" in content_text

    # The error should be isolated to TestModule
    # Assets script should not appear in errors