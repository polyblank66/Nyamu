# Nyamu

**Nyamu** is a minimal MCP server for Unity, designed to give coding agents a way to check script compilation. It is based on the **[Yamu](https://github.com/keijiro/Yamu)** PoC project by [Keijiro Takahashi](https://github.com/keijiro).

Designed by a human, coded mostly by Claude Code.

## Compatibility with Coding Agents and Tools

Prompt: Check script compilation with Nyamu MCP tool

| Tool                 | Result | Tool Version               | Nyamu Version | Test Date  | Notes                                            |
|----------------------|--------|----------------------------|---------------|------------|--------------------------------------------------|
| Claude Code          | ✅      | 2.0.69                     | 0.1.0         | 2025-12-13 | Excellent compatibility, thoroughly tested       |
| Zed                  | ✅      | 0.216.1                    | 0.1.0         | 2025-12-13 |                                                  |
| Rider + AI Assistant | ✅      | 2025.3.0.4 + 253.28294.360 | 0.1.0         | 2025-12-13 |                                                  |
| Rider + Junie        | ❌      | 2025.3.0.4 + 253.549.29    | 0.1.0         | 2025-12-13 | Error in the settings for the nyamu tool         |
| Codex                | ❌      | 0.72.0                     | 0.1.0         | 2025-12-13 | Tool is not visible via the `/mcp` command       |

**For AI agents:** See [`AGENT-GUIDE.md`] for best practices, workflows, and troubleshooting.

## Features

### Script Compilation
* `compilation_trigger` – Triggers Unity Editor compilation, waits for completion, and returns compilation results, including any errors.
* `compilation_status` – Returns the current compilation status without triggering a compilation. Includes compilation state, last compile time, and any errors.

### Shader Compilation
* `compile_shader` – Compiles a single shader by name with fuzzy matching support. Searches for shaders by partial name, handles case-insensitive matching, and returns detailed compilation results with error reporting.
* `compile_all_shaders` – Compiles all shaders in the Unity project and returns a comprehensive summary with statistics (total, successful, failed), individual shader results, and detailed error information for failed shaders.
* `compile_shaders_regex` – Compiles shaders matching a regex pattern applied to shader file paths. Returns per-shader results with errors/warnings. Useful for compiling a subset of shaders based on path patterns.
* `shader_compilation_status` – Returns the current shader compilation status without triggering compilation. Includes compilation state, last compilation type (single/all/regex), last compilation time, and complete results from the previous shader compilation command.

### Testing
* `tests_run_regex` – Executes Unity Test Runner tests (EditMode and PlayMode) with regex-based filtering. Supports flexible pattern matching for test selection.
* `tests_run_all` – Runs all Unity tests in the specified mode (EditMode or PlayMode).
* `tests_run_single` – Runs a single specific Unity test by its full name.
* `tests_status` – Returns the current test execution status without running tests. Includes execution state, last test time, test results, and test run ID.
* `tests_cancel` – Cancels running Unity test execution. Supports cancellation of EditMode tests by GUID or current test run.

### Asset Management
* `assets_refresh` – Forces Unity to refresh the asset database and returns compilation error information. Shows the last compilation status even if no new compilation occurred during the refresh. This is critical after file operations to ensure Unity detects file system changes (new, deleted, or moved files). Use this single command to both refresh assets and check compilation status.

### Editor Status
* `editor_status` – Returns the current Unity Editor status, including compilation state, test execution state, and play mode state, for real-time monitoring.

### Menu Item Execution
* `execute_menu_item` – Executes any Unity Editor menu item by its path. Useful for automating Unity Editor operations programmatically.

### Unity Editor Logs
* `editor_log_path` – Returns the platform-specific path to the Unity Editor log file along with existence status. Useful for verifying log file location before reading.
* `editor_log_head` – Reads the first N lines from the Unity Editor log file. Supports filtering by log type (error, warning, info) to quickly find specific issues during startup.
* `editor_log_tail` – Reads the last N lines from the Unity Editor log file. Supports filtering by log type to quickly isolate recent errors or warnings.
* `editor_log_grep` – Searches Unity Editor log for lines matching a regex pattern. Returns matching lines with optional context lines. Supports case-sensitive/insensitive search and filtering by log type.

## Installation

### 0. Prerequisites

* **Platform**: Windows (the only tested platform)
* **Unity**: 2021.3.45f2 or later (not a hard requirement—this is simply the version used for testing)
* **Node.js**: Required to run the intermediate server

### 1. Install the Package via Unity Package Manager (Git URL)

1. Open the Unity Editor
2. Open **Window → Package Manager**
3. Click the **+** button in the top-left corner
4. Select **Add package from git URL...**
5. Enter the following URL:

   ```
   https://github.com/polyblank66/Nyamu.git?path=/Packages/dev.polyblank.nyamu
   ```
6. Click **Add**

Unity will install the Nyamu package directly from the GitHub repository.

### 2. Add the MCP Server to the AI Agent

When you first open your Unity project with Nyamu installed, it automatically generates a `.nyamu/nyamu.bat` file. This bat file launches the MCP server with the correct configuration.

**Option 1: Manual Setup (Recommended)**

Add the bat file path to your MCP settings. For **Claude Code** (`.mcp.json`):

```json
{
  "mcpServers": {
    "Nyamu": {
      "command": "cmd.exe",
      "args": ["/c", "D:\\code\\YourProject\\.nyamu\\nyamu.bat"],
      "timeout": 30000
    }
  }
}
```

Replace `D:\\code\\YourProject` with your actual project path.

**Option 2: Let the AI Agent Configure Itself**

Ask your AI agent to follow the setup instructions. For example, if you're using Gemini CLI:

```
You're Gemini CLI. Follow nyamu-mcp-setup.md
```

The `"You're ---"` statement is important, as some AI agents do not know what they are unless explicitly told.

For detailed setup instructions for different AI agents, see [`nyamu-mcp-setup.md`].

**Note**: The bat file automatically updates when you change Nyamu settings (like the server port), so you typically only need to configure this once.

## Configuration

### Response Character Limits

Nyamu provides configurable character limits for MCP server responses to prevent overwhelming AI agents with excessively long outputs. This is particularly useful when dealing with large compilation errors or test results.

Configuration is stored in `.nyamu/NyamuSettings.json` and can be edited manually or through Unity's Project Settings UI.

**Configuration Location**: Unity → Project Settings → **Nyamu MCP Server**

**Settings**:

* **Response Character Limit** – Maximum number of characters in a complete MCP response (default: 25,000)
* **Enable Truncation** – When enabled, responses exceeding the limit are truncated
* **Truncation Message** – Message appended to indicate that the content was cut off

The system automatically calculates the available space for response content by subtracting MCP JSON overhead and truncation message length from the configured limit, ensuring maximum space for meaningful output.

### Server Port Configuration

Nyamu’s HTTP server port is configurable, allowing multiple Unity Editor instances to run simultaneously without conflicts.

**Configuration Location**: Unity → Project Settings → **Nyamu MCP Server**

**Setting**:

* **Server Port** – HTTP server port (default: 17932)

#### Working with Multiple Unity Projects

To work with multiple Unity Editor instances at the same time:

1. **Configure unique ports** for each Unity project:

    * Project A: Unity → Project Settings → Nyamu MCP Server → Server Port: `17932`
    * Project B: Unity → Project Settings → Nyamu MCP Server → Server Port: `17933`

2. Each project will automatically generate its own bat file with the correct port. Configure your AI agent with multiple MCP entries:

   ```json
   {
     "mcpServers": {
       "Nyamu-ProjectA": {
         "command": "cmd.exe",
         "args": ["/c", "D:\\code\\ProjectA\\.nyamu\\nyamu.bat"],
         "timeout": 30000
       },
       "Nyamu-ProjectB": {
         "command": "cmd.exe",
         "args": ["/c", "D:\\code\\ProjectB\\.nyamu\\nyamu.bat"],
         "timeout": 30000
       }
     }
   }
   ```

3. **Launch all Unity Editors** — each instance will listen on its configured port.

Each AI agent session can now interact with its corresponding Unity Editor independently, enabling parallel development across multiple Unity projects.

**Important**: Port conflicts will prevent `NyamuServer` from starting. Make sure each Unity Editor uses a unique port number.

**Note**: The bat files automatically include the `--port` parameter from each project's settings, so you don't need to specify it manually in the MCP configuration.

## Documentation

- **[`AGENT-GUIDE.md`]** - Best practices and workflows for AI coding agents
- **[`nyamu-mcp-setup.md`]** - Setup instructions for different AI tools
- **[`NyamuServer-API-Guide.md`]** - HTTP API reference documentation

[`AGENT-GUIDE.md`]: Packages/dev.polyblank.nyamu/AGENT-GUIDE.md
[`nyamu-mcp-setup.md`]: Packages/dev.polyblank.nyamu/nyamu-mcp-setup.md
[`NyamuServer-API-Guide.md`]: Packages/dev.polyblank.nyamu/NyamuServer-API-Guide.md

## Similar Projects

- [MCP for Unity](https://github.com/CoplayDev/unity-mcp) by [Coplay](https://github.com/CoplayDev) — a package with many tools for asset manipulation. Code change verification is limited and works only when changes are made through MCP code-manipulation tools. Verification is performed by running the Roslyn Compiler on the modified files only.

- [MCP Unity](https://github.com/CoderGamester/mcp-unity) by [Miguel Tomas](https://github.com/CoderGamester) — another feature-rich package. Recent versions include a `recompile_scripts` tool for code verification, which relies on the Unity Editor compiler. When code changes involve structural modifications (such as adding or deleting files), an Asset Database Refresh is required. With this package, this can be triggered via a menu item called from the coding agent, but the user must configure the agent to do so.
