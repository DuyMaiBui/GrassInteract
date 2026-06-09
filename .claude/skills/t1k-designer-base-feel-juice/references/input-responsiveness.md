---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Input Responsiveness & Feel

## Input Latency Budget

Total budget from hardware input to visible screen change:

| Game Type | Total Budget | Breakdown |
|-----------|-------------|-----------|
| Action / fighting | <50ms | Input poll 8ms + logic 8ms + render 16ms + display 16ms |
| Standard action-RPG | <80ms | More forgiving — complex logic allowed |
| Strategy / RPG | <150ms | Turn-based feel acceptable |
| Mobile (touch) | <100ms | Touch sensor adds ~20ms baseline |

**Rule**: If input → visual feedback exceeds budget, prioritize:
1. Start animation on input frame (don't wait for logic result)
2. Use optimistic rendering (assume success, rollback if needed)
3. Pre-buffer inputs 1-2 frames ahead

---

## Input Buffering

Accept input before current action completes — enables fluid combos:

| Buffer Window | Frames | Use Case |
|--------------|--------|---------|
| Tight (combo precision) | 2-3 frames | Fighting games, precise combos |
| Standard | 3-5 frames | Action RPG, standard combat |
| Loose (casual) | 6-8 frames | Platformer, casual games |

**Implementation**:
```csharp
// Store buffered input with timestamp
struct BufferedInput { InputAction action; float timestamp; }

// Accept if within buffer window
bool InputBuffered(InputAction action, float bufferWindow = 0.083f) // 5 frames @ 60fps
    => bufferedInputs.Any(b => b.action == action && Time.time - b.timestamp < bufferWindow);
```

**Gotcha**: Buffer window too large = accidental inputs. Too small = combos feel unresponsive. Test with real players to tune.

---

## Coyote Time

Grace period allowing jump after walking off an edge — makes platformers feel fair:

| Platform Type | Coyote Frames | Duration |
|--------------|--------------|----------|
| Standard platformer | 5-6 frames | 83-100ms @ 60fps |
| Precision platformer | 3-4 frames | 50-66ms |
| Casual/forgiving | 8-10 frames | 133-166ms |

**Implementation**:
```csharp
bool canJump = isGrounded || (timeSinceLeftGround < coyoteTime && !jumpPressed);
```

**Gotcha**: Coyote time must NOT apply after jumping off an edge (player-initiated fall). Check `wasGrounded` vs `isGrounded` transition.

---

## Cancel Windows

Allow action cancellation in early frames — gives player control over mistakes:

| Action | Cancel Window | Cancels Into |
|--------|--------------|-------------|
| Light attack | Frames 1-3 | Dodge, heavy attack, movement |
| Heavy attack wind-up | Frames 1-4 | Dodge only (commitment after) |
| Ability cast | First 20% of cast time | Movement (break cast) |
| Pickup animation | Frames 1-3 | Movement |

**Rule**: High-commitment actions (heavy attacks, abilities) have small cancel windows. Light actions have larger windows.

---

## Optimistic Rendering

Start animation on input frame; don't wait for logic validation:

```
Frame 0: Player presses attack button
Frame 0: Attack animation begins (IMMEDIATE — no wait)
Frame 1: Hit detection logic runs
Frame 1-2: Particle + sound triggered (slightly delayed from anim start)
Frame 2+: If attack hit = show impact effects; if missed = only play miss anim
```

**Why**: Players perceive "input → animation" latency, not "input → game-state-change" latency. Starting animation immediately feels responsive even if logic is one frame behind.

---

## Hold vs Tap Distinction

| Threshold | Tap | Hold |
|-----------|-----|------|
| Standard | < 0.3s | ≥ 0.3s |
| Long hold | < 0.3s tap, 0.3-0.8s short hold | ≥ 0.8s long hold |
| UI confirm | < 0.5s tap | ≥ 0.5s for destructive confirm |

**Use cases**:
- Tap = immediate action (light attack, select)
- Hold = alternate/charged action (heavy attack, context menu, aim)
- Hold to confirm = destructive action (sell item, delete)

---

## Dead Zone Configuration

Controller stick dead zone (inner = ignore drift, outer = max input):

| Zone | Value | Purpose |
|------|-------|---------|
| Inner dead zone | 0.10-0.15 | Ignore stick drift / neutral rest |
| Outer dead zone | 0.90-0.95 | Treat as max input (full speed) |
| Radial normalize | Yes | Remap [0.15, 0.90] → [0.0, 1.0] |

**Gotcha**: Inner dead zone too low → stick drift causes unintended movement. Too high → first 15% of stick travel does nothing (unresponsive feel).

---

## Input Priority System

When multiple inputs compete, higher-priority action wins:

```
Priority (highest → lowest):
1. Dodge / roll (interrupt everything)
2. Special ability (interrupt standard attacks)
3. Heavy attack (interrupt light attack, not dodge)
4. Light attack (interrupt movement)
5. Movement (lowest — always interruptible)
```

**Rule**: High-priority inputs interrupt ongoing low-priority actions immediately (frame of input). Never queue a dodge behind an attack animation.
