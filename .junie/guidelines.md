Nyamu development guidelines (project-specific)

Audience: Senior engineers working on this Unity + MCP (Model Context Protocol) integrated project. This is not a generic Unity primer; it captures the particulars of this repo so you can be productive fast and avoid known foot?guns.

1) Build and configuration

Unity/Editor
- Unity version: 2021.3.45f2. Open the project (D:\code\Nyamu) in this exact editor version to avoid API/asmdif deltas.
- Normal Unity build pipeline applies; no custom post?processors are required for just compiling scripts and running tests.

Nyamu MCP server (in?Editor HTTP + MCP bridge)
- The in?Editor HTTP server is implemented by Packages\jp.keijiro.Nyamu\Editor\NyamuServer.cs.
  - Port: 17932 (Constants.ServerPort). Key endpoints:
    - GET /compile-status > current compile/test state JSON
    - GET /compile-and-wait > triggers compile and waits; returns summary (succeeds even if no changes)
    - GET /run-tests?mode=EditMode|PlayMode&filter=… > starts Unity Test Runner
    - GET /test-status > current test run status/results
    - POST /refresh-assets with JSON { force: bool } > asset database refresh (force=true after deletions)
- During domain reloads/compilation/asset refresh the HTTP server restarts; external calls can fail transiently. This is expected.
  - If you’re calling tools programmatically, implement retries with a small backoff (2–5 s) for HTTP failures. See Packages\jp.keijiro.Nyamu\Nyamu-mcp-setup.md for the canonical blurb about handling “MCP Error -32603: HTTP request failed”.

Agent/CLI MCP registration
- The Node-side MCP bridge is Packages\jp.keijiro.Nyamu\Node\mcp-server.js which proxies to the Unity HTTP server at http://localhost:17932.
- To use from an AI agent that supports MCP, add an entry similar to:
  {
    "mcpServers": {
      "Nyamu": {
        "command": "node",
        "args": ["Library/PackageCache/jp.keijiro.Nyamu@(HASH)/Node/mcp-server.js"]
      }
    }
  }
  Notes:
  - Replace (HASH) with the actual package cache hash for this package version (Unity controls this).
  - For Claude Code the file is typically .mcp.json at the project root; for Gemini CLI it is .gemini/settings.json. Update the args path when the hash changes.

Local Python tooling (optional but useful)
- McpTests contains a thin client (McpTests/mcp_client.py) to call the MCP server from Python.
- Requirements are listed in McpTests/README.md (pip install -r requirements.txt).

2) Testing: how this project’s tests work

Test stacks present
- Unity Test Runner (EditMode/PlayMode) run by Nyamu HTTP endpoints and by MCP.
- Python-based integration tests in McpTests/ exercising both the raw HTTP endpoints and the MCP tools (compile_and_wait, refresh_assets, run_tests).

Unity Test Runner via MCP
- From Python you can do:
  from McpTests.mcp_client import MCPClient
  async with MCPClient() as client:
      await client.refresh_assets(force=False)
      compile_result = await client.compile_and_wait(timeout=30)
      test_result = await client.run_tests(test_mode="PlayMode", test_filter="", timeout=60)
- Important:
  - After deleting files, you MUST call refresh_assets(force=true) before compiling to avoid stale references/CS2001. After creating new files, refresh_assets(force=false) is sufficient. Pure content edits don’t require a refresh.
  - Nyamu server may restart while compiling/refreshing; re?try on transient HTTP errors.

Python pytest suite (McpTests)
- Location: D:\code\Nyamu\McpTests
- Prereqs:
  - Unity Editor must be running with this project open. The autouse fixture in McpTests/conftest.py performs a health check against http://localhost:17932/compile-status and will skip tests if unavailable.
  - Python 3.7+ and the dependencies from requirements.txt.
- Typical flows:
  - Run everything:  pytest
  - Focus categories:  pytest -m essential | protocol | structural | mcp | compilation | asmdef | slow
  - Randomize order:   pytest --random-order  [--random-order-seed=12345]
  - Verbose:           pytest -v
