---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Context — Game Feel Metric 3

How the spatial environment frames the player's perception of speed, scale, and collision. The same character moving the same way feels distinctly different in tight halls versus open fields. Source: Swink Chapter 8 (pp.139-150).

## Definition

**Context** is the effect of simulated space on game feel: collision detection, spatial layout, object spacing, speed perception, and size impression (p.139). Context operates at three levels — high-level (global spatial impression), medium-level (immediate space around the avatar), and low-level (intimate, tactile collision interaction) (p.149).

## Three Levels of Context

### High-Level — Impressions of Space and Scale

The overall sense of space is created by the architecture and layout of the game world (p.139-144). Speed and size are relative; they require visual reference.

**Soft Metrics:**
- Space impression: open vs. constrained (p.140)
- Visual references: distant mountains, ceiling height, fog density establish spatial scale
- Sightlines: clear or occluded by objects
- Player agency: exploration encouraged or immediate focus required

**Examples from Swink:**
- World of Warcraft: open, sprawling space with visible mountains → players feel small but free (p.140)
- Counter-Strike: tight, twisting tunnels and close quarters → hemmed-in feeling, emphasis on careful positioning (p.142-143)
- Oblivion: large expansive worlds with clear sightlines → sense of openness (p.140)

**Key Insight:** Swink notes personal anecdotes from hiking in Arizona (p.140) to illustrate how the same avatar speed feels different in vast space versus claustrophobic caverns — no quantitative threshold given, entirely perceptual.

### Medium-Level — Object Spacing and Avoidance

At the medium level, context is the immediate spatial topology around the avatar: object count, sizes, distances, and navigation patterns (p.145-146).

**Measurable Metrics (Figure 8.2, p.145-146):**
- Number of objects in local area
- Size of objects
- Distance between objects (spacing intervals)
- Layout pattern: grid, clustered, scattered

**Examples:**
- Spaced-far-apart objects → open feel, easy navigation (p.145)
- Closely-spaced small objects → cramped feel, requires precision (p.146)
- Variable spacing → challenge density varies as player moves

**Highway Hypnosis Effect (p.146):** World of Warcraft example — uniform object spacing + constant avatar speed = hypnotic, trance-like effect. Changing spacing or speed breaks the effect. *No specific object-per-distance ratio given by Swink; designers tune empirically.*

**Soft Metrics:**
- Spatial coherence: objects feel like a natural environment or jarring layout?
- Navigation feel: organic or requiring constant micro-corrections?
- Speed perception: how does object spacing modulate apparent motion speed?

### Low-Level — Collision Models and Tactile Behavior

At the low level, context is how objects collide and interact at close range — the most tactile and physics-based layer (p.147-149).

**Hard Metrics:**
- Collision model type (frictionless, sticky, bouncy, realistic) (p.147)
- Collision response: bounce vs. slide vs. stop
- Friction and restitution values (if simulated)

**Swink's Collision Model Examples (p.147-149):**
- **Waterslide model** (World of Warcraft): smooth, frictionless sliding; objects push player along without resistance → feels safe, soft (p.147-148)
- **Rigid model** (Vanishing Point / GTA): collision with barrier at speed is crisp, violent, no slide → feels hard, responsive (p.147)
- **Sticky model** (Burnout Revenge): moderate friction; collisions slide but with resistance → feels realistic, weighty (p.148)

**Soft Metrics:**
- Does collision match game intent? (Playful = waterslide; racing = rigid; realistic = sticky)
- Is collision response predictable?
- Do collisions feel solid and weighty?

## Sub-axes and Design Dimensions

- **Object spacing relative to avatar speed** (medium-level)
- **Camera field of view and distance** (high-level impression of scale)
- **Visual reference density** (landmarks for speed perception)
- **Collision model type** (frictionless, sticky, bouncy)
- **Per-surface friction** (ice, mud, sand, water)
- **Perspective and camera angle** (first-person vs. third-person distance)

## Tunable Knobs (Designer Controls)

- **Visibility:** fog distance, draw distance, object culling
- **Object density:** number of obstacles per spatial unit
- **Object spacing intervals:** distance between obstacles
- **Avatar size relative to environment:** camera distance, scale factor
- **Collision model:** choose frictionless, sticky, bouncy, or realistic
- **Collision response curve:** how quickly objects decelerate on impact
- **Visual references:** place landmarks at regular intervals to prevent highway hypnosis (p.146)
- **Doppler audio:** pitch shift for fast-moving objects (optional, not quantified by Swink)

## Diagnostic Questions

- **High-level:** Does the space feel open or constrained? Are there sufficient visual cues (distant features, ceiling height) to establish scale?
- **Medium-level:** How densely packed are obstacles? Is the avatar's speed balanced relative to object spacing?
- **Low-level:** What collision model is used? Does it match the game's tone and genre intent?
- **General:** Do collisions feel consistent and predictable? Are there spatial surprises (invisible walls)?

## Anti-patterns

- **Uniform terrain without landmarks:** highway hypnosis; breaking spacing or speed alleviates (p.146)
- **Over-constrained collision:** sticky collision in a playful platformer; contradicts genre intent
- **Obstacle spacing too sparse:** environment feels empty and disengaging
- **Obstacle spacing too dense:** layout becomes chaotic and overwhelming
- **No visual references:** speed becomes unjudgeable relative to the world
- **Inconsistent collision behavior:** objects slide sometimes, stick other times → player confusion
- **Scale-speed mismatch:** large objects moving fast feel uncanny; small objects moving slowly feel lumbering
- **Poor visibility:** player cannot see threats or navigate clearly

## Cross-reference

- **Response metric:** how the avatar moves through context (attack phase, sustain, release)
- **Polish metric:** audio/visual cues that reinforce context (doppler audio, motion blur, screen shake)
- **Metaphor metric:** whether context matches the game's conceptual frame (waterslide = casual; rigid = hardcore racing)

---

## Notes on Swink's Methodology

Swink emphasizes that high-level and medium-level context metrics are primarily soft (observational, perceptual). No universal spacing ratio is given for "good" layouts — designers must playtester-tune based on game intent and genre convention. Low-level collision metrics are more hard (selectable model type, friction coefficient), but the *perception* of collision (whether it feels weighty, responsive, or playful) remains subjective and context-dependent (p.147-149).
