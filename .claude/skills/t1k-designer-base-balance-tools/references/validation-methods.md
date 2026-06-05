---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Validation Methods

## Simulation Testing

Run N simulated encounters to measure win rate without playtesting.

```csharp
// Pseudo-code: simulate encounter outcome
int wins = 0;
for (int i = 0; i < SimCount; i++)
{
    float playerHP = playerMaxHP;
    float enemyHP  = enemyMaxHP * enemyCount;
    while (playerHP > 0 && enemyHP > 0)
    {
        enemyHP  -= playerDPS * deltaTime;
        playerHP -= enemyDPS * deltaTime;
    }
    if (playerHP > 0) wins++;
}
float winRate = (float)wins / SimCount;
// Target: 55-75% win rate at recommended level
```

**Minimum sample**: 1000 iterations per encounter for <5% margin of error.
**Red flags**: win rate <40% (too hard) or >85% (trivial).

## Stat Audit (Code-Based)

Find outliers in all tunable constants without a spreadsheet:

```bash
# Extract all stat constants from PrefabCreators
grep -rn "baseStats\.\|\.HP\s*=\|\.ATK\s*=\|\.DEF\s*=\|\.Speed\s*=" \
  "Assets/Demos/" --include="*.cs" | grep -v "//\|Test\|\.meta"

# Extract all StatFormulaConfig multipliers
grep -rn "= [0-9]" \
  "Packages/" --include="StatFormulaConfig.cs"  # path varies by engine

# Check for outliers: sort numerically
grep -oP "\d+\.?\d*" output.txt | sort -n | uniq -c | sort -rn | head -20
```

Flag any value >2× the median of its category.

## Power Curve Graphing

Plot player power vs encounter difficulty across a full run:

```bash
# Gather data points from CrawlerEncounterSystem or equivalent
grep -n "difficulty\|scale\|multiplier\|index" \
  "Packages/" --include="*.cs" -r  # path varies by engine
```

Then plot in any spreadsheet: X=encounter index, Y=PlayerPower/EnemyPower ratio.
- Healthy: ratio stays 1.1-1.5 throughout
- Spike: ratio drops below 1.0 — insert rest encounter before
- Sag: ratio rises above 2.0 — increase enemy scaling or add elite variants

## Economy Audit

```bash
# Find all gold reward values
grep -rn "Gold\|Reward\|Currency\|reward" "Assets/Demos/" --include="*.cs" \
  | grep -i "= [0-9]\|reward\s*=" | grep -v "//\|Test"

# Find all gold cost values (shop, upgrades)
grep -rn "Cost\|Price\|cost\s*=\|price\s*=" "Assets/Demos/" --include="*.cs" \
  | grep "[0-9]" | grep -v "//\|Test"
```

Sum all faucets per run. Sum all sinks per run. Verify balance > 0 at run end.

## Difficulty Spike Detection

```bash
# Extract encounter scaling values
grep -n "0\.[0-9]\|[1-9]\.[0-9]" \
  "Packages/"  # path varies by engine
```

Then compute: `Difficulty[i+1] / Difficulty[i]` for each consecutive pair.
Flag any jump >1.5× as a spike requiring mitigation.

## Automated Balance Tests

Add to `Tests/EditMode/` to catch regressions:

```csharp
[Test]
public void StatRanges_AreWithinExpectedBounds()
{
    // Load StatFormulaConfig
    var config = AssetDatabase.LoadAssetAtPath<StatFormulaConfig>("...");
    Assert.IsTrue(config.StrToPhysAtk is >= 0.3f and <= 1.0f,
        "StrToPhysAtk out of balance range");
    Assert.IsTrue(config.VitToMaxHP is >= 5f and <= 20f,
        "VitToMaxHP out of balance range");
}

[Test]
public void DerivedStats_RatiosAreBalanced()
{
    float atk = 20f + 10 * 0.5f;     // Level 10, STR=10
    float def = 5f  + 10 * 0.2f;
    float ratio = atk / def;
    Assert.IsTrue(ratio is >= 1.5f and <= 4.0f,
        $"ATK/DEF ratio {ratio} outside balanced range [1.5, 4.0]");
}

[Test]
public void EncounterScaling_NoDifficultySpike()
{
    float prev = GetDifficulty(0);
    for (int i = 1; i <= 10; i++)
    {
        float curr = GetDifficulty(i);
        float jump = curr / prev;
        Assert.IsTrue(jump < 1.5f || i == 7,  // rest at index 7 is allowed
            $"Difficulty spike at encounter {i}: {jump:F2}×");
        prev = curr;
    }
}
```

