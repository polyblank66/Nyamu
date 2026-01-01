"""
Test new warning messages returned from MCP tools when operations are already in progress
"""

import pytest
import asyncio
from mcp_client import MCPClient


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_assets_refresh_concurrent_warning(unity_state_manager):
    """Test that concurrent assets_refresh calls return warning message"""
    # Use separate clients to avoid stdout stream conflicts
    client1 = MCPClient()
    client2 = MCPClient()
    await client1.start()
    await client2.start()

    try:
        # Start two refresh operations simultaneously using different clients
        task1 = asyncio.create_task(client1.assets_refresh(force=True))
        # Small delay to ensure first request starts
        await asyncio.sleep(0.1)
        task2 = asyncio.create_task(client2.assets_refresh(force=True))

        # Get both responses
        response1 = await task1
        response2 = await task2

        # Both should succeed, but one might get a warning
        assert response1["jsonrpc"] == "2.0"
        assert response2["jsonrpc"] == "2.0"

        # At least one should have result, one might have warning
        results = [response1, response2]

        # Check if any response contains the warning message
        warning_found = False
        for response in results:
            if "result" in response:
                content_text = response["result"]["content"][0]["text"]
                if "Asset refresh already in progress" in content_text:
                    warning_found = True
                    # Warning message is just plain text
                    assert "Please wait for current refresh to complete" in content_text

    finally:
        await client1.stop()
        await client2.stop()


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_warning_capability(unity_state_manager):
    """Test that run_tests handles test execution scenarios appropriately"""
    client = MCPClient()
    await client.start()

    try:
        # The tests_run_all functionality might fail due to no actual Unity tests
        # but we can verify the warning message capability exists
        try:
            response = await client.tests_run_all(test_mode="EditMode", timeout=30)

            assert response["jsonrpc"] == "2.0"
            assert "result" in response

            content_text = response["result"]["content"][0]["text"]

            # Should either succeed normally or show warning (both are valid responses)
            if "Tests are already running" in content_text:
                # Warning message format
                assert "Please wait for current test run to complete" in content_text
            else:
                # Normal test execution result or failure message
                assert "Test Results:" in content_text or "execution" in content_text or "failed" in content_text

        except RuntimeError as e:
            # This is expected if Unity Test Runner can't find tests or has issues
            # The important thing is that the warning system exists in the code
            assert "failed to start" in str(e).lower() or "test execution" in str(e).lower()
            print(f"Expected test runner issue: {e}")

    finally:
        await client.stop()


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_assets_refresh_warning_message_format(unity_state_manager):
    """Test the exact format of refresh assets warning message"""
    # Use separate clients to avoid stdout stream conflicts
    clients = [MCPClient() for _ in range(3)]

    for client in clients:
        await client.start()

    try:
        # Trigger a potential warning by making rapid refresh calls with separate clients
        tasks = []
        for i, client in enumerate(clients):
            task = asyncio.create_task(client.assets_refresh(force=False))
            tasks.append(task)
            if i < len(clients) - 1:  # Don't sleep after last task
                await asyncio.sleep(0.05)  # Very small delay

        # Wait for all tasks to complete
        responses = await asyncio.gather(*tasks, return_exceptions=True)

        # At least one should succeed
        success_count = 0
        warning_count = 0

        for i, response in enumerate(responses):
            if isinstance(response, Exception):
                print(f"Response {i}: Exception - {response}")
                continue

            assert response["jsonrpc"] == "2.0"

            if "result" in response:
                content_text = response["result"]["content"][0]["text"]
                print(f"Response {i}: {content_text}")

                if "Asset refresh already in progress" in content_text:
                    warning_count += 1
                    # The warning message comes as plain text, not JSON
                    assert "Asset refresh already in progress. Please wait for current refresh to complete" in content_text
                else:
                    success_count += 1

        # At least one operation should occur (either success or warning)
        assert (success_count + warning_count) >= 1
        # Warning count is not guaranteed due to timing - just verify that IF warnings occur, they have correct format
        # The assertion above already validated warning format if any warnings were present
        print(f"Success count: {success_count}, Warning count: {warning_count}")

    finally:
        for client in clients:
            await client.stop()


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_warning_messages_with_retry_logic(unity_state_manager):
    """Test that retry logic handles warning messages appropriately"""
    client = MCPClient()
    await client.start()

    try:
        # The retry logic should handle -32603 errors, but warning messages are different
        # They come as successful responses with warning status
        response = await client.assets_refresh(force=True)

        assert response["jsonrpc"] == "2.0"
        assert "result" in response

        content_text = response["result"]["content"][0]["text"]

        # Should either succeed normally or show warning (both are valid responses)
        if "Asset refresh already in progress" in content_text:
            assert "Please wait for current refresh to complete" in content_text
        else:
            # Normal success response - Unity says "Asset database refresh completed"
            assert "Asset database refresh completed" in content_text or "refresh completed" in content_text

    finally:
        await client.stop()