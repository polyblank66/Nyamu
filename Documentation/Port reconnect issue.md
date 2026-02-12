# Port Reconnect Issue

## Problem

The Nyamu MCP server occasionally fails to bind its HTTP port after a Unity
domain reload, making MCP integration unavailable until the next reload cycle.

The issue is intermittent and more likely to occur during rapid edit-compile
iterations.

## Root Cause Analysis

Seven related issues contribute to the problem.

### 1. TIME_WAIT exhaustion during domain reloads

**Location:** `NyamuServer.cs`, `Initialize()` method (lines 169-219)

During a domain reload, `Cleanup()` calls `_listener.Stop()` +
`_listener.Close()`, then `Initialize()` immediately tries to bind the same
port. The OS may keep the closed socket in TIME_WAIT state for up to 2 minutes
(TCP standard). The current retry logic only tries **3 times with 300ms
delays** (~900ms total), which is often insufficient.

```
Cleanup() → listener.Stop() + listener.Close()
   ↓
OS holds port in TIME_WAIT (up to 120s)
   ↓
Initialize() → listener.Start() on same port
   ↓
HttpListenerException after 3 retries (900ms) → server dead
```

Why it's intermittent: TIME_WAIT duration varies by OS load. Fast machines
release the port quickly. Under heavy load or with rapid domain reloads, the
retry window is more likely to miss.

### 2. Fallback to a known-occupied port

**Location:** `NyamuProjectRegistry.cs`, `FindFreePort()` method (lines 51-76)

When no free port is found in the search range (startPort to startPort+100),
the method falls back to returning `startPort` — the very first port that was
already confirmed unavailable:

```csharp
// All ports in range were checked and none are available.
// Yet the method returns startPort, which is known to be occupied.
return startPort;
```

The server then tries to bind this port and fails. A better approach would be
to return a sentinel value (e.g., -1) or throw, so the caller can handle the
"no port available" case explicitly.

### 3. No distinction between TIME_WAIT and active occupation

**Location:** `NyamuProjectRegistry.cs`, `CanBindPort()` (lines 268-286) and
`NyamuServer.cs`, retry logic (line 187)

The port check is binary — it either binds or it doesn't. There is no
distinction between:

| Scenario | Cause | Correct action |
|---|---|---|
| TIME_WAIT | Previous Nyamu server just closed | Wait and retry (seconds) |
| Active listener | Another process holds the port | Try a different port immediately |

Currently, the retry logic always waits 300ms between attempts regardless of
the reason for failure. This means:

- **TIME_WAIT:** 900ms total retry window is often not enough (OS may hold for
  seconds or minutes). The server gives up too early.
- **Active listener:** 900ms is wasted on a port that will never become free,
  when it could immediately search for an alternative port.

### 4. Race condition in port availability check (TOCTOU)

**Location:** `NyamuProjectRegistry.cs`, `CanBindPort()` (lines 268-286) and
`NyamuServer.cs`, `Initialize()` (line 180)

`CanBindPort()` tests availability by binding a `TcpListener`, then
immediately releasing it:

```csharp
var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();  // port is bound
listener.Stop();   // port is released ← gap opens here
return true;
```

Then `NyamuServer.Initialize()` creates an `HttpListener` and binds the same
port. Between `TcpListener.Stop()` and `HttpListener.Start()`, another process
can grab the port. This is a classic TOCTOU (time-of-check-time-of-use) race.

In practice this is rare on localhost, but can happen when multiple Unity
editors start simultaneously.

### 5. Registry desynchronization after crashes

**Location:** `NyamuProjectRegistry.cs`, `RegisterProjectPort()` (lines
99-137) and `LoadRegistry()` (lines 174-207)

The registry file (`~/.nyamu/NyamuProjectsRegistry.json`) maps project paths
to ports. Entries are added when a project starts but never removed when Unity
exits normally or crashes. Over time, stale entries accumulate — pointing to
ports that may now be occupied by unrelated processes.

The `IsPortInRegistryForOtherProject()` check (line 288) considers stale
entries as "occupied by another project", which can cause `FindFreePort()` to
skip ports that are actually free, narrowing the available range unnecessarily.

There is no cleanup mechanism (e.g., verifying that registered projects are
still running, or removing entries on clean shutdown).

### 6. Mono throws SocketException, not HttpListenerException

**Location:** `NyamuServer.cs`, retry `catch` clause (line 187)

**This is the confirmed primary bug.** The retry logic catches
`HttpListenerException`, but Unity uses the Mono runtime, and Mono's
`HttpListener.Start()` throws a `SocketException` (or a plain `Exception`)
when the port is occupied — not `HttpListenerException`.

Evidence from production logs:

```
[Nyamu][Server] Unexpected error starting HTTP listener: Only one usage of each socket address (protocol/network address/port) is normally permitted.
NyamuServer.cs:208  ← generic catch (Exception) block, NOT the retry block
```

The error message contains `"normally permitted"`, which the `when` clause
checks for — but the clause never runs because the exception type doesn't
match `HttpListenerException` in the first place.

The generic `catch (Exception)` block has `break`, so the server gives up
on the **very first attempt** with zero retries. Once this happens, every
subsequent domain reload repeats the same pattern:

```
Domain reload → Initialize() → Cleanup() (no-op, _listener is null)
→ try bind port → SocketException → catch (Exception) → break → dead
→ next domain reload → same thing
```

The server stays dead until Unity is fully restarted.

Additionally, the `when` filter itself has issues with error codes:

- Error code 48 is macOS `EADDRINUSE`.
- Error code 32 is `EPIPE` on Linux, not `EADDRINUSE` (should be 98).
- On Windows, `HttpListenerException.ErrorCode` returns Win32 error codes,
  not Unix errno values.

### 7. Multi-editor instance synchronization

**Location:** `NyamuProjectRegistry.cs`, `SaveRegistry()` (lines 209-245)
and `LoadRegistry()` (lines 174-207)

When multiple Unity editors start simultaneously (e.g., opening several
projects at once), they all read the registry, find the same "free" port, and
race to bind it. The registry file uses a lock object (`_registryLock`), but
this lock is process-local — it only synchronizes threads within a single
Unity editor, not across multiple editor processes.

The atomic write pattern (write to `.tmp` then rename) prevents file
corruption, but doesn't prevent the read-decide-write race between processes.
File-level locking (e.g., `FileStream` with `FileShare.None`) would be needed
to synchronize across processes.

## Possible Fixes

### For issue 1 (TIME_WAIT) — FIXED

- Increased retry window from 3 × 300ms (900ms) to 10 × 500ms (5s).
- This provides sufficient time for most TIME_WAIT states to clear.
- Future improvement: exponential backoff or SO_REUSEADDR socket option.

### For issue 2 (fallback to occupied port)

- Return -1 or throw when no free port is found.
- Or: expand the search range dynamically.

### For issue 3 (no TIME_WAIT detection)

- After retry exhaustion in `NyamuServer.Initialize()`, call
  `FindFreePort()` to try an alternative port instead of giving up.
- This combines the benefits of waiting (in case of TIME_WAIT) with fallback
  to a new port (in case of active occupation).

### For issue 4 (TOCTOU race)

- Skip the `CanBindPort()` pre-check entirely and try binding `HttpListener`
  directly. Handle failure by trying the next port.
- Or: keep the check as a fast filter but treat `HttpListener` bind failure
  as non-fatal and retry with the next port.

### For issue 5 (registry desynchronization)

- Remove the current project's entry from the registry on clean shutdown
  (in `Cleanup()`).
- On startup, validate existing entries by checking if their ports are
  actually in use. Remove stale entries whose ports are free.
- Add a timestamp or PID to entries so stale ones can be identified.

### For issue 6 (Mono SocketException) — FIXED

- Replaced the two separate catch blocks (`HttpListenerException` with
  `when` filter + generic `Exception` with `break`) with a single
  `catch (Exception)` that always retries.
- Since the only code inside the `try` is `HttpListener` creation and
  `Start()`, any exception there is port-related and worth retrying.
- This fixes the Mono `SocketException` issue and eliminates the
  platform-specific error code problem entirely.

### For issue 7 (multi-editor synchronization)

- Use file-level locking (`FileStream` with `FileShare.None`) around
  registry read-modify-write operations.
- Or: use the registry only as a hint and rely on actual port binding as
  the source of truth — if `HttpListener.Start()` fails, try the next port.

## Relevant Files

| File | Role |
|---|---|
| `Nyamu.UnityPackage/Editor/NyamuServer.cs` | HTTP server lifecycle, retry logic |
| `Nyamu.UnityPackage/Editor/Core/NyamuProjectRegistry.cs` | Port discovery, availability checks, registry |
| `Nyamu.UnityPackage/Editor/NyamuSettings.cs` | Port configuration, calls FindFreePort/RegisterProjectPort |
