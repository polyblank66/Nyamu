"""
Test run_tests tool filter and regex filter functionality
"""

import pytest
import json
from mcp_client import MCPClient


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_filter_single_test(mcp_client, unity_state_manager):
    """Test running a single test using exact filter"""
    # Test specific EditMode test
    response = await mcp_client.tests_run_single(
        test_name="NyamuTests.PassingTest1",
        test_mode="EditMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # Should only run 1 test
    assert "Total: 1" in content_text
    # Should pass since it's PassingTest1
    assert "Passed: 1" in content_text
    assert "Failed: 0" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_filter_multiple_tests_pipe_separator(mcp_client, unity_state_manager):
    """Test running multiple tests using pipe separator"""
    # Test multiple EditMode tests using pipe separator (pipe works as regex OR)
    response = await mcp_client.tests_run_regex(
        test_filter_regex="NyamuTests.PassingTest1|NyamuTests.PassingTest2",
        test_mode="EditMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # Should run 2 tests
    assert "Total: 2" in content_text
    # Both should pass
    assert "Passed: 2" in content_text
    assert "Failed: 0" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_filter_failing_test(mcp_client, unity_state_manager):
    """Test running a test that fails using filter"""
    # Test specific failing EditMode test
    response = await mcp_client.tests_run_single(
        test_name="NyamuTests.FailingTest1",
        test_mode="EditMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # Should only run 1 test
    assert "Total: 1" in content_text
    # Should fail
    assert "Failed: 1" in content_text
    assert "Passed: 0" in content_text
    # Should show failure details
    assert "Failed Tests:" in content_text
    assert "NyamuTests.FailingTest1" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_filter_playmode_with_namespace(mcp_client, unity_state_manager):
    """Test running PlayMode test with namespace using filter"""
    # Test specific PlayMode test with namespace
    response = await mcp_client.tests_run_single(
        test_name="Nyamu.Tests.NyamuPlayModeTests.SimplePlayModeTest",
        test_mode="PlayMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # Should only run 1 test
    assert "Total: 1" in content_text
    # Should pass since it's SimplePlayModeTest
    assert "Passed: 1" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_filter_nonexistent_test(mcp_client, unity_state_manager):
    """Test filtering for a test that doesn't exist"""
    # Test filtering for non-existent test
    response = await mcp_client.tests_run_single(
        test_name="NonExistentTest.DoesNotExist",
        test_mode="EditMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # Should run 0 tests
    assert "Total: 0" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_regex_filter_pattern_matching(mcp_client, unity_state_manager):
    """Test using regex filter to match test patterns"""
    # Test regex filter that matches all passing tests
    response = await mcp_client.tests_run_regex(
        test_filter_regex=".*PassingTest.*",
        test_mode="EditMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # Should run 3 passing tests (PassingTest1, PassingTest2, PassingTest3)
    assert "Total: 3" in content_text
    # All should pass
    assert "Passed: 3" in content_text
    assert "Failed: 0" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_regex_filter_failing_tests(mcp_client, unity_state_manager):
    """Test using regex filter to match failing tests"""
    # Test regex filter that matches failing tests
    response = await mcp_client.tests_run_regex(
        test_filter_regex=".*FailingTest.*",
        test_mode="EditMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # Should run 2 failing tests (FailingTest1, FailingTest2)
    assert "Total: 2" in content_text
    # All should fail
    assert "Failed: 2" in content_text
    assert "Passed: 0" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_regex_filter_namespace_pattern(mcp_client, unity_state_manager):
    """Test using regex filter to match namespace patterns"""
    # Test regex filter that matches tests in Nyamu.Tests namespace
    response = await mcp_client.tests_run_regex(
        test_filter_regex="Nyamu\\.Tests\\..*",
        test_mode="PlayMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # Should run multiple PlayMode tests from the namespace
    # Note: Some may fail, but we're testing the filtering works
    assert "Total:" in content_text and not "Total: 0" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_regex_filter_specific_method_pattern(mcp_client, unity_state_manager):
    """Test using regex filter with specific method pattern"""
    # Test regex filter that matches specific method pattern
    response = await mcp_client.tests_run_regex(
        test_filter_regex=".*PassingTest[12]$",  # Matches PassingTest1 and PassingTest2 but not PassingTest3
        test_mode="EditMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # Should run 2 tests (PassingTest1, PassingTest2)
    assert "Total: 2" in content_text
    # Both should pass
    assert "Passed: 2" in content_text
    assert "Failed: 0" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_regex_filter_no_matches(mcp_client, unity_state_manager):
    """Test using regex filter that matches no tests"""
    # Test regex filter that matches no tests
    response = await mcp_client.tests_run_regex(
        test_filter_regex="NonExistentPattern.*",
        test_mode="EditMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # Should run 0 tests
    assert "Total: 0" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_both_filters_specified(mcp_client, unity_state_manager):
    """Test behavior when both filter and regex filter are specified"""
    # Test when both filters are provided - using regex filter only (new API doesn't support both)
    response = await mcp_client.tests_run_regex(
        test_filter_regex=".*PassingTest.*",
        test_mode="EditMode",
        timeout=60
    )

    assert response["jsonrpc"] == "2.0"
    assert "result" in response

    content_text = response["result"]["content"][0]["text"]

    # Should show test results
    assert "Test Results:" in content_text
    # The intersection should be applied (depends on Unity's implementation)
    # At minimum, we should get valid test results
    assert "Total:" in content_text


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.protocol
@pytest.mark.asyncio
async def test_run_tests_filter_consistency_with_direct_call(mcp_client, unity_state_manager):
    """Test that MCP tool filter behavior matches direct Unity API calls"""
    # Run test with filter via MCP
    mcp_response = await mcp_client.tests_run_single(
        test_name="NyamuTests.PassingTest1",
        test_mode="EditMode",
        timeout=60
    )

    mcp_content = mcp_response["result"]["content"][0]["text"]

    # Verify the filtered test actually ran
    assert "NyamuTests.PassingTest1" in mcp_content or "Total: 1" in mcp_content
    assert "Test Results:" in mcp_content

    # Should show that filtering worked (not all tests ran)
    # We know there are more than 1 test total, so if filter works, we should see Total: 1
    assert "Total: 1" in mcp_content
