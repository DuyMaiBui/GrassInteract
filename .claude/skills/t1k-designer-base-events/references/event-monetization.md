---
title: Event Monetization — Banners, Bundles, Pity Tables
skill: game-events
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---

# Event Monetization

## Banner Design

### Standard Event Banner
```
Featured item rate:    1.6% per pull (displayed in UI — legal requirement in many markets)
Soft pity start:       at 70 pulls (rate begins increasing)
Hard pity (guarantee): at 90 pulls (guaranteed featured item)
Per-banner pity:       resets when banner ends — does NOT carry to next banner
Bundle pity bonus:     bundle purchase = +10 pulls toward pity (shown to player)
```

### Banner Reveal Timing
- Reveal banner items 48h before event launch
- Tease silhouette or partial art 72h before launch (organic social moment)
- Post featured pull rates in all markets (legal compliance; Apple/Google requirements)

---

## Bundle Formulas

### Standard Bundle ($4.99-$9.99)
```
Contents:
  - 1× event exclusive cosmetic (limited-time)
  - Event currency: 500 (≈ 2 days of full daily play)
  - 1× booster or consumable
  - 200 soft currency
Value proposition: 4-5× value vs direct currency purchase
```

### Premium Bundle ($19.99-$29.99)
```
Contents:
  - Everything in Standard bundle
  - 1× additional limited cosmetic (color variant or accessory)
  - 1× featured banner pull (with pity count credit)
  - 500 soft currency
Value proposition: 6-8× value; targets mid-spender segment
```

### Whale Bundle ($49.99-$99.99)
```
Contents:
  - Everything in Premium bundle
  - 10× featured banner pulls (guarantees featured item via soft pity range)
  - 2× additional cosmetics
  - Season pass upgrade (if applicable)
Targeting: top 1-3% of spenders; accounts for 30-40% of event revenue
```

---

## Revenue Attribution Benchmarks

| Monetization type | % of event revenue (typical) |
|-------------------|------------------------------|
| Banner (gacha pulls) | 45-55% |
| Bundles | 25-35% |
| Currency top-up | 10-15% |
| Premium pass/track | 10-15% |

### Conversion Rate Targets During Events
- IAP conversion (event window): 3-7% of DAU (vs 1-3% baseline)
- Bundle attach: 15-25% of IAP converters purchase event bundle
- Banner spenders: 5-10% of DAU who engage with event

---

## §BPC — BackpackCrawler Calibrated Pity & Lessons

> Sources: `docs/wiki/data/backpack-crawler/revenue-mix.json` (locked 2026-04-29, Round 3 corrections 2026-05-02); Systems.md §3.3 (gacha pity tables); IAA-Strategy doc §6 (LTV derivation).

### BPC Banner Pity (per-event)
| Parameter | Value | Notes |
|---|---|---|
| Featured rate | 1.6% | Displayed in UI per legal requirement |
| Soft pity | 70 pulls | +6% rate increase per pull thereafter |
| Hard pity | 90 pulls | Guaranteed featured |
| Per-banner reset | YES | Does NOT carry across banners |
| Bundle pity bonus | +10 pulls | Standard bundle ($4.99) shown to player |

### BPC Bundle Tiers
| Tier | Price | Contents | Cozy framing |
|---|---|---|---|
| Standard | $4.99 | 1× event cosmetic + 500 event currency + 1 booster + 200 soft | "Frank's deal — small one." |
| Premium | $9.99 | Standard + 1 color variant + 1 banner pull + 500 soft | "Frank's deal — bigger one." |
| Whale | $49.99 | Premium + 10 banner pulls + 2 cosmetics + season pass | "Mochi's tail wag tier." |

### Lesson 1 — HKIA Was Not a Peer
Earlier draft cited Hello Kitty Island Adventure as a cozy-IAA peer for pity calibration. **HKIA is $40 premium / Apple Arcade with zero microtransactions.** No proven cozy + heavy-IAA + dual-tier ad-free pass mobile peer exists at scale. BPC is first-of-kind in this monetization stack — soft launch must prove the model BEFORE applying any "cozy peer" pity benchmark. **Do not borrow pity values from premium-paid games for F2P-IAA stacks.**

### Lesson 2 — D7 Floor as Kill Switch
BPC sets D7 floor at 18% (not industry-typical 22%) to absorb expected 2-4pp Tier 3 ad-density churn. **Auto-revert trigger:** if D7 < 18% any week, automatically disable sequential rewarded, daily mystery ad-pull, weekend cosmetic tickets, push, surveys; demote offerwall; revert interstitial cadence to 1/2 runs. The kill switch is not optional — it is the cozy-pillar guardrail.

### Lesson 3 — Whale Bundle ≠ Cozy Violation IF Framed Right
Whale bundle ($49.99) accounts for 30-40% of event revenue (industry-typical for top-1-3% spenders). BPC keeps cozy by:
- Frank-voice sponsor framing on EVERY banner placement
- Mochi pity-counter UI (small dog-ear icon ticks toward guarantee — visual cue, not nag)
- No FOMO countdown on whale tier (premium audience self-paces; FOMO erodes trust)

### Lesson 4 — Conversion Realism, Not Best-in-Class
Original BPC plan modeled BP at 28% MAU implied conversion (= $4.99 ARPU × 28% / 30 days × 30-day BP cycle). **Round 3 validation reset to 10% MAU conversion** (industry-typical for cozy F2P at $4.99). Ad-free pass blended reset 7% best-in-class → 4% realistic. **Plan with realistic conversion; track stretch separately. Stretch is not a base case.**

---

## Pity System Implementation Notes

```csharp
// Per-banner state (NOT persistent across banners)
struct BannerPityState {
    int pullsSinceLastFeatured;   // resets on featured pull
    int pullsSinceLast4Star;      // resets on 4-star+ pull
    bool guaranteedNextFeatured;  // set when featured won, ensures 50/50 fairness
}

// Soft pity: at pull 70+, increase featured rate by 6% per pull
float GetSoftPityRate(int pulls) {
    if (pulls < 70) return BASE_FEATURED_RATE;
    return BASE_FEATURED_RATE + (pulls - 70) * 0.06f;
}
```
