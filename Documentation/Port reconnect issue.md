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

### 6. Platform-specific error code gaps

**Location:** `NyamuServer.cs`, retry `catch` clause (line 187)

The exception filter checks specific error codes:

```csharp
catch (HttpListenerException ex) when (
    ex.ErrorCode == 48 ||          // macOS: EADDRINUSE
    ex.ErrorCode == 32 ||          // Linux: EPIPE (wrong code for this purpose)
    ex.Message.Contains("already in use") ||
    ex.Message.Contains("normally permitted"))
```

- Error code 48 is macOS `EADDRINUSE`.
- Error code 32 is not the correct "address in use" code on Linux (should be
  98 `EADDRINUSE`).
- On Windows, `HttpListenerException.ErrorCode` returns Win32 error codes
  (e.g., 183 `ERROR_ALREADY_EXISTS` or 5 `ERROR_ACCESS_DENIED`), not socket
  error codes.
- The `Message.Contains` fallbacks provide some coverage but depend on
  locale-specific error message text.

If the exception doesn't match any of these conditions, it falls through to
the generic `catch (Exception)` block (line 209), which does **not retry** —
it breaks immediately. This means on some platforms, a retryable port conflict
may be treated as a fatal error.

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

### For issue 1 (TIME_WAIT)

- Increase retry count and/or delay (e.g., 10 retries × 500ms = 5s).
- Use exponential backoff instead of fixed delay.
- Set `SO_REUSEADDR` on the underlying socket (if HttpListener allows it) to
  bypass TIME_WAIT.

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

### For issue 6 (platform error codes)

- Use `SocketError` enum values instead of raw integer codes (e.g.,
  `SocketError.AddressAlreadyInUse` which is 10048 on Windows, mapped
  correctly per platform by .NET).
- Or: catch all `HttpListenerException` in the retry loop regardless of
  error code, since any listener start failure is worth retrying.

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
