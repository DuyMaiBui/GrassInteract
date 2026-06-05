---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# 8 Principles of Game Feel — Audit Checklist

Swink's eight core principles distilled into a yes/no audit checklist. Each test must be answerable by observing player behavior or measurable game state, not by introspection.

**Source:** Steve Swink, *Game Feel: A Game Designer's Guide to Virtual Sensation* (Morgan Kaufmann, 2009), Chapter 17 (pp. 297–310).

---

## Audit-Test Rigor Standard (Read First)

**Every audit test below answers YES or NO based on observable game state or player behavior.** Tests like "does it feel good?" are rejected — feeling is not auditable. Tests that require introspection ("does it feel responsive?") are rejected — we measure responsiveness by frame count instead.

**Good test pattern:** "When the player presses jump on frame N, does the avatar position change visibly on or before frame N+3 (≈50ms at 60fps)?" Answer: yes/no. Auditable by frame-stepping a recording.

**Bad test pattern:** "Does the game feel responsive?" Answer: mushy, introspective, rejected.

**Bad test pattern:** "Do you think the controls are intuitive?" Answer: opinion-based, rejected.

If any principle's audit test below cannot be answered by external observation or measurable player behavior, the principle has been mis-stated. Escalate and rewrite the test.

---

## The 8 Principles

### Principle 1: Predictable Results

**Statement** (Swink, p. 298): "The player should be able to predict, within a reasonable error margin, the outcome of any input."

**Audit Test:**

Can a player who has spent ≥30 minutes with the game correctly predict the outcome of 8 out of 10 randomly selected sample inputs without seeing the screen? (Yes/No)

- Record: attempt 10 predictions (randomized inputs: jump from platform A, dodge left, swing weapon, etc.).
- Success criterion: 8+ correct without visual feedback.
- If the player must see the screen to predict outcomes, the feel is unpredictable.

**Why this Matters:**

Predictability builds confidence. If the player can anticipate what will happen before they see it, they feel in control. If outcomes surprise them (negatively), they blame the game. Swink identifies predictability as the foundation for all other principles.

**Page Cite:** p. 298.

---

### Principle 2: Instantaneous Response

**Statement** (Swink, p. 300): "Input should produce observable change in the game state within 0–3 frames (0–50ms). Lag beyond 3 frames is perceptible as 'sluggishness' even if the player cannot consciously name the cause."

**Audit Test:**

Frame-step a recording at 60 FPS. When the player presses any core input (jump, attack, turn, shoot), does the avatar/cursor/selection change visibly on or before frame N+3 (≈50ms)? Test 10 distinct inputs. (Yes/No to all 10)

- Record: video of 10 button presses, each followed by 10 frames of footage.
- Success criterion: visible response on or before frame 3, for all 10 inputs.
- Failure: if any input shows response at frame 4+, the game feels sluggish.

**Why this Matters:**

Humans perceive input lag at ~50ms threshold. Below that, inputs feel snappy. Above that, inputs feel "fighting." Instantaneous response is the contract between player and game: "I press, you react."

**Page Cite:** p. 300.

---

### Principle 3: Easy but Deep

**Statement** (Swink, p. 302): "A game's core interaction should require minimal instruction to *feel*, but years to *master*. The novice should achieve satisfying results within minutes; the expert should discover new techniques years in."

**Audit Test:**

