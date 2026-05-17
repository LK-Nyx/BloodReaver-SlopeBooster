# Slide Booster — Blood Reaver

A BepInEx plugin that enhances slide movement in [Blood Reaver](https://store.steampowered.com/app/2906070/Blood_Reaver/) with configurable speed boosts and air inertia.

> **Built 100% by [X.A.N.A.](https://hermes-agent.nousresearch.com) (Hermes AI Agent)** — from reverse engineering the game's assemblies to final release. The user provided guidance and testing; every line of code was authored by the agent.

## Features

- **Slide speed multiplier** — boost normal slide velocity
- **Jump-slide multiplier** — boost slide velocity during jump slides
- **Air inertia** — slide momentum carries into the air after leaving a slide (bunny-hop style), with configurable fade timing
- **Config file** — all values adjustable in `BepInEx/config/slidebooster.cfg`, no recompilation needed
- **No collision side effects** — uses displacement-based patching (delta on `bodyRef.transform.position`) instead of modifying `velocity`, avoiding gravity/ground-check contamination

## Default Values

| Setting | Default | Description |
|---------|---------|-------------|
| `SlideMultiplier` | 1.2 | Normal slide speed multiplier |
| `JumpSlideMultiplier` | 1.2 | Jump-slide speed multiplier |
| `Duration` | 0.8 | Air inertia duration (seconds) |
| `FadeStartTime` | 0.2 | When inertia starts fading (seconds) |

## Requirements

- **Blood Reaver** (Unity 6 / Mono build)
- **BepInEx 6.x** (Windows x86 variant) — install in game root
- **Proton** (if on Linux) — tested with Proton 9.0+
- .NET runtime (for building from source only)

## Installation

1. **Install BepInEx 6**
   - Download [BepInEx 6 pre-release](https://github.com/BepInEx/BepInEx/releases) (`BepInEx_x86_64.zip`)
   - Extract into the game folder: `SteamLibrary/steamapps/common/BloodReaver/`
   - Verify: `BloodReaver/BepInEx/core/BepInEx.Core.dll` exists

2. **Add Steam launch options** (required for Proton)
   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```

3. **Install SlideBooster**
   - Download `SlideBooster.dll` from [Releases](https://github.com/3L0-gh/BloodReaver-SlideBooster/releases)
   - Place it in `BloodReaver/BepInEx/plugins/`
   - Start the game

4. **Configure** (optional)
   - After first launch, edit `BepInEx/config/slidebooster.cfg`
   - Restart the game to apply changes

## Building from Source

```bash
git clone https://github.com/3L0-gh/BloodReaver-SlideBooster
cd BloodReaver-SlideBooster
dotnet build -c Release
```

Output: `bin/Release/net48/SlideBooster.dll`

The `.csproj` references game assemblies at fixed paths. Update the `HintPath` entries for your system.

## Linux / Proton Notes

- The game runs under Wine/Proton but the .NET assemblies are standard Mono — compilation works on both Windows and Linux
- No special Wine configuration needed beyond the `WINEDLLOVERRIDES` launch option
- BepInEx's `LogOutput.log` and config files are in `BloodReaver/BepInEx/`

## Technical Details

Instead of patching `velocity` (which contaminates gravity and ground-check systems), the plugin uses **MonoMod RuntimeDetour** hooks on `PlayerMovement.SlideCurve()` and an injected **MonoBehaviour** for air inertia:

1. **SlideCurve hook**: captures `bodyRef.transform.position` before/after the vanilla method, multiplies the displacement delta, and adds the surplus. No collision check needed — the game's own `SlideCollisionCheck()` runs inside the vanilla method.

2. **Inertia MonoBehaviour**: a `FixedUpdate`-driven component that applies stored slide momentum during air movement with a linear fade curve.

3. **Why not Harmony?** BepInEx 6 ships HarmonyX (`0Harmony20.dll`) which silently fails to apply patches despite loading without errors. MonoMod RuntimeDetour is already bundled in BepInEx 6 and works reliably.

## Config File

Auto-generated at first launch at `BepInEx/config/slidebooster.cfg`:

```ini
[Boost]
SlideMultiplier = 1.2
JumpSlideMultiplier = 1.2

[Inertia]
Duration = 0.8
FadeStartTime = 0.2
```

## Known Limitations

- **No wall collision check** on air inertia — extra displacement may clip through thin walls
- **Multiplayer**: slide boost is client-side only. In-host-authoritative games, the server may not see boosted speed

## License

MIT
