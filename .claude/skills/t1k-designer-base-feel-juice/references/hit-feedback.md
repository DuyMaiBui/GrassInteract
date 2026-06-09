---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Hit Feedback Patterns

## Hit Stop (Freeze Frames)

Hit stop = brief timescale pause on impact. Makes hits feel weighty and satisfying.

| Impact Type | Duration | Implementation |
|-------------|----------|---------------|
| Light hit (normal attack) | 2 frames (33ms @ 60fps) | `Time.timeScale = 0` for N frames |
| Heavy hit (heavy attack) | 3-4 frames (50-66ms) | Same, longer duration |
| Critical hit | 4-6 frames (66-100ms) | + scale punch on attacker |
| Boss taking damage | 2-3 frames | Subtle — boss shouldn't stagger too often |
| Player taking heavy damage | 3-4 frames | Camera shake simultaneous |

**Implementation note**: Freeze only gameplay timescale, not UI timescale. Use `Time.timeScale` + `Time.fixedDeltaTime` reset, OR separate DOTSSimulationGroup pause.

**Gotcha**: Hit stop >8 frames (133ms) feels like a bug, not style. Keep it subtle.

---

## Hit Flash

Entity flashes white (or red for player) on taking damage.

**Timing**:
- Flash ON: frame of impact
- Flash duration: 1-2 frames (16-33ms)
- Optional: second flash at 50% if multiple hits land close together

**Implementation (DOTS)**:
- Use `MaterialPropertyBlock` or ECS material property component (`HitFlashTag`)
- `SpriteColor` component: set to white for 1 frame, restore original color
- Shader: `lerp(_BaseColor, white, _HitFlashAmount)` — blend via material property

**Player hit tint**: red instead of white (1-3 frames), stronger indication of damage to self.

---

## Knockback

Visual displacement on hit — makes impact feel physical.

| Hit Type | Displacement | Duration | Recovery |
|----------|-------------|----------|---------|
| Light hit | 0.2-0.3 units | 0.1s | Spring back 0.15s |
| Heavy hit | 0.4-0.6 units | 0.15s | Spring back 0.2s |
| Knockback ability | 1-3 units | 0.2-0.3s | No spring — intentional repositioning |
| Boss slam | 0.5-1.0 units | 0.2s | Spring back 0.3s |

**Direction**: always away from attacker (normalize `target.pos - attacker.pos`).
**DOTS**: `AgentCCOverrideSystem` handles knockback via `PhysicsVelocity` impulse. See `agents-navigation` skill.

---

## Damage Numbers

- **Spawn position**: at hit point, offset 0.3-0.5 units toward camera
- **Animation**: float up 1-2 units over 0.8-1.0s, then fade alpha 0→1 over last 0.3s
- **Scale**: base 1.0×, crit 1.5-2.0× (brief scale-up then settle)
- **Color coding**:
  - White: standard physical damage
  - Yellow/orange: critical hit
  - Red: player taking damage (not enemy — inverted for clarity)
  - Green: healing
  - Blue: mana damage / magic drain
  - Purple: poison/DoT
  - Gray: blocked / reduced damage
- **Gotcha**: Color alone is not enough — pair with icon for colorblind. Use crit icon ⚡ or size difference.
- **Cap visible count**: 6-8 floaters max on screen; merge rapid hits into accumulated number

---

## Screen Shake on Hit

→ See `references/screen-effects.md` for full shake formula.

**Quick values for hit context**:
- Normal hit landed (attacker's camera): magnitude 0.05-0.1, decay 0.15s
- Hit received (defender's camera): magnitude 0.15-0.25, decay 0.2s
- Explosion nearby: magnitude 0.3-0.5, decay 0.3s
- Boss slam ground: magnitude 0.4, decay 0.4s, radial pattern

---

## Particle Bursts on Hit

**Particle count by impact**:
- Light hit: 4-8 particles (sparks, dust motes)
- Heavy hit: 8-15 particles
- Critical hit: 12-20 particles + trail
- Kill: 15-25 particles + lingering smoke/blood

**Particle types by damage**:
- Physical/slash: sparks + metal fragments
- Fire: flame wisps + ember scatter
- Ice: ice shard fragments + frost mist
- Poison: green droplets + vapor
- Magic/arcane: energy wisps + geometric shards

**Rule**: Particle burst direction = away from attacker (same as knockback direction).

---

## Sound Priority on Hit

Sound is the highest-priority feedback — even if visual budget is exceeded:

1. Hit sound (always plays, never skip)
2. Critical hit sound variant (louder, more impact)
3. Death sound (distinct, memorable)
4. Ambient sound (skip if hit sound queued)

**Audio design rule**: vary 3-5 hit sound variants, play randomly to avoid repetition fatigue.
