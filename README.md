# rF2 -> DualSense Haptic Bridge

A Windows app that reads rFactor 2 telemetry and drives PS5 DualSense adaptive triggers (L2/R2) and rumble motors via native USB HID. Feel curbs, lockups, wheelspin, impacts, surface texture, and oversteer through your controller.

## What You Feel

| Situation | Triggers (L2/R2) | Rumble |
|-----------|------------------|--------|
| Braking | L2 resistance proportional to brake pressure | - |
| Throttle | R2 light resistance proportional to throttle | - |
| ABS / lockup | L2 rapid pulse (~40Hz) | Chatter on fine motor |
| Wheelspin / TC | R2 pulse (~30Hz) + increased resistance | Buzz on fine motor |
| Curb hit | - | Side-biased burst (left curb = left motor) |
| Road surface | - | Subtle texture that increases with speed |
| Wall impact | Short burst on both triggers | Strong thump, fast decay |
| Oversteer / spin | R2 vibration cue | Escalating chassis shake |
| Engine | - | Very subtle rumble near redline |

All effects are layered with smoothing, noise gates, hysteresis, and soft clipping. Nothing pegs to max or buzzes constantly.

## Requirements

### 1. DualSense controller via USB (recommended)

USB connection is required for full adaptive trigger + rumble fidelity. The app communicates directly with the controller via HID output reports -- no DualSenseX needed.

Supported controllers:
- DualSense (PID 0x0CE6)
- DualSense Edge (PID 0x0DF2)

### 2. rF2SharedMemoryMapPlugin

1. Download from [TheIronWolfModding/rF2SharedMemoryMapPlugin](https://github.com/TheIronWolfModding/rF2SharedMemoryMapPlugin/releases)
2. Copy `rFactor2SharedMemoryMapPlugin64.dll` to:
   ```
   C:\Program Files (x86)\Steam\steamapps\common\rFactor 2\Bin64\Plugins\
   ```
3. In rFactor 2: **Settings > Plugins** > enable the shared memory plugin

### 3. .NET 8 Runtime

Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) if not installed.

## Quick Start

```
cd src
dotnet build
dotnet run
```

Or publish a standalone exe:
```
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

1. Connect DualSense via USB
2. Start rFactor 2 and load into a session
3. Run `rf2-dsx-bridge.exe`
4. Recalibrate your controller in Windows (Settings > Bluetooth & devices > your controller, or run `joy.cpl`)
5. Drive and feel the effects

## Output Modes

| Mode | Set in config | What it does |
|------|---------------|--------------|
| `hid` (default) | `"outputMode": "hid"` | Native USB HID: full triggers + rumble |
| `dsx` (fallback) | `"outputMode": "dsx"` | DualSenseX text file: triggers only, no rumble, vibration falls back to resistance |

**Important:** Do not run DualSenseX simultaneously with HID mode. Two apps sending HID output reports will conflict.

## Configuration (appsettings.json)

```json
{
  "outputMode": "hid",
  "updateHz": 120,

  "brakeDeadzone": 0.02,
  "throttleDeadzone": 0.02,
  "brakeStartPos": 2,
  "throttleStartPos": 1,
  "maxBrakeStrength": 8,
  "maxThrottleStrength": 5,

  "absGripThreshold": 0.85,
  "absTriggerFreqHz": 40,
  "tcGripThreshold": 0.80,
  "tcTriggerFreqHz": 30,

  "oversteerThresholdDeg": 5.0,

  "masterRumbleGain": 1.0,
  "roadRumbleGain": 0.5,
  "curbRumbleGain": 1.0,
  "absRumbleGain": 0.8,
  "tcRumbleGain": 0.6,
  "engineRumbleGain": 0.3,
  "impactRumbleGain": 1.0,
  "spinRumbleGain": 0.8,

  "rumbleSmoothingAlpha": 0.35,
  "rumbleNoiseGate": 0.03
}
```

### Key settings

| Setting | Default | Description |
|---------|---------|-------------|
| `outputMode` | `hid` | `"hid"` for native USB, `"dsx"` for DualSenseX fallback |
| `updateHz` | `120` | Update rate in Hz (60-250) |
| `maxBrakeStrength` | `8` | Max L2 resistance (1-8) |
| `maxThrottleStrength` | `5` | Max R2 resistance (1-8), lighter than brake by default |
| `absGripThreshold` | `0.85` | Grip fraction below which ABS pulse activates |
| `tcGripThreshold` | `0.80` | Grip fraction below which TC pulse activates |
| `absTriggerFreqHz` | `40` | ABS trigger vibration frequency |
| `tcTriggerFreqHz` | `30` | TC trigger vibration frequency |
| `oversteerThresholdDeg` | `5.0` | Oversteer angle (degrees) to trigger spin cues |
| `masterRumbleGain` | `1.0` | Global rumble multiplier (0 = off, 2 = double) |
| `*RumbleGain` | varies | Per-effect rumble multiplier |
| `rumbleNoiseGate` | `0.03` | Below this level, rumble snaps to zero (prevents jitter) |
| `estimatedWheelbaseMeter` | `2.6` | Used for oversteer detection (tune per car if needed) |

## Tuning Tips

- **Too much rumble?** Lower `masterRumbleGain` (e.g. 0.6)
- **Can't feel curbs?** Raise `curbRumbleGain` (e.g. 1.5) and/or lower `curbSuspVelocityThreshold`
- **ABS too sensitive?** Lower `absGripThreshold` (e.g. 0.75)
- **Triggers too stiff?** Lower `maxBrakeStrength` / `maxThrottleStrength`
- **Jitter at idle?** Raise `rumbleNoiseGate` (e.g. 0.05) and/or raise deadzones
- **Oversteer cues wrong car?** Adjust `estimatedWheelbaseMeter` and `estimatedMaxSteerAngleDeg`

## Architecture

```
Telemetry (shared memory) -> TelemetryFrame (per-tick snapshot)
  -> TriggerEffectsEngine (brake resistance, ABS, TC, impact, oversteer)
  -> RumbleEffectsEngine  (road, curb, ABS, TC, engine, impact, spin)
  -> EffectMixer           (EMA smoothing, noise gate, hysteresis)
  -> HidOutputSink         (USB HID report to DualSense)
```

## Behavior

- **No rF2 running:** triggers off, rumble silent, auto-reconnects every 2 seconds
- **Controller unplugged:** auto-reconnects when plugged back in
- **Ctrl+C:** clears all effects before exiting
- **CPU usage:** < 1% typical at 120Hz

## License

This project reads shared memory provided by [rF2SharedMemoryMapPlugin](https://github.com/TheIronWolfModding/rF2SharedMemoryMapPlugin) (GPL-3.0). No plugin code is copied or redistributed. Users must install the plugin separately.
