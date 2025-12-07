# Yamu ’ Nyamu Rename: Remaining Steps

## Status: 7/9 Phases Complete 

All code changes are complete! Just need Unity validation and final merge.

##  Completed (Phases 1-7)

- [x] Pre-flight checks (branch, backup tag)
- [x] C# code (namespaces, classes, file renames)
- [x] Unity package structure (directory, package.json, asmdef)
- [x] MCP configuration (.mcp.json, settings, Node.js server)
- [x] Python test files (all test references updated)
- [x] Documentation (README, Design, CLAUDE, guidelines)

**7 commits created:**
```
d7906ff Rename meta file for nyamu-mcp-setup.md #AI
afc1cf0 Update documentation and metadata for Nyamu rename #AI
d3a78c1 Update Python MCP tests for Nyamu rename #AI
8684213 Update MCP configuration: Yamu ’ Nyamu server and tool names #AI
77dc013 Rename package directory: jp.keijiro.yamu ’ dev.polyblank.nyamu #AI
47f3a45 Update package.json and asmdef for rename #AI
b8e9efe Rename C# classes and namespaces: Yamu ’ Nyamu #AI
```

## =Ë TODO: Remaining Steps

### Step 1: Restart Claude Code CLI  

**IMPORTANT:** The MCP server name changed from `Yamu` to `Nyamu`. You MUST restart the Claude Code CLI for the new configuration to load.

After restart, the MCP tools will be:
- `mcp__Nyamu__compile_and_wait` (was `mcp__Yamu__compile_and_wait`)
- `mcp__Nyamu__refresh_assets`
- `mcp__Nyamu__run_tests`
- `mcp__Nyamu__editor_status`
- `mcp__Nyamu__compile_status`
- `mcp__Nyamu__test_status`
- `mcp__Nyamu__tests_cancel`

### Step 2: Open Unity Editor

1. Open Unity Hub
2. Open the project from `D:\code\Yamu`
3. Wait for asset import to complete (Unity will detect package rename)
4. Monitor Console for errors

**Expected behavior:**
- Unity will reimport all assets under new package name
- `packages-lock.json` will be regenerated
- Compilation should succeed with no errors

### Step 3: Validate Compilation (via Claude Code)

After Unity Editor opens and compiles:

```
mcp__Nyamu__compile_status
```

Should show:
- `"status": "idle"`
- `"isCompiling": false`
- `"errors": []` (empty array)

### Step 4: Run Unity Tests

Run both EditMode and PlayMode tests:

```
mcp__Nyamu__run_tests test_mode=EditMode timeout=60
mcp__Nyamu__run_tests test_mode=PlayMode timeout=120
```

**Expected results:**
- Tests discover `NyamuTests` class (not `YamuTests`)
- Tests discover `Nyamu.Tests` namespace
- Known passes still pass, known failures still fail

### Step 5: Run Python MCP Tests

```bash
cd D:\code\Yamu\McpTests
python -m pytest -v
```

**Expected:** All tests pass with new `NyamuServer` name

### Step 6: Final Search Verification

Verify no "Yamu" references remain in functional code:

```bash
git grep "Yamu" -- '*.cs' '*.json' '*.md' '*.js' '*.py' '*.yml'
```

Should only find:
- Git commit messages (historical)
- Possibly comments like "formerly Yamu"
- No functional code references

### Step 7: Merge to Main

```bash
git checkout main
git merge rename-yamu-to-nyamu --no-ff
git tag "v2.0.0-nyamu-rename"
git push origin main
git push origin --tags
```

## =' Rollback (If Needed)

If something goes wrong:

```bash
# Close Unity Editor first!
git checkout main
git branch -D rename-yamu-to-nyamu
git tag -d pre-rename-backup  # Optional: keep the tag
# Delete Library/ folder
# Restart from beginning
```

## =Ê Files Changed Summary

- **C# files:** 5 (3 core classes + 2 test files)
- **Unity assets:** 2 (.asset + .meta)
- **Package configs:** 3 (package.json, .asmdef, packages-lock.json)
- **MCP configs:** 3 (.mcp.json, .gemini/settings.json, .claude/settings.local.json)
- **Node.js:** 1 (mcp-server.js)
- **Python tests:** 7 files
- **Documentation:** 5 files (.md files)
- **Workflow:** 1 (.github/workflows/main.yml)
- **Directory renames:** 1 (package directory)

**Total:** ~80+ files affected

##  Success Criteria

- [ ] All C# files compile without errors
- [ ] All Unity tests pass (EditMode and PlayMode)
- [ ] All Python MCP tests pass
- [ ] All 7 MCP tools respond correctly
- [ ] No "Yamu" references in functional code
- [ ] Documentation updated
- [ ] Git history preserved (renames tracked)
- [ ] Changes merged to main with tag
