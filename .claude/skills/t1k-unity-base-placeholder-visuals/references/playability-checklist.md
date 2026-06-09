---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Playability Validation Checklist — Auditor's Worksheet

Use this worksheet to audit a demo before declaring it playable. Each row has a concrete check ("look at the Game View at Y") + a Unity MCP verification command. Pass criteria are objective.

Run the full pass after every visual change. A demo with ANY failing P0 row is not shippable — it's not playable.

---

## How to run

1. Open the demo scene (`File > Open Scene`).
2. Run `Tools/{DemoName}/Setup Scene` (the one-button setup).
3. Clear `Library/EntityScenes/` (force SubScene rebake).
4. Enter Play mode.
5. Walk through each row below. Record PASS / FAIL / N/A.
6. For every FAIL → spawn a `dots-environment` or `unity-ui-developer` agent with the specific row + fix description.
7. After fixes → re-run from step 2.

Total time: ~5 minutes per demo on a clean session.

---

## A. World readability (4 rows)

### P0-A1 — Player avatar visible
**Check:** In Game View at game start (Arrange / Wave 1 / Idle phase), the player avatar is on screen with a recognizable silhouette built from at least 3 primitive child GameObjects.

**Pass criteria:**
- At least 3 distinct child renderers under the player root (e.g. body + tracks + turret; body + cape + head; chassis + 2 wheels).
- Each child has its own colored material (no two children share a material).
- Silhouette reads as "this is the player" without a label.

**MCP probe:**
```
find_gameobjects(search_term="TankChassis", search_method="by_name")
# then read mcpforunity://scene/gameobject/{id}/components — confirm child count + renderers
```

**Common failure:** single grey quad. Fix: see RushTank `RushTankSceneSetup.SubScene.TankVisual.cs` — 4 child quads (body + tracks + turret + barrel + cockpit).

### P0-A2 — Enemies visually distinguishable
**Check:** Spawn or wait for each enemy archetype. Each must look distinctly different from every other.

**Pass criteria:**
- N archetypes → N distinct silhouettes (not just color variants).
- Each archetype has a unique body shape (scale) + at least one accent part (barrel/spike/rotor/etc.).
- Two side-by-side enemies of different archetypes should be identifiable without labels.

**MCP probe:**
```
find_gameobjects(search_term="Enemy", search_method="by_name")
# count distinct mesh-renderer color sets; one set per archetype
```

**Common failure:** all enemies are flat colored quads with same shape, only color differs. Fix: see RushTank `RushTankPrefabCreator.EnemyParts.cs` — `ApplyArchetypeBodyShape` + `AddArchetypeParts` per archetype.

### P0-A3 — Ground line + horizon backdrop
**Check:** Take a Game View screenshot. The frame must contain at least 3 visually distinct horizontal bands (sky / mid-ground / ground), not a solid-color void.

**Pass criteria:**
- At least 3 quad GameObjects parented under World root, named `Env_Ground`, `Env_Sky`, `Env_Mountain` (or similar).
- Each at a different Z depth (ground in front, sky furthest).
- All using `Universal Render Pipeline/Unlit` materials saved to `Materials/Env_*.mat`.

**MCP probe:**
```
find_gameobjects(search_term="Env_", search_method="by_name")
# expect >= 3 results
```

**Common failure:** camera clear color = brown, nothing else. Fix: see RushTank `RushTankSceneSetup.Environment.cs` — 6-quad layered backdrop.

### P0-A4 — Items / pickups visible + distinct
**Check:** If the demo has items/pickups, view the Workshop/Inventory/Spawn area. Each item type must look distinct.

**Pass criteria:**
- Each item type has a unique color OR shape variant.
- Rarity is encoded in color (Common=grey, Uncommon=green, Rare=blue, Epic=purple, Legendary=gold).
- Item names readable on hover/inspection (label, tooltip, or text overlay).

**N/A if:** demo has no item system (pure combat arena, idle clicker without rewards).

---

## B. HUD — game state visible (3 rows)

### P0-B1 — HP bar visible
**Check:** Enter Play mode. HP bar should render at the top of the screen, with a red/green fill proportional to current HP and numeric overlay ("200 / 200").

**Pass criteria:**
- `UnityEngine.UI.Slider` widget visible.
- Fill rect rendered.
- Numeric overlay text rendered ("X / Y").
- Damage causes the bar to update visibly.

**MCP probe:**
```
find_gameobjects(search_term="HpBar", search_method="by_name", include_inactive=true)
# expect 1+ result; check sliderValue updates after a damage event
```

**Common failure:** SerializedField for `hpBar` left null — bar exists in code but never on screen. Fix: see RushTank `RushTankSceneSetup.UI.HUD.cs` — `WireHUDChildren` constructs + `SerializedObject` wires.

### P0-B2 — Phase / objective text visible
**Check:** Phase label on screen updates whenever the game phase changes.

**Pass criteria:**
- Text widget visible on screen reading current phase ("Phase: Arrange", "Phase: Rolling", etc.).
- Updates within 1 frame of phase change.
- Transient phase-intro banner appears for ~2s on phase change (e.g. "ARRANGE — Drag items to tank").

