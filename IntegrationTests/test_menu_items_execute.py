"""
Integration tests for menu_items_execute
"""

import json

import pytest


def _payload(response):
    return json.loads(response["result"]["content"][0]["text"])


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.essential
@pytest.mark.asyncio
async def test_menu_items_execute_is_advertised(mcp_client, unity_state_manager):
    names = {t["name"] for t in (await mcp_client.list_tools())["result"]["tools"]}
    assert "menu_items_execute" in names


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.asyncio
async def test_bogus_menu_path_is_honest(mcp_client, unity_state_manager):
    """A menu path that does not exist must be reported as not_executed, not
    conflated with a main-thread timeout."""
    data = _payload(
        await mcp_client.menu_items_execute("Nyamu/Definitely Not A Real Menu Item")
    )
    assert data["success"] is False
    assert data["status"] == "not_executed"
    assert "timeout" not in data["message"].lower()


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.asyncio
async def test_real_menu_item_executes(mcp_client, unity_state_manager):
    """Assets/Refresh is idempotent, raises no dialogs, and is safe in batch mode."""
    data = _payload(await mcp_client.menu_items_execute("Assets/Refresh"))
    assert data["success"] is True
    assert data["status"] == "ok"


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.asyncio
async def test_missing_path_is_rejected(mcp_client, unity_state_manager):
    data = _payload(await mcp_client.menu_items_execute(""))
    assert data["success"] is False
    assert data["status"] == "error"
