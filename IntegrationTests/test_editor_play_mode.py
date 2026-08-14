"""
Integration tests for editor_enter_play_mode / editor_exit_play_mode
"""

import asyncio
import json
import os
import time

import pytest

from mcp_client import MCPClient


def _payload(response):
    return json.loads(response["result"]["content"][0]["text"])


async def _poll_status(client, predicate, timeout=90.0, interval=1.0):
    """Poll editor_status until predicate holds, tolerating the domain-reload
    gap. There is deliberately no server-side retry for that gap - the client
    is expected to retry, and this helper is the reference implementation."""
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        try:
            last = _payload(await client.editor_status())
            if not last["isStateStale"] and predicate(last):
                return last
        except RuntimeError:
            pass  # server down mid-reload: ECONNREFUSED / ECONNRESET
        await asyncio.sleep(interval)
    raise AssertionError(f"editor_status never satisfied predicate; last={last}")


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.essential
@pytest.mark.asyncio
async def test_play_mode_tools_are_advertised(mcp_client, unity_state_manager):
    """Regression test: editor_exit_play_mode shipped without a
    capabilities.tools entry, so tools/list never advertised it."""
    names = {t["name"] for t in (await mcp_client.list_tools())["result"]["tools"]}
    assert "editor_enter_play_mode" in names
    assert "editor_exit_play_mode" in names


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.asyncio
async def test_exit_when_not_playing_is_honest(mcp_client, unity_state_manager):
    """Exiting Play Mode when already in Edit Mode must not claim success at
    something that didn't happen."""
    status = _payload(await mcp_client.editor_status())
    if status["isPlaying"] or status["isEnteringPlayMode"]:
        pytest.skip("Editor is in or entering Play Mode")

    data = _payload(await mcp_client.editor_exit_play_mode())
    assert data["success"] is True
    assert data["status"] == "not_playing"
    assert data["wasPlaying"] is False
    assert "Successfully exited" not in data["message"]


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.asyncio
async def test_enter_when_already_playing_is_honest(mcp_client, unity_state_manager):
    """Entering Play Mode when already playing is a no-op, not a failure."""
    status = _payload(await mcp_client.editor_status())
    if not status["isPlaying"]:
        pytest.skip("Editor is not in Play Mode")

    data = _payload(await mcp_client.editor_enter_play_mode())
    assert data["success"] is True
    assert data["status"] == "already_playing"
    assert data["wasPlaying"] is True


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.asyncio
async def test_play_mode_round_trip():
    """Full enter/exit round trip through two domain reloads. Never marked
    essential/protocol - this is slow and triggers real editor state changes."""
    if os.environ.get("NYAMU_SERIAL_BATCH_MODE") == "true":
        pytest.skip("Play Mode round-trip is not meaningful in batch mode")

    client = MCPClient()
    await client.start()
    try:
        enter = _payload(await client.editor_enter_play_mode())
        assert enter["success"] is True
        assert enter["status"] in ("requested", "already_playing")

        await _poll_status(client, lambda s: s["isPlaying"] is True)

        exit_ = _payload(await client.editor_exit_play_mode())
        assert exit_["success"] is True
        assert exit_["status"] in ("exit_requested", "not_playing")

        await _poll_status(
            client,
            lambda s: s["isPlaying"] is False and s["isExitingPlayMode"] is False,
        )
    finally:
        # Never leave a worker's Editor in Play Mode - it would poison every
        # later test that runs against this Unity instance.
        try:
            await client.editor_exit_play_mode()
            await _poll_status(client, lambda s: s["isPlaying"] is False, timeout=60.0)
        except Exception:
            pass
        await client.stop()
