---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: editor
protected: false
---
# Library API — DOTSInventory.Loot

Reusable loot and rarity utilities in `com.the1studio.dots-inventory`. Use these before writing custom weighted-roll code in a demo.

---

## RarityRoller — Burst-Safe Weighted Rarity Roll

**Location**: `Packages/unity-dots-library/com.the1studio.dots-inventory/Runtime/Loot/RarityRoller.cs`

**Namespace**: `DOTSInventory.Loot`

```csharp
[BurstCompile]
public static class RarityRoller
{
    public const byte InvalidTier = byte.MaxValue;   // sentinel: bad input or empty blob

    // Primary entry point — honours pity threshold.
    [BurstCompile]
    public static byte Roll(
        ref BlobAssetReference<RarityWeightsBlob> weightsRef,
        ref Random rngRef,
        int drawsSinceMinTier,  // counter of consecutive draws below the pity floor
        int pityThreshold,      // pass <= 0 to disable pity entirely
        byte minTierIndex);     // tier index that pity forces (and resets the counter)

    // Unrestricted weighted roll across all tiers (no pity check).
    [BurstCompile]
    public static byte RollWeighted(
        ref BlobAssetReference<RarityWeightsBlob> weightsRef,
        ref Random rngRef);

    // Pity path: restricted roll at minTierIndex-or-higher.
    [BurstCompile]
    public static byte RollAtLeast(
        ref BlobAssetReference<RarityWeightsBlob> weightsRef,
        ref Random rngRef,
        byte minTierIndex);
}
```

### Semantics

- **Return value**: `byte` tier index, `0` = first/lowest tier, ascending. Returns `InvalidTier` (`byte.MaxValue`) when blob is unset, empty, or total weight is zero.
- **Pity**: when `drawsSinceMinTier >= pityThreshold` AND `pityThreshold > 0`, `Roll` delegates to `RollAtLeast`. Caller is responsible for tracking and resetting the counter.
- **Blob format**: `RarityWeightsBlob.Weights[i]` = weight for tier `i`. `TotalWeight` is pre-summed. Builder validates positive total at bake time.

### Companion types

```csharp
public struct RarityWeightsBlob
{
    public BlobArray<float> Weights;
    public float TotalWeight;
}

// Singleton component for runtime lookup via SystemAPI.GetSingleton
public struct RarityWeightsBlobRef : IComponentData
{
    public BlobAssetReference<RarityWeightsBlob> Reference;
}
```

### Typical usage

```csharp
// In a Burst-compiled system:
ref var blobRef = ref SystemAPI.GetSingleton<RarityWeightsBlobRef>().Reference;
var shopState = SystemAPI.GetSingletonRW<ShopState>();
byte tier = RarityRoller.Roll(
    ref blobRef,
    ref shopState.ValueRW.Rng,
    drawsSinceMinTier: shopState.ValueRO.DrawsSinceRare,
    pityThreshold: 20,
    minTierIndex: 2);   // 0=Common, 1=Uncommon, 2=Rare, 3=Epic, 4=Legendary
if (tier == RarityRoller.InvalidTier) return; // blob not baked
```

### When to use

Any loot or shop system needing weighted, pity-aware rarity selection. Prefers this over ad-hoc `Random.NextFloat` comparisons — weights stay in designer-editable blob config, no inline literals.

---

## Demo wrappers (before library promotion)

**BPC `BackpackBattlefield.Shop.RarityRoller`** (`Assets/Demos/RPG/BackpackBattlefield/Runtime/Shop/RarityRoller.cs`) was the original game-specific wrapper that returned `BackpackBattlefield.Registries.Rarity` (an enum cast over `byte`). The library version generalises this to plain `byte` tier index so any demo's rarity scale fits without the enum dependency.

The BPC wrapper is now superseded by the library class. New demos (RushTankDemo 4-tier rarity, future projects) should use `DOTSInventory.Loot.RarityRoller` directly with a per-project enum cast at the call site.

**Migration note (BPC)**: BPC's `ShopOfferGeneratorSystem` calls `RarityRoller.Roll(ref blobRef, ref rng, drawsSinceRare, pityThreshold)` with a 4-parameter signature (no `minTierIndex`). The library version adds `minTierIndex` as the fifth parameter. Update callers when promoting.

---

## Gotchas

- `RarityWeightsBlob` must be baked by an authoring component's Baker — do NOT build it in `OnUpdate` (structural change). Use `baker.AddBlobAsset()`.
- `Roll` passes `weightsRef` by `ref` (Burst requirement for blob structs) — ensure the blob is created before the first shop open (baking gate via `RequireForUpdate<RarityWeightsBlobRef>()`).
- `TotalWeight` is pre-computed in the blob — do NOT recompute it in tight loops; read `blob.TotalWeight` directly.
