"""
Integration tests for code_execute / code_execute_status
"""

import asyncio
import json
import time

import pytest


def _payload(response):
    return json.loads(response["result"]["content"][0]["text"])


async def _poll_execution(client, execution_id, timeout=30.0, interval=0.5):
    """Poll code_execute_status until the execution reports isDone."""
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        last = _payload(await client.code_execute_status(execution_id))
        if last.get("isDone"):
            return last
        await asyncio.sleep(interval)
    raise AssertionError(f"code_execute_status never reported isDone; last={last}")


@pytest.mark.mcp
@pytest.mark.protocol
@pytest.mark.essential
@pytest.mark.asyncio
async def test_code_execute_tools_are_advertised(mcp_client, unity_state_manager):
    tools = (await mcp_client.list_tools())["result"]["tools"]
    names = {t["name"] for t in tools}
    assert "code_execute" in names
    assert "code_execute_status" in names

    code_execute = next(t for t in tools if t["name"] == "code_execute")
    assert "code" in code_execute["inputSchema"]["required"]


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.asyncio
async def test_expression_happy_path(mcp_client, unity_state_manager):
    status = _payload(await mcp_client.code_execute("1 + 1"))
    assert status["outcome"] == "success"
    assert status["result"] == "2"
    assert "Int32" in status["resultType"]
    assert status["resolvedMode"] == "expression"


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.asyncio
async def test_statements_touch_unity_main_thread(mcp_client, unity_state_manager):
    """Proves the main-thread marshalling path: a GameObject can only be
    created/destroyed from Unity's main thread."""
    code = (
        'var go = new UnityEngine.GameObject("NyamuCodeExecuteTmp");\n'
        'var n = go.name;\n'
        'UnityEngine.Object.DestroyImmediate(go);\n'
        "return n;"
    )
    status = _payload(await mcp_client.code_execute(code))
    assert status["outcome"] == "success"
    assert status["result"] == '"NyamuCodeExecuteTmp"'


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.asyncio
async def test_compile_error_reports_user_line_numbers(mcp_client, unity_state_manager):
    """Locks the #line-directive mapping: the reported error line must match
    the user's own source, not the position inside the generated wrapper."""
    code = "\n\nthis is not csharp;\n"
    status = _payload(await mcp_client.code_execute(code))
    assert status["outcome"] == "compile_error"
    assert len(status["errors"]) > 0
    assert status["errors"][0]["line"] == 3
    assert status["errors"][0]["file"].endswith("NyamuUserCode.cs")


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.asyncio
async def test_runtime_exception_is_reported(mcp_client, unity_state_manager):
    status = _payload(await mcp_client.code_execute(
        'throw new System.InvalidOperationException("boom");'
    ))
    assert status["outcome"] == "runtime_exception"
    assert status["exceptionType"].endswith("InvalidOperationException")
    assert "boom" in status["exceptionMessage"]
    assert "NyamuGenerated" not in status["stackTrace"]


@pytest.mark.mcp
@pytest.mark.essential
@pytest.mark.asyncio
async def test_debug_log_is_captured(mcp_client, unity_state_manager):
    status = _payload(await mcp_client.code_execute(
        'UnityEngine.Debug.Log("nyamu-test-marker"); return null;'
    ))
    assert status["outcome"] == "success"
    assert "nyamu-test-marker" in status["logs"]


@pytest.mark.mcp
@pytest.mark.asyncio
async def test_worker_thread_reflection(mcp_client, unity_state_manager):
    """run_on_main_thread=false must still work for pure reflection/computation
    that never touches a UnityEngine/UnityEditor API."""
    status = _payload(await mcp_client.code_execute(
        "1 + 2 + 3", run_on_main_thread=False
    ))
    assert status["outcome"] == "success"
    assert status["result"] == "6"


@pytest.mark.mcp
@pytest.mark.asyncio
async def test_class_mode_with_entry_point(mcp_client, unity_state_manager):
    code = (
        "namespace NyamuTestGenerated { public static class Foo { "
        "public static object Execute() { return 42; } } }"
    )
    status = _payload(await mcp_client.code_execute(code, mode="class"))
    assert status["outcome"] == "success"
    assert status["resolvedMode"] == "class"
    assert status["result"] == "42"


@pytest.mark.mcp
@pytest.mark.asyncio
async def test_background_returns_id_then_status(mcp_client, unity_state_manager):
    start = _payload(await mcp_client.code_execute("1 + 1", background=True))
    assert start["executionId"]
    assert start["phase"] in ("queued", "compiling", "compiled", "executing")

    final = await _poll_execution(mcp_client, start["executionId"])
    assert final["outcome"] == "success"
    assert final["result"] == "2"


@pytest.mark.mcp
@pytest.mark.asyncio
async def test_globals_persist_across_calls(mcp_client, unity_state_manager):
    set_status = _payload(await mcp_client.code_execute(
        'NyamuGlobals.Set("nyamu_test_key", 7); return null;'
    ))
    assert set_status["outcome"] == "success"

    get_status = _payload(await mcp_client.code_execute(
        'return NyamuGlobals.Get<int>("nyamu_test_key");'
    ))
    assert get_status["outcome"] == "success"
    assert get_status["result"] == "7"


@pytest.mark.mcp
@pytest.mark.slow
@pytest.mark.asyncio
async def test_timeout_path_is_honest_and_recovers(mcp_client, unity_state_manager):
    """A blocking snippet freezes the Editor for its own duration - this is
    documented, expected behaviour, not a bug. The MCP-level timeout must
    still return an honest status, and the Editor must recover afterwards."""
    start_response = await mcp_client.code_execute(
        "System.Threading.Thread.Sleep(8000); return 1;", timeout=2
    )
    text = start_response["result"]["content"][0]["text"]
    assert "executing" in text.lower() or "phase" in text.lower()

    # Give the blocking snippet time to actually finish before checking recovery.
    await asyncio.sleep(8)
    status = _payload(await mcp_client.editor_status())
    assert "isCompiling" in status