- Adding new tests:
  - Prefer small, focused tests using provided fixtures:
    - mcp_client: async MCP client session
    - unity_state_manager: smart cleanup (noop|minimal|full) selected by markers
    - unity_helper: creates/restores temp files and runs refreshes when needed
    - temp_files: tracks and cleans temp files and their .meta counterparts
  - Mark tests appropriately so the cleanup tier is correct and the suite stays fast:
    - @pytest.mark.protocol > no Unity state changes; fastest
    - @pytest.mark.structural > modifies Assets/ or assembly structure; full cleanup
    - @pytest.mark.compilation / @pytest.mark.mcp / @pytest.mark.asmdef as needed
  - File ops rules that matter:
    - Create file > write file > refresh_assets(force=false) > compile_and_wait
    - Delete file > delete file + its .meta > refresh_assets(force=true) > compile_and_wait
    - Modify contents only > compile_and_wait is enough (no refresh)
- Verified example run:
  - A simple smoke test was executed locally with pytest and passed, confirming the Python test harness is wired correctly in this environment. Note that real tests will be skipped if the Unity HTTP server isn’t running, by design of the autouse health check.

Unity tests directly from HTTP
- You can also cURL the in?Editor server:
  - curl http://localhost:17932/compile-status
  - curl http://localhost:17932/compile-and-wait
  - curl "http://localhost:17932/run-tests?mode=EditMode"
  - curl http://localhost:17932/test-status
  - Use POST /refresh-assets with JSON body { "force": true|false }

3) Additional development notes

Project layout quick map
- Assets/Tests: Unity test scripts
- Assets/TestModule: Sample assembly/asmdef used by tests
- Packages/jp.keijiro.Nyamu: Editor server (C#), Node MCP bridge, and docs
- McpTests/: Python integration tests and helpers

Editor server internals worth knowing (NyamuServer.cs)
- Exposes endpoints listed above; serializes compile/test state, streams results after completion.
- Handles enter?play?mode options: the TestCallbacks class snapshots and restores play mode options across test runs to avoid leaking settings.
- Uses a background thread (HttpRequestProcessor) and restarts its HttpListener around compilation/refresh transitions; this is the source of transient connection failures.

Common pitfalls and how to avoid
- Transient HTTP errors during compile/refresh/test start are expected. Implement retries with backoff.
- Always refresh after file deletions (force=true) to prevent CS2001 and dangling GUID references in the asset DB. The Python helpers already bake this in.
- When touching files under Assets/, also consider .meta files: delete them alongside the file or the asset DB will retain entries.
- When the package hash changes (Library/PackageCache/jp.keijiro.Nyamu@(HASH)), update any agent/CLI registrations that hardcode that path.

Debugging tips
- Check the Unity console for errors during compile/test; correlate with HTTP endpoints:
  - GET /compile-status to see if the server believes compilation is running/idle
  - GET /test-status to see last/active test run progress
- From Python tests, increase verbosity or print [DEBUG_LOG] lines for diagnostics.
- If MCP calls appear stuck right after a domain reload, wait a couple seconds and retry; the server is reinitializing.

Code style/formatting guidelines in this repo
- C#: follow standard Unity/Rider defaults; no custom analyzers are enforced. Keep MonoBehaviour fields that you want editable marked public or [SerializeField].
- Python: black/pep8 is fine; fixtures are organized and used extensively—prefer composition over ad?hoc sleeps and direct HTTP calls.

Appendix: handy snippets
- Programmatic MCP usage from Python (see McpTests/MCP_USAGE_EXAMPLES.md for more):
  import asyncio
  from mcp_client import MCPClient
  async def main():
      async with MCPClient() as client:
          await client.refresh_assets(force=False)
          res = await client.compile_and_wait(timeout=30)
          print(res["result"]["content"][0]["text"])
  asyncio.run(main())

- Raw HTTP test kick:
  powershell -Command "curl http://localhost:17932/compile-and-wait"

That’s it—this captures the non?obvious workflow details unique to this project. Keep this file updated as the server’s endpoints/behavior evolves.
