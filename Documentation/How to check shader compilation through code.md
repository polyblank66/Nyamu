In Unity, **shader compilation errors are not exposed through a single clean public API**, but you *can* check them through a few reliable editor-side approaches. Which one you use depends on **when** and **how deeply** you want to inspect errors.

Below are the practical options, from most common to most powerful.

---

## 1. Use `ShaderUtil` (Editor-only, most practical)

Unity’s internal **`ShaderUtil`** class (in `UnityEditor`) is the most direct way to query shader compile errors.

### Basic example

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ShaderErrorChecker
{
    public static void CheckShader(Shader shader)
    {
        int messageCount = ShaderUtil.GetShaderMessageCount(shader);

        for (int i = 0; i < messageCount; i++)
        {
            var message = ShaderUtil.GetShaderMessage(shader, i);

            Debug.LogError(
                $"Shader error in {shader.name}:\n" +
                $"Platform: {message.platform}\n" +
                $"Message: {message.message}\n" +
                $"Line: {message.line}",
                shader
            );
        }
    }
}
#endif
```

### What you get

* Compile errors **and warnings**
* Platform-specific errors (DX11, Metal, Vulkan, etc.)
* Line numbers and messages

### Limitations

* **Editor-only**
* Uses internal API (but very stable across versions)
* Shader must already be imported/compiled

---

## 2. Force shader compilation before checking

Unity compiles shaders lazily. To ensure errors appear, you may need to **force compilation**.

### Option A: Warm up shaders

```csharp
Shader.WarmupAllShaders();
```

Then query errors via `ShaderUtil`.

### Option B: Reimport shader

```csharp
AssetDatabase.ImportAsset(
    AssetDatabase.GetAssetPath(shader),
    ImportAssetOptions.ForceUpdate
);
```

This triggers a fresh compile.

---

## 3. Capture errors via `Application.logMessageReceived`

Shader compile errors are logged to the Unity console. You can intercept them.

```csharp
#if UNITY_EDITOR
using UnityEngine;

[InitializeOnLoad]
public static class ShaderLogListener
{
    static ShaderLogListener()
    {
        Application.logMessageReceived += OnLog;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error && condition.Contains("Shader error"))
        {
            Debug.Log($"Captured shader error:\n{condition}");
        }
    }
}
#endif
```

### Pros

* Works automatically during imports and recompiles
* No internal APIs

### Cons

* String parsing
* Harder to associate errors with a specific shader asset

---

## 4. Detect shader compile failure programmatically

For a quick **“is this shader broken?”** check:

```csharp
bool hasErrors = ShaderUtil.ShaderHasError(shader);
```

Or:

```csharp
bool hasErrors = ShaderUtil.GetShaderMessageCount(shader) > 0;
```

This is useful for CI or validation tools.

---

## 5. Platform-specific compilation checks

You can inspect which platforms failed:

```csharp
var platforms = ShaderUtil.GetShaderPlatforms(shader);

foreach (var platform in platforms)
{
    var errors = ShaderUtil.GetShaderMessages(shader, platform);
    // Analyze errors per platform
}
```

This is valuable when dealing with:

* Mobile vs desktop differences
* Metal/Vulkan-only failures
* CI build validation

---

## 6. When you need *runtime* checks (important limitation)

At **runtime (player builds)**:

* Shader compile errors are **not accessible**
* Unity strips editor diagnostics
* You can only detect symptoms (pink material, missing pass)

**Best practice**:

* Validate shaders in the **Editor or CI**
* Fail the build if shader errors are detected

---

## Recommended approach (summary)

| Use case           | Best method                        |
| ------------------ | ---------------------------------- |
| Editor tooling     | `ShaderUtil.GetShaderMessageCount` |
| CI validation      | Force reimport + `ShaderUtil`      |
| Passive monitoring | `Application.logMessageReceived`   |
| Runtime detection  | Not supported (by design)          |

---

If you want, I can also:

* Show how to **scan all shaders in a project**
* Add **CI-friendly validation**
* Integrate this into an **MCP/automation workflow**
* Show how Unity reports **compute shader** errors separately
