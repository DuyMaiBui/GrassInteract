---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Validation Script — How to Run the Checklist via Unity MCP

Step-by-step recipe for an agent or human to audit a demo against the 12-point Playability Validation Checklist. Outputs a markdown report.

---

## Prerequisites

- Unity Editor open, DOTS-AI project loaded.
- Unity MCP bridge connected (see `unity-mcp-skill`).
- The demo's scene file exists at `Assets/Demos/RPG/{DemoName}/Scenes/{DemoName}.unity` or equivalent.

---

## Step 1 — Open scene + run setup

```
mcp__UnityMCP__manage_scene(
  action="load",
  scene_path="Assets/Demos/RPG/{DemoName}/Scenes/{DemoName}.unity")

mcp__UnityMCP__execute_menu_item(
  menu_path="Tools/{DemoName}/Setup Scene")

mcp__UnityMCP__read_console(
  action="get", types=["error", "warning"],
  filter_text="{DemoName}")
# Expect: 0 errors. 0 warnings related to the demo.
```

If the demo has a SubScene, clear the bake cache too:

```bash
rm -rf Library/EntityScenes/
```

---

## Step 2 — Capture before-screenshot in Edit mode

```
mcp__UnityMCP__manage_camera(
  action="screenshot",
  capture_source="game_view",
  include_image=true,
  max_resolution=900,
  screenshot_file_name="{DemoName}-editmode.png")
```

Visually verify:
- P0-A1 Player avatar — multi-part silhouette visible
- P0-A3 Ground + horizon — at least 3 horizontal bands

(P0-B / P0-C rows are UI Overlay which only renders in Play mode — verify after Step 3.)

---

## Step 3 — Enter Play mode + capture

```
mcp__UnityMCP__manage_editor(action="play")
# wait ~4s for systems to bootstrap

mcp__UnityMCP__manage_camera(
  action="screenshot",
  capture_source="screen",  # captures Overlay UI too
  include_image=true,
  max_resolution=900,
  screenshot_file_name="{DemoName}-playmode.png")
```

Visually verify:
- P0-B1 HP bar rendered with fill + numeric
- P0-B2 Phase text visible + phase banner overlay
- P0-B3 Currency texts visible
- P0-C1 At least one interactive button visible
- P0-A2 Enemies (spawn or fast-forward to combat phase first)
- P0-A4 Items / pickups

---

## Step 4 — Probe scene hierarchy for SerializeField wiring

For each UI MonoBehaviour, confirm all SerializeFields are assigned:

```
mcp__UnityMCP__find_gameobjects(
  search_term="{DemoName}HUD", search_method="by_name")

# Then for each match, read components:
# resource: mcpforunity://scene/gameobject/{id}/components
# verify every SerializeField property has non-null objectReferenceValue
```

Repeat for: WorkshopUI, ShopUI, EventUI, RunSummaryUI, MapUI, PhaseBanner — whichever exist.

**Failure signal:** any `objectReferenceValue: null` on a SerializeField → P0-D2 FAIL.

---

## Step 5 — Force phase transitions, check end screens

If the demo has Won/Lost end states, force them via console code injection:

```
mcp__UnityMCP__execute_code(
  code="""
    var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
    using var q = em.CreateEntityQuery(typeof(RunPhase));
    if (q.CalculateEntityCount() > 0)
    {
        em.SetComponentData(q.GetSingletonEntity(),
            new RunPhase { Value = RunPhaseValue.Won });
    }
  """)
```

Capture screenshot → confirm WonPanel renders (P0-C3 green-tinted screen with VICTORY title + New Run button).

Repeat for `Lost` → confirm LostPanel.

---

## Step 6 — Stop Play mode + write report

```
mcp__UnityMCP__manage_editor(action="stop")
```

Fill out the report using the template in `playability-checklist.md`:

```markdown
# Playability Audit — {DemoName}
Date: YYYY-MM-DD

| Row | Status | Evidence | Fix needed |
|---|---|---|---|
| P0-A1 ... | PASS / FAIL | ... | ... |
...

## Verdict

X/12 P0 rows green. Demo is {playable | NOT playable}.

## Failed rows

(none if all green; else one entry per FAIL row with the fix action)
```

Save to `plans/reports/playability-audit-{date}-{demo}.md`.

---

## Step 7 — If any FAIL, fix and re-audit

For each failed row, spawn a focused agent with strict file ownership:

```
Agent(
  subagent_type="dots-environment" | "unity-ui-developer",
  description="Fix P0-{row} for {DemoName}",
  prompt="""
    Demo {DemoName} fails P0-{row} ({row description}).
    Evidence: {what was missing}.
    Fix scope: edit ONLY {file path(s)}.
    Implementation: {specific change — e.g. add WireXxxChildren call,
                    add multi-part silhouette, add phase banner overlay}.
    Verify: compile clean + re-screenshot in Play mode.
    Commit message: 'fix(rushtank-demo): P0-{row} ({short description})'.
  """,
  run_in_background=true)
```

Run the full audit again after fixes land. Iterate until 12/12 green.

---

## Quick one-liner for human auditors

If you don't want to run Unity MCP yourself, this is the human checklist:

1. Open scene. Run `Tools/{Demo}/Setup Scene`. Press Play.
2. Looking at the screen, can you ID: (a) the player, (b) the current phase, (c) your HP, (d) at least one button you can press?
3. Cause a phase change. Does the screen tell you it changed?
4. Win / lose the game. Does the end screen show stats + a New Run button?

If all 4 are "yes" → demo is shippable. If any is "no" → see the failed P0 row.
