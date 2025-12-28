"""
Test compile status functionality via direct HTTP requests
"""

import pytest
import requests
import json


@pytest.mark.compilation
@pytest.mark.protocol
def test_compile_status_endpoint():
    """Test compilation-status HTTP endpoint directly"""
    response = requests.get("http://localhost:17932/compilation-status")

    assert response.status_code == 200
    assert response.headers["content-type"] == "application/json"

    data = response.json()
    assert "status" in data
    assert "isCompiling" in data
    assert "lastCompilationTime" in data
    assert "lastCompilationRequestTime" in data
    assert "errors" in data

    # Status should be either "idle" or "compiling"
    assert data["status"] in ["idle", "compiling"]
    assert isinstance(data["isCompiling"], bool)
    assert isinstance(data["errors"], list)


@pytest.mark.compilation
@pytest.mark.protocol
def test_compile_status_response_structure():
    """Test that compile status response has correct structure"""
    response = requests.get("http://localhost:17932/compilation-status")
    data = response.json()

    # Check all required fields are present
    required_fields = ["status", "isCompiling", "lastCompilationTime", "lastCompilationRequestTime", "errors"]
    for field in required_fields:
        assert field in data

    # Check field types
    assert isinstance(data["status"], str)
    assert isinstance(data["isCompiling"], bool)
    assert isinstance(data["lastCompilationTime"], str)
    assert isinstance(data["lastCompilationRequestTime"], str)
    assert isinstance(data["errors"], list)

    # If there are errors, they should have proper structure
    for error in data["errors"]:
        assert "file" in error
        assert "line" in error
        assert "message" in error
        assert isinstance(error["file"], str)
        assert isinstance(error["line"], int)
        assert isinstance(error["message"], str)


@pytest.mark.compilation
@pytest.mark.protocol
def test_compile_status_idle_state():
    """Test compile status when Unity is idle"""
    # First trigger compilation to ensure it completes
    requests.get("http://localhost:17932/compilation-trigger", timeout=60)

    # Poll status until idle (with timeout)
    import time
    max_wait = 10
    start_time = time.time()
    while time.time() - start_time < max_wait:
        response = requests.get("http://localhost:17932/compilation-status")
        data = response.json()
        if data["status"] == "idle":
            break
        time.sleep(0.5)

    # Should be idle after compilation
    response = requests.get("http://localhost:17932/compilation-status")
    data = response.json()
    assert data["status"] == "idle"
    assert data["isCompiling"] is False


@pytest.mark.compilation
@pytest.mark.protocol
def test_compile_status_headers():
    """Test that compile status endpoint returns proper headers"""
    response = requests.get("http://localhost:17932/compilation-status")

    # Check CORS headers
    assert response.headers.get("access-control-allow-origin") == "*"
    assert response.headers.get("access-control-allow-methods") is not None
    assert response.headers.get("access-control-allow-headers") is not None


@pytest.mark.compilation
@pytest.mark.protocol
def test_compile_status_multiple_requests():
    """Test multiple consecutive requests to compile status"""
    responses = []

    for i in range(3):
        response = requests.get("http://localhost:17932/compilation-status")
        assert response.status_code == 200
        responses.append(response.json())

    # All responses should have valid structure
    for data in responses:
        assert "status" in data
        assert "isCompiling" in data
        assert "lastCompilationTime" in data
        assert "lastCompilationRequestTime" in data
        assert "errors" in data