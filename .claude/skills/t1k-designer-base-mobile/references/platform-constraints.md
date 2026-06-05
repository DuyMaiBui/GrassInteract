---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Platform Constraints

## Battery Budget

| Target | Specification |
|--------|--------------|
| Drain per 30-min session | <5% (flagship), <8% (mid-range) |
| Max CPU usage sustained | 60% average across session |
| Wake lock usage | Only during active gameplay — release in menus |

**Low battery response** (≤20%):
- Reduce particle system max count by 50%
- Disable post-processing (bloom, depth of field)
- Drop target frame rate from 60fps to 30fps automatically
- Show "Low battery mode enabled" toast (transparent, not intrusive)

```csharp
// Unity: detect battery and reduce quality
void CheckBattery() {
    if (SystemInfo.batteryLevel < 0.20f && SystemInfo.batteryLevel > 0f) {
        QualitySettings.SetQualityLevel(0); // lowest preset
        Application.targetFrameRate = 30;
    }
}
```

## Thermal Management

**Adaptive Performance API** (Unity Package: `com.unity.adaptiveperformance`):
- Subscribe to `PerformanceStatus.ThermalMetrics.WarningLevel`
- `PerformanceWarningLevel.NoWarning` → full quality
- `PerformanceWarningLevel.ThrottlingImminent` → reduce particles, shadows
- `PerformanceWarningLevel.Throttling` → drop to 30fps, disable AA, reduce draw distance

**Manual thermal proxy** (if no Adaptive Performance):
- Track `Time.deltaTime` rolling average — if average rises >2ms over 5 min, thermal throttle suspected
- Reduce quality tier proactively before OS forces throttle

**Never unlock frame rate**: `Application.targetFrameRate = -1` is the #1 cause of thermal runaway on mobile.

## Memory Budget Tiers

| Device Class | RAM | Budget | Strategy |
|-------------|-----|--------|----------|
| Low-end | 2-3 GB | 1.5 GB | ASTC compressed textures, atlas everything, pool aggressively |
| Mid-range | 4-6 GB | 3 GB | Standard quality, texture streaming |
| High-end | 8+ GB | 6 GB | High res, advanced effects, large texture budgets |

**Detection**: `SystemInfo.systemMemorySize` — bucket into tiers at startup, load quality preset.
**Addressables**: stream assets by tier — low-end downloads ASTC variants, high-end downloads full-res.
**Texture compression**: ASTC (iOS A8+, Android Vulkan/ES3.1) universally; ETC2 fallback (older Android).

## App Size Constraints

| Store | Initial Download Limit | OTA Limit |
|-------|----------------------|-----------|
| App Store (iOS) | 200 MB (cellular warning at 200MB) | No limit |
| Google Play | 150 MB APK + 2GB OBB | No limit |

**Strategy to stay under limits**:
- Initial bundle: core gameplay only (first 3 levels)
- Stream additional content via Addressables on first launch (Wi-Fi only by default)
- Compress audio: AAC for music (128kbps), OGG for SFX (64kbps)
- Sprite atlases: 2048×2048 max, power-of-two dimensions
- Strip unused Unity modules in Player Settings (reduces base ~5-15 MB)

## Frame Rate Policy

| Mode | Target FPS | Use Case |
|------|-----------|----------|
| Default | 30 | Battery saver, broad device compat |
| Quality (opt-in) | 60 | Player preference setting |
| Menu/UI only | 30 | No need for 60fps in menus |
| Cutscenes | 30 | Pre-rendered feel, saves battery |
| Never | Unlocked | Thermal runaway risk |

```csharp
// Apply in GameManager/SettingsManager
void ApplyFrameRate(bool highQuality) {
    Application.targetFrameRate = highQuality ? 60 : 30;
    QualitySettings.vSyncCount = 0; // disable vSync on mobile
}
```

## Screen Size and Safe Area

**Support range**: 4.7" (iPhone SE) to 6.7" (iPhone Pro Max / large Android)
**Aspect ratios**: 16:9 (legacy) to 19.5:9 (modern tall phones) to 21:9 (ultra-wide Android)

**Safe area insets** (notch, punch-hole camera, home indicator pill):
```csharp
// Apply safe area to canvas RectTransform
Rect safeArea = Screen.safeArea;
Vector2 anchorMin = safeArea.position;
Vector2 anchorMax = safeArea.position + safeArea.size;
anchorMin.x /= Screen.width;  anchorMin.y /= Screen.height;
anchorMax.x /= Screen.width;  anchorMax.y /= Screen.height;
rectTransform.anchorMin = anchorMin;
rectTransform.anchorMax = anchorMax;
```

**Rule**: any interactive UI element (buttons, health bars) must be inside safe area. Decorative backgrounds may extend to screen edges (bleeding under notch is fine).

## Orientation Decision

| Orientation | Suited For | Examples |
|------------|-----------|---------|
| Portrait | Casual, one-hand, commute | Clash Royale, Candy Crush, Backpack Hero |
| Landscape | Action, immersive, two-hand | PUBG Mobile, Genshin Impact |

**Hard rule**: pick ONE orientation per game and lock it.
`Screen.orientation = ScreenOrientation.Portrait` — set at startup, never change at runtime.
**Why**: UI layout for both orientations doubles art/UX work and introduces rotation-lag friction.

## Network Handling

- **Offline-first**: all core gameplay works without network connection
- **Action queue**: if network unavailable, queue server actions locally and sync when online
- **Reconnect**: attempt reconnect every 30s in background — silent, no error spam
- **Timeout**: server requests timeout at 10s — show "Connection slow" not "Error"
- **Content updates**: check for asset bundles on session start (Wi-Fi only by default, user opt-in for cellular)

## Gotchas
- **`Screen.safeArea` not called on `Awake`**: call on `Start` or after canvas initializes — `Awake` may return wrong values on some devices
- **vSync on mobile**: `QualitySettings.vSyncCount` must be 0 — enabling vSync ignores `targetFrameRate`
- **Adaptive Performance not available on all devices**: always null-check the API before subscribing to callbacks
- **OBB size limit**: Google Play OBB max is 2 GB per file (2 files allowed) — plan Addressables hosting for >4 GB content
