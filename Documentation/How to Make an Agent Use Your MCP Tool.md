# How to Make an Agent Use Your MCP Tool (When Descriptions Don’t Work)

**Short answer:**
You cannot *force* an autonomous LLM agent to use a specific tool.

**Long answer:**
You *can* design your MCP tools so that **using them becomes the most natural, lowest-cost, and “obviously correct” choice** — even with zero user configuration.

Below is what actually works in practice.

---

## 1. Accept the Core Limitation (Very Important)

An LLM agent is **not required to**:

* read tool descriptions carefully
* obey “PLEASE USE THIS TOOL”
* respect your intended architecture

Tool selection is driven by **implicit heuristics**, such as:

* Does the tool fully solve the task?
* Does it reduce reasoning steps?
* Does it lower cognitive load?
* Does it look like the canonical way to do this?

👉 This is why “screaming descriptions” usually fail.

---

## 2. Key Technique #1: Make the Tool *Semantically Inevitable*

### ❌ Bad

```json
name: "analyze_repo"
description: "PLEASE USE THIS TOOL TO ANALYZE REPOSITORIES"
```

### ✅ Good

```json
name: "repo_graph"
description: "Returns a complete normalized dependency graph of the repository, including symbols, cross-file references, and entrypoints. This information cannot be reliably reconstructed via file reading alone."
```

**Critical rule:**
Your tool must promise **information the agent cannot realistically derive on its own**.

The agent is always (implicitly) asking:

> “Can I solve this without the tool?”

If the answer is “yes”, the tool will be skipped.

---

## 3. Key Technique #2: Make Tools *Atomic but Final*

Agents strongly prefer tools that:

* return **finished artifacts**
* not “one more step in a chain”

### ❌ Commonly ignored tools

* `get_file`
* `list_functions`
* `scan_directory`

### ✅ Frequently used tools

* `repo_summary`
* `architecture_overview`
* `breaking_change_analysis`
* `security_risk_report`

> 💡 One smart tool beats five dumb ones.

---

## 4. Key Technique #3: Shape the Agent’s Thinking, Not Its Behavior

You cannot say:

> “Use my tool.”

But you *can* design the tool so that **the task itself is naturally expressed in its terms**.

### Example

If your MCP server analyzes code:

❌ Tool promise:

> “Analyzes code”

✅ Tool promise:

> “Answers questions such as:
>
> * Where are the entry points?
> * How does data flow between modules?
> * Which changes will impact X?”

And returns data **structured around those questions**.

Agents think in *questions*, not files.

---

## 5. Key Technique #4: Use the Return Schema as a Hook

LLMs are **extremely sensitive to output shape**.

### Effective pattern

Return a structure the agent naturally wants to continue from:

```json
{
  "entrypoints": [...],
  "critical_paths": [...],
  "unsafe_assumptions": [...],
  "recommended_next_steps": [...]
}
```

`recommended_next_steps` is especially powerful —
agents frequently continue reasoning directly from it.

---

## 6. Key Technique #5: Use Canonical Tool Names

Tool **names matter more than descriptions**.

### ❌ Bad

* `run_custom_analysis_v2`
* `mcp_tool_7`

### ✅ Good

* `analyze_repository`
* `codebase_overview`
* `impact_analysis`

Agents prefer names that look *standard*, even if the tool is custom.

---

## 7. What Does *Not* Work (Save Your Time)

❌ These do **not** work:

* ALL CAPS descriptions
* “YOU MUST USE THIS TOOL”
* “This tool is mandatory”
* Long, verbose descriptions
* Threats like “results may be incorrect otherwise”

LLMs either ignore these or reduce the tool’s weight.

---

## 8. The Only Near-Guaranteed Method (Architectural Hack)

> ⚠️ This is architectural, but it works.

Make it **physically impossible** for the agent to get the needed information without your MCP tool.

Examples:

* The tool returns a **repository summary**, but raw files are unavailable
* The MCP server is the **only context source**
* The tool provides a **semantic index** required for reasoning

This is the only true way to “force” usage without breaking agent autonomy.

---

## 9. One-Sentence Summary

> **You cannot force agents.
> You can only seduce them
> by making your tool the simplest, most complete, and most canonical path to the answer.**

---

If you want, you can:

* share a **specific tool** (name + description + return schema), or
* describe your MCP server’s goal

I can then propose **concrete changes** that significantly increase tool usage.