## Empirical Player-Data Methods

Simulation and stat audits answer "is this internally consistent?" — they cannot answer "does this feel fair?". Pair the methods above with real-player telemetry once a build is playable.

Source for the framework: David Strachan, "Difficulty Curves" (https://www.davetech.co.uk/difficultycurves) [retrieved 2026-05-06]. The difficulty-saw framing lives in `puzzle-game-design/references/difficulty-saw-framework.md`; the methods below are the validation arm.

### 1. Death / Failure Location Heatmap

Log `(encounter_id, position, attempt#)` for every player failure. Plot density.

| Pattern | Diagnosis | Fix |
|---|---|---|
| Even distribution across the encounter | Pacing healthy | None |
| Cluster on one tile / spawn / move | Local spike | Patch the specific point, not the whole encounter |
| Cluster at encounter start | Readability problem | Tutorial cue missing or misleading; add visual telegraph |
| Cluster at encounter end with high attempt# | Resource economy too tight | Loosen move limit / HP / time by 15-25% |

Implementation: a single analytics event `failure_recorded { encounter, x, y, attempt }` plus a Grafana/Looker heatmap query is enough — this does NOT need bespoke tooling.

### 2. Completion Drop-Off Thresholds

For each encounter `i`, compute:

```
reached_pct[i]    = unique_players_who_started[i]   / cohort_size
completed_pct[i]  = unique_players_who_finished[i]  / cohort_size
drop[i]           = completed_pct[i-1] - completed_pct[i]
```

| Drop magnitude | Interpretation | Action |
|---|---|---|
| `> 40%` consecutive | Panic signal — encounter is too hard or too boring (either is bad) | Halt rollout; investigate before next push |
| `15-40%` | Hard wall typical of pre-boss / arc-end | Acceptable IF intentional and followed by rest |
| `5-15%` | Normal post-tutorial decay | None |
| `< 5%` | Encounter adds no friction | Question whether it earns its slot |

> Strachan's example: "If 80% finish level 4 but only 20% finish level 5, level 5 is too hard or boring."

### 3. Structured Friend / Closed-Beta Testing

Scripted questions, written-down answers. Not "what did you think?":

- "How long did encounter N take you?"
- "On encounter M, did you understand what the new mechanic does without trying it?"
- "Which encounter made you put the controller down?"
- "Did you use [specific tactic] — and if not, why?"

Spreadsheet the answers. Outliers across 5+ testers are signal; single-tester answers are anecdote.

### 4. Footage / Silent Observation

Watch recordings of unfamiliar players with audio off. Note **first failed attempts** — what they tried that didn't work tells you which alternate read of the encounter was plausible. If 4 of 5 testers try the same wrong move first, the encounter's visual language is misdirecting (not its difficulty).

### 5. Pairing Empirical With Simulation

| Question | Best Method |
|---|---|
| "Is the math internally consistent?" | Simulation testing (1000+ iterations) |
| "Is there a difficulty spike at index `i`?" | Spike detection + telemetry confirmation |
| "Does the encounter FEEL fair?" | Empirical methods (telemetry + observation) |
| "Why are players churning at encounter `N`?" | Death heatmap + footage → identify the specific failure mode |

Simulation says "win rate 62%" → Empirical says "but 70% of those wins came from one degenerate strategy we didn't model" → fix.

## MCP Validation (Runtime)

Use the runtime validation agent (engine layer) after any stat or balance code change:
1. `read_console` — zero compile errors
2. Enter Play mode via `manage_editor`
3. `validation_snapshot` — capture entity states
4. Check TTK in real combat via `read_console` logs
5. Verify no NaN stats (indicates missing component on entity)

→ See runtime validation skill in your engine layer for full protocol
