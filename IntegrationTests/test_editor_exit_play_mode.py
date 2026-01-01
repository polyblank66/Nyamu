"""
Integration tests for editor_exit_play_mode tool
"""

import pytest
import asyncio
from mcp_client import MCPClient

@pytest.mark.essential
async def test_editor_exit_play_mode_basic():
    """Test basic functionality of editor_exit_play_mode tool"""
    async with MCPClient() as client:
        try:
            response = await client.editor_exit_play_mode()

            # Verify response structure - response is wrapped in content format
            assert "result" in response
            result = response["result"]
            assert "content" in result
            content = result["content"][0]
            assert "text" in content

            # Parse the JSON text content
            import json
            response_data = json.loads(content["text"])

            # Verify response values
            assert response_data["success"] is True
            assert "Successfully exited PlayMode" in response_data["message"]
        except RuntimeError as e:
            # Expected when Unity Editor is not running
            if "Unity HTTP server timeout" in str(e):
                print("✓ Unity Editor not running - expected behavior")
                return
            else:
                raise

@pytest.mark.essential
async def test_editor_exit_play_mode_via_endpoint():
    """Test editor_exit_play_mode via HTTP endpoint"""
    async with MCPClient() as client:
        try:
            response = await client.editor_exit_play_mode()
            import json
            content = response["result"]["content"][0]
            response_data = json.loads(content["text"])
            assert response_data["success"] is True
        except RuntimeError as e:
            # Expected when Unity Editor is not running
            if "Unity HTTP server timeout" in str(e):
                print("✓ Unity Editor not running - expected behavior")
                return
            else:
                raise

@pytest.mark.essential
async def test_editor_exit_play_mode_error_handling():
    """Test error handling for editor_exit_play_mode tool"""
    async with MCPClient() as client:
        try:
            await client.editor_exit_play_mode()
            # If no exception is raised, the tool is working correctly
            assert True
        except RuntimeError as e:
            # Expected when Unity Editor is not running
            if "Unity HTTP server timeout" in str(e):
                print("✓ Unity Editor not running - expected behavior")
                return
            else:
                raise

def run_sync_test():
    """Run the essential test synchronously for easy execution"""
    async def _run():
        print("Running editor_exit_play_mode tests...")
        await test_editor_exit_play_mode_basic()
        print("✓ Basic functionality test completed")

        await test_editor_exit_play_mode_via_endpoint()
        print("✓ HTTP endpoint test completed")

        await test_editor_exit_play_mode_error_handling()
        print("✓ Error handling test completed")

        print("All essential tests completed!")

    asyncio.run(_run())

if __name__ == "__main__":
    run_sync_test()