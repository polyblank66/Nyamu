# MCP Server Setup Instructions for AI Agents

Add an MCP server entry to your **per-project agent settings file**.

Example:

```
{
  "mcpServers": {
    "yamu": {
      "command": "node",
      "args": ["Library/PackageCache/jp.keijiro.yamu@(HASH)/Node/mcp-server.js"]
    }
  }
}
```

`(HASH)` varies between versions, so you should locate it using a shell command.

The location of the **per-project agent settings file** depends on the AI agent
you're using. For Claude Code, the file is usually `.mcp.json` at the project
root. For Gemini CLI, the most common location is `.gemini/settings.json`.

If the file already contains a `yamu` entry in the `mcpServers` section, you
only need to update the `(HASH)` in the `args` field.

## Important: Understanding MCP Error -32603

When using Yamu MCP tools, you may encounter **MCP Error -32603** with message "Tool execution failed: HTTP request failed". **This is expected behavior**, not a bug.

### Why This Happens:
- Unity's HTTP server **automatically restarts** during script compilation
- Unity's HTTP server **restarts** during asset database refresh operations
- This prevents interference with Unity's compilation process

### How to Handle:
1. **Expect the error** - It means Unity is compiling/refreshing
2. **Wait 2-5 seconds** - Allow compilation to progress
3. **Retry the command** - The HTTP server will be available again
4. **Repeat as needed** - Until success or reasonable timeout


