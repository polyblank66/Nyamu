Below is the **practical way to use a `.bat` file as an MCP server entry for Claude Code** (on Windows).

Claude Code doesn’t care that it’s a `.bat` file — it only needs a **command it can execute that launches a valid MCP server process**.

---

## 1. What Claude Code expects

Claude Code MCP config expects something like:

```json
{
  "command": "...",
  "args": ["..."]
}
```

Your `.bat` file must:

* Start a long-running MCP server
* Communicate via **stdio** (not a GUI, not detached)
* NOT exit immediately

---

## 2. Example `.bat` file

Let’s say your MCP server is actually a Node.js script.

### `start-mcp.bat`

```bat
@echo off
cd /d C:\path\to\your\mcp-server
node index.js
```

Important:

* `@echo off` → avoids polluting stdout
* `cd /d` → ensures correct working directory
* Do **not** use `start` (it detaches the process ❌)

---

## 3. Claude Code MCP configuration

Edit (or create):

```
%USERPROFILE%\.claude\mcp_servers.json
```

### Example config using `.bat`

```json
{
  "my-mcp-server": {
    "command": "cmd.exe",
    "args": ["/c", "C:\\path\\to\\start-mcp.bat"]
  }
}
```

✅ This works because:

* `cmd.exe /c` executes the batch file
* MCP server inherits stdio properly

---

## 4. If your `.bat` needs arguments

### `start-mcp.bat`

```bat
@echo off
node index.js --mode=%1 --port=%2
```

### MCP config

```json
{
  "my-mcp-server": {
    "command": "cmd.exe",
    "args": ["/c", "C:\\path\\start-mcp.bat", "dev", "3000"]
  }
}
```

---

## 5. Common mistakes (very important)

❌ **Using `start`**

```bat
start node index.js
```

→ Claude will lose stdio and MCP will fail

❌ **Printing extra output**

```bat
echo Starting server...
```

→ Breaks MCP protocol

❌ **Script exits immediately**
→ MCP server must stay alive

---

## 6. Debugging tips

Run the exact command Claude uses:

```cmd
cmd.exe /c C:\path\to\start-mcp.bat
```

If it:

* Exits → MCP won’t work
* Prints random text → MCP will break
* Shows errors → fix them first

You can also temporarily add logging to a file:

```bat
node index.js > mcp.log 2>&1
```

---

## 7. Alternative (cleaner): call runtime directly

If possible, **skip `.bat` entirely**:

```json
{
  "my-mcp-server": {
    "command": "node",
    "args": ["C:\\path\\to\\index.js"]
  }
}
```

This is more reliable, but `.bat` is fine when needed.

---

## Summary

✔ `.bat` **can** be used as an MCP server
✔ Use `cmd.exe /c your.bat`
✔ No `start`, no extra output, must stay running
✔ StdIO must be clean

If you want, tell me:

* What language your MCP server is in
* Whether it’s stdio or HTTP-based
* Your error message (if any)

I can tailor the `.bat` and config exactly for your setup.
