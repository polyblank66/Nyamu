"""
Test test status functionality via direct HTTP requests
"""

import pytest
import requests
import json

@pytest.mark.mcp
@pytest.mark.protocol
def test_test_status_endpoint(unity_base_url):
    """Test tests-status HTTP endpoint directly"""
    response = requests.get(f"{unity_base_url}/tests-run-status")

    assert response.status_code == 200
    assert response.headers["content-type"] == "application/json"

    data = response.json()
    assert "status" in data
    assert "isRunning" in data
    assert "lastTestTime" in data
    assert "testRunId" in data

    # Status should be either "idle" or "running"
    assert data["status"] in ["idle", "running"]
    assert isinstance(data["isRunning"], bool)

@pytest.mark.mcp
@pytest.mark.protocol
def test_test_status_response_structure(unity_base_url):
    """Test that tests status response has correct structure"""
    response = requests.get(f"{unity_base_url}/tests-run-status")
    data = response.json()

    # Check required fields
    required_fields = ["status", "isRunning", "lastTestTime", "testRunId"]
    for field in required_fields:
        assert field in data

    # Check field types
    assert isinstance(data["status"], str)
    assert isinstance(data["isRunning"], bool)
    assert isinstance(data["lastTestTime"], str)

    # testRunId can be null or string
    if data["testRunId"] is not None:
        assert isinstance(data["testRunId"], str)

@pytest.mark.mcp
@pytest.mark.protocol
def test_test_status_with_results(unity_base_url):
    """Test tests status when test results are available"""
    # First run some tests to get results
    requests.get(f"{unity_base_url}/tests-run-all?mode=EditMode")

    # Wait for tests to complete
    import time
    max_wait = 30
    waited = 0

    while waited < max_wait:
        response = requests.get(f"{unity_base_url}/tests-run-status")
        data = response.json()

        if data["status"] == "idle" and data["testResults"] is not None:
            break

        time.sleep(1)
        waited += 1

    # Should have test results now
    if data["testResults"] is not None:
        results = data["testResults"]
        assert isinstance(results, dict)

        # Check test results structure
        expected_fields = ["totalTests", "passedTests", "failedTests", "skippedTests", "duration"]
        for field in expected_fields:
            assert field in results
            assert isinstance(results[field], (int, float))

        # Should have some results array
        if "results" in results:
            assert isinstance(results["results"], list)

@pytest.mark.mcp
@pytest.mark.protocol
def test_test_status_idle_state(unity_base_url):
    """Test tests status when no tests are running"""
    # Make sure no tests are running by checking status multiple times
    response = requests.get(f"{unity_base_url}/tests-status")
    data = response.json()

    if data["status"] == "idle":
        assert data["isRunning"] is False

@pytest.mark.mcp
@pytest.mark.protocol
def test_test_status_headers(unity_base_url):
    """Test that tests status endpoint returns proper headers"""
    response = requests.get(f"{unity_base_url}/tests-status")

    # Check CORS headers
    assert response.headers.get("access-control-allow-origin") == "*"
    assert response.headers.get("access-control-allow-methods") is not None
    assert response.headers.get("access-control-allow-headers") is not None

@pytest.mark.mcp
@pytest.mark.protocol
def test_test_status_multiple_requests(unity_base_url):
    """Test multiple consecutive requests to tests status"""
    responses = []

    for i in range(3):
        response = requests.get(f"{unity_base_url}/tests-run-status")
        assert response.status_code == 200
        responses.append(response.json())

    # All responses should have valid structure
    for data in responses:
        assert "status" in data
        assert "isRunning" in data
        assert "lastTestTime" in data
        assert "testRunId" in data