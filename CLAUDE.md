Check out Design.md for the project design and goal definitions.

# About MCP

- This project uses the Nyamu MCP server, which enables triggering compilations
  and retrieving compilation errors from the Unity Editor.
- Iterate through the edit-compile-debug cycle until all errors are resolved.

## Nyamu MCP Workflow Guidelines

### File Operation Workflows
**CRITICAL**: Always call `refresh_assets` before `compilation_trigger` for structural changes:

- **Creating files**: Write → `refresh_assets(force=false)` → Wait for MCP → `compilation_trigger`
- **Deleting files**: Delete → `refresh_assets(force=true)` → Wait for MCP → `compilation_trigger`
- **Editing existing files**: Edit → `compilation_trigger` (no refresh needed)

### Error Handling
- **Error -32603 "HTTP request failed"**: Expected during Unity recompilation/refresh
  - Wait 3-5 seconds and retry
  - This is normal behavior - Unity HTTP server is restarting
- Always wait for MCP responsiveness after `refresh_assets` before calling other tools

### Testing
- Prefer `test_filter_regex` over `test_filter` for pattern matching
- EditMode: Fast, editor-only verification (use 30s timeout)
- PlayMode: Full runtime simulation (use 60-120s timeout)
- Only EditMode tests can be cancelled via `tests_cancel`

### Status Checking
- Use `compilation_status`, `test_status`, `editor_status` to check state without triggering operations
- Check status before long operations to avoid redundant work

# Technology Choices

- The project is built with Unity.
- We use the Universal Render Pipeline (URP) for rendering.
- UI Toolkit is used for building runtime user interfaces.
- IMGUI may be used for editor-only UI elements.

# Directory Structure

- Editable project source files are located in the `Assets/` directory and its
  subdirectories.
- Read-only package source files are located in `Library/PackageCache/`. **Do not
  modify** files in this directory.
- MCP integration tests are located in the `McpTests/` directory. These Python tests
  verify MCP server functionality including compilation, test execution, and response
  formatting. Run tests with `cd McpTests && python -m pytest`.

# Code Style Guidelines

- All comments must be written in English.
- Do not use documentation-style comments (`///` or `/** */`), as we do not
  generate documentation from comments.
- Use `var` for local variables whenever the type can be inferred.
- Omit the `private` access modifier when it is implicit and does not harm
  clarity.
- Omit braces for single-statement blocks (`if`, `for`, `while`, etc.).
- Use expression-bodied members whenever appropriate (for properties, methods,
  lambdas, etc.).
- Don't put trailing whitespaces.

# Workflow Instructions

- Focus primarily on writing or modifying source code.
- C# scripts can be compiled via MCP (`compilation_trigger`).
- Compilation status can be retrieved via MCP (`compilation_status`).
- If an operation requires scene editing or interaction with the Unity Editor,
  provide clear, step-by-step instructions.
- Write all Git commit messages in English.