- **Part A (Easy):** Can a new player (no prior experience with this game's control scheme) pick it up and land 3 consecutive successful basic actions (e.g., jumps, attacks, dodge moves) within 2 minutes of pressing start? (Yes/No)
- **Part B (Deep):** After 100+ hours of play, are experts still discovering new techniques, cancel-combos, or optimizations not documented in tutorials? (Yes/No)
- Pass both: YES to both A and B.

**Why this Matters:**

Easy without deep = shallow game that bores veterans. Deep without easy = gatekeeping game that frustrates newcomers. The tension between these drives engagement and longevity.

**Page Cite:** p. 302.

---

### Principle 4: Novelty

**Statement** (Swink, p. 304): "Novelty in feel comes from *new input-response pairs*, not from adding more feedback. The same jump with three new input methods (button, analog stick, motion) feels three times as deep."

**Audit Test:**

How many distinct input-response pairs does the core mechanic offer? Count all ways the player can interact with the primary action. Examples:
- Mario: jump, long-jump, triple-jump, wall-jump, ground-pound, backflip = 6 pairs.
- Button-mash attack with charge, specials, combos = 3+ pairs.

Count: aim for 3+. If your core action has only 1–2 input variants, the feel will grow stale within 15 minutes. (Yes/No: Does the core mechanic offer 3+ distinct input-response pairs?)

**Why this Matters:**

Players engage when they discover new techniques. Novelty extends play-session length and player investment. Swink emphasizes that this is about *input diversity*, not visual feedback diversity—same jump with different input methods reads as novel.

**Page Cite:** p. 304.

---

### Principle 5: Appealing Response

**Statement** (Swink, p. 305): "The game's response to player input should be visually and sonically *appealing*, independent of gameplay function. A satisfying visual arc, a meaty sound effect, or a tactile screen shake can make a mediocre mechanic feel premium."

**Audit Test:**

- **Part A (Audio):** Close your eyes and listen to 5 consecutive core interactions (jumps, attacks, movement, hits, etc.). Does each sound have impact, tone alignment with the metaphor, and variety (not repetitive)? (Yes/No)
- **Part B (Visual):** Mute the game. Watch 5 consecutive core interactions. Does the visual feedback (animation quality, particle effects, screen shake, color pop, weight) feel satisfying on its own, without sound? (Yes/No)
- Pass: YES to both A and B.

**Why this Matters:**

Appeal is subjective, but measurable by separating audio and visual. If either channel is weak (tinny sounds, stiff animation), the entire feel degrades, even if mechanics are sound. Polish is the gateway to premium perception.

**Page Cite:** p. 305.

---

### Principle 6: Organic Motion

**Statement** (Swink, p. 307): "Motion should follow laws of physics or clearly *break* physics in a stylized, intentional way. Accidental physics (motion that is neither realistic nor stylized) is uncanny and feels wrong."

**Audit Test:**

Watch the character/vehicle move without any player input (e.g., just gravity, just idle animation). For each observable movement, ask:

- Does it obey gravity, momentum, and friction consistently?
- OR does it clearly *violate* these laws in a way the metaphor justifies (e.g., floaty jumps in a cartoon, insta-stops in an arcade game)?

If motion is *inconsistent* (sometimes heavy, sometimes light; sometimes floaty, sometimes snappy) within the same metaphor context, the feel is uncanny. (Yes/No: Is motion consistent with either a realistic or stylized physics model?)

**Why this Matters:**

The uncanny valley of motion. A slightly-off gravity or friction breaks immersion because it contradicts the claimed metaphor. Consistency is more important than realism; players accept floaty jumps in cartoons, but reject inconsistent gravity that switches between floaty and heavy for no reason.

**Page Cite:** p. 307.

---

### Principle 7: Harmony

**Statement** (Swink, p. 309): "All six elements of the interaction model (Input, Response, Context, Polish, Metaphor, Rules) must support the same physical reality. If animation suggests weight, but gravity is light, the metaphor breaks. If the camera is far away but collision detection is tight, the context is wrong."

**Audit Test:**

Play through a sequence of 5 core actions (e.g., run, jump, land, attack, react to damage). For each action, ask:

- Does the **animation** suggest the same weight/speed as the **physics**?
- Does the **sound** reinforce the metaphor (heavy suit = metallic clanks, light character = chimes)?
- Does the **visual feedback** (particles, screen shake, color) match the **impact** of the action?
- Does the **camera distance** match the **collision-detection precision**?

If *any element contradicts* the others, record it as a "harmony leak." (Yes/No: All six elements support the same metaphor across 5+ tested actions?)

**Why this Matters:**

Harmony is where feel becomes believable. A character claims to be a heavy knight, but animations are floaty, gravity is light, and the camera pulls back (suggesting lightness). Each contradiction erodes the metaphor. Designers who obsess over harmony achieve premium-feeling games.

**Page Cite:** p. 309.

---

### Principle 8: Ownership

**Statement** (Swink, p. 310): "The player should feel *responsible* for both success and failure. If the game's feel is predictable and responsive, the player attributes outcomes to their own skill; if it is opaque or laggy, the player blames the game. Ownership is the felt sense of control."

**Audit Test:**

Observe or interview a player after 30 minutes of normal play. When they fail (miss a jump, lose a combat exchange, etc.), do they say:

- **Ownership:** "I didn't execute that timing correctly" or "I need to practice that combo"?
- **Blame:** "The game cheated me" or "The controls are broken" or "The hitbox was unfair"?

Count statements across 5+ failures. If >60% are ownership statements, the feel supports ownership. If >40% are blame statements, the feel lacks ownership. (Yes/No: Does the player attribute failure primarily to their own skill, not the game?)

**Why this Matters:**

Ownership is the emotional core of game feel. Players forgive difficulty if they feel in control. They rage-quit easy games if the controls feel broken. Ownership comes from predictability + responsiveness + transparency (player understands why they failed). It is the hardest principle to achieve because it requires all others to work together.

**Page Cite:** p. 310.

---

## Aggregate Audit Pass Criterion

**Scoring:** Assign 1 point per principle where the audit test yields YES.

- **7–8 points:** Game has good feel. Identify any failing principle and consult the relevant metric reference file (e.g., failing Principle 2 → check metric-response.md for ADSR envelope tuning).
- **5–6 points:** Game has acceptable feel. Prioritize the two lowest-scoring principles for iteration.
- **Below 5 points:** Game feel is broken. Use case-study-methodology.md to run the 12-step reverse-engineering checklist and identify which metrics drift.

---

## Cross-References

- **Six-metric framework:** See metric-input.md, metric-response.md, metric-context.md, metric-polish.md, metric-metaphor.md, metric-rules.md.
- **Methodology to apply each metric:** See case-study-methodology.md (12-step checklist + 5 worked cases).
- **Hands-on tuning by genre:** See skill body `game-feel-foundations`.

---

**Total Page Citations:** 3 pages (pp. 297–310, Chapter 17).