**MCP probe:**
```
find_gameobjects(search_term="PhaseText", search_method="by_name")
find_gameobjects(search_term="PhaseBanner", search_method="by_name")
```

**Common failure:** silent transitions — phase changes but player has no feedback. Fix: see RushTank `RushTankPhaseBanner.cs` — watches ECS `RunPhase` singleton, fades in title + instruction.

### P0-B3 — Currency / resource counters
**Check:** Currencies (Scrap, Gold, XP, Cores, etc.) are shown with both a label and a numeric value.

**Pass criteria:**
- Format: `"<Currency>: <value>"` (e.g. "Scrap: 47", "Cores: 4").
- Updates when value changes.
- Multiple currencies stacked vertically or side-by-side (not all on one line crammed).

---

## C. Interaction — player knows what to do (3 rows)

### P0-C1 — At least one clickable button per active phase
**Check:** For every phase the player can be in, there's at least one labeled interactive control on screen.

**Phases to check:**
- Arrange / Setup: GO/Start/Confirm button
- Rolling / Combat: Speed-control or Pause
- Shop: Buy/Reroll/Exit
- Event: 2-3 Choice buttons
- Won/Lost: New Run button

**Pass criteria:**
- Button has a visible label (text, not just an icon).
- Click triggers a visible state change.
- No silent screens where the player must guess the input.

### P0-C2 — Phase-intro feedback
**Check:** Trigger a phase change manually (use the Map UI or hotkey). The transition is announced visually.

**Pass criteria:**
- A banner / overlay / text change occurs within 0.5s of the phase change.
- The new phase's instruction is on screen ("Drag items to tank", "Defeat all enemies", "Choose your path").

### P0-C3 — End-state screens
**Check:** Force a Won state (kill the boss or use a cheat). Force a Lost state (zero the player HP via console). Each must show its own screen.

**Pass criteria:**
- WonPanel and LostPanel are separate sibling GameObjects.
- Won shows "VICTORY" + stats + "+N Cores" + "New Run" button.
- Lost shows "DEFEAT" + stats + "New Run" button.
- New Run resets the game state.

---

## D. Setup / re-runability (2 rows)

### P0-D1 — One-button scene setup
**Check:** Delete the scene file. Re-run `Tools/{DemoName}/Setup Scene`. Confirm the scene is rebuilt from scratch with all entities + UI + environment.

**Pass criteria:**
- One menu invocation produces a fully-playable scene.
- No Inspector touch-ups required.
- Re-runnable: running setup twice produces the same scene (idempotent).

### P0-D2 — No null SerializeFields after setup
**Check:** Inspect every authoring + UI MonoBehaviour in the SubScene + Canvas hierarchies. Every `[SerializeField]` reference must be assigned.

**Pass criteria:**
- Zero `None (Slider)`, `None (Text)`, `None (Button)`, `None (GameObject)` slots in the Inspector after `Setup Scene`.
- Confirm via SerializedObject scan: every `SerializeField` has a non-null `objectReferenceValue`.

**MCP probe:**
```
# For each UI MonoBehaviour:
find_gameobjects(search_term="RushTankHUD", search_method="by_name")
# read mcpforunity://scene/gameobject/{id}/components — every SerializeField shows a target
```

**Common failure:** the partial-file comment "user wires refs in Inspector" is a smell — it means refs ARE null and the demo will appear broken on a clean checkout. Fix: build widgets in code, wire via `SerializedObject` (see RushTank `WireHUDChildren`).

---

## Validation report template

When auditing a demo, fill this out:

```markdown
# Playability Audit — {DemoName}
Date: YYYY-MM-DD
Auditor: {agent or human}

## Results

| Row | Status | Evidence | Fix needed |
|---|---|---|---|
| P0-A1 Player avatar | PASS | TankChassis has 5 child renderers | — |
| P0-A2 Enemies distinct | PASS | 6 archetypes, each unique body+parts | — |
| P0-A3 Ground+horizon | PASS | 6 env quads (ground/haze/sky/3 mountains) | — |
| P0-A4 Items distinct | PASS | 10 items, rarity tint Common/Uncommon | — |
| P0-B1 HP bar | PASS | Slider 200/200 visible top-left | — |
| P0-B2 Phase text | PASS | "Phase: MapSelect" + banner overlay | — |
| P0-B3 Currency | PASS | Scrap: 0, Cores: 4 visible | — |
| P0-C1 Clickable per phase | PASS | MAP / 1×/2×/4× / Map nodes all clickable | — |
| P0-C2 Phase-intro | PASS | RushTankPhaseBanner watching singleton | — |
| P0-C3 End screens | PASS | Won + Lost panels both built | — |
| P0-D1 One-button setup | PASS | Tools/RushTank/Setup Scene rebuilds clean | — |
| P0-D2 No null SerializeFields | PASS | All 20+ refs wired via SerializedObject | — |

## Verdict

**12/12 P0 rows green → demo is playable.**

## Next steps

- (none if all P0 green)
- (else: file a follow-up issue per failed row)
```
