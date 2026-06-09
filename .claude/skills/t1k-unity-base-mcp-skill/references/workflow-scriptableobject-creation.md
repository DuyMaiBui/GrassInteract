---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Workflow: ScriptableObject Asset Creation

`manage_asset(action="create")` only supports three asset types: **Folder**, **Material**, and
**PhysicsMaterial**. For custom ScriptableObject types you must use one of the fallback paths below.

## Decision tree

```
Need a custom ScriptableObject .asset?
  ├─ Is execute_code available (non-Windows / short temp path)?
  │    └─ YES → use execute_code path (Option A)
  └─ Windows or Mono "filename too long" error?
       └─ YES → write YAML directly to disk (Option B) ← default on Windows
```

---

## Option A — execute_code (Linux / macOS, short temp paths)

```csharp
// execute_code contents
var so = ScriptableObject.CreateInstance<MyConfig>();
so.someField = "value";
UnityEditor.AssetDatabase.CreateAsset(so, "Assets/Data/MyConfig.asset");
UnityEditor.AssetDatabase.SaveAssets();
```

**Known failure on Windows:** Mono's temp compilation path can exceed 260 chars, causing:
```
IOException: The filename or extension is too long
```
Both `roslyn` and `codedom` backends are affected on Windows Unity 6. Switch to Option B.

---

## Option B — Direct YAML write (Windows-safe, always reliable)

### Step 1: Get the script GUID

```bash
# Read the GUID from the .cs.meta file
cat "Assets/Scripts/MyConfig.cs.meta"
# Find the line: guid: <32-char hex string>
```

Or via MCP:
```python
manage_asset(action="get_info", path="Assets/Scripts/MyConfig.cs")
# Returns: {"guid": "<GUID>", ...}
```

### Step 2: Write the .asset YAML

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: <GUID-FROM-META>, type: 3}
  m_Name: MyConfig
  # Add your serialized fields below, matching field names exactly:
  # someField: value
  # someInt: 0
  # someFloat: 0
```

Write via MCP `Write` tool to `Assets/Data/MyConfig.asset`.

**YAML field name rules:**
- Match the C# field name exactly (Unity serializes private `[SerializeField]` fields by name).
- Nested objects use YAML mapping blocks.
- Arrays use YAML sequences.
- Object references use `{fileID: <id>, guid: <guid>, type: <type>}` format.

### Step 3: Import the asset

```python
refresh_unity(mode="force", scope="assets", wait_for_ready=True)
# Then verify:
manage_asset(action="get_info", path="Assets/Data/MyConfig.asset")
```

---

## Gotchas

- **`manage_asset(action="create")` silently errors for custom types** — it does NOT fall back
  to a generic ScriptableObject; the call returns an error or no-op with no Unity log entry.
- **`execute_code` on Windows hits Mono temp-path limit (260 chars)** — both `roslyn` and
  `codedom` compiler backends fail with `IOException: The filename or extension is too long`.
  This is a known Unity-on-Windows limitation. Workaround: use Option B (direct YAML write).
- **YAML fileID `&11400000` is the canonical standalone asset anchor** — do not change it;
  Unity uses it to identify the root MonoBehaviour in a `.asset` file.
- **`m_Script` fileID must be `11500000`** — this is Unity's built-in fileID for MonoBehaviour
  script references. The `guid` is what uniquely identifies YOUR script class.
- **Field names are case-sensitive** — `myValue` in C# must be `myValue:` in YAML (not
  `MyValue:` or `my_value:`). Mismatches silently leave the field at its default value.
- **After writing YAML, always call `refresh_unity(scope="assets")`** — without a refresh the
  asset exists on disk but is not tracked by the AssetDatabase (get_info returns null).

## Related

- `tools-systems-code.md` § manage_scriptable_object — MCP tool for reading/modifying
  existing ScriptableObject fields at runtime (not creation)
- `workflow-script-lifecycle.md` — C# script creation and compilation lifecycle
- `error-recovery-guide.md` — general asset import failure recovery
