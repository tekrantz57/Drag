# Handoff Notes

This repository is the slot car drag strip project now backed up privately on
GitHub at `https://github.com/tekrantz57/Drag`.

## Repository State

- Local path before handoff: `C:\Users\krantz\Documents\Arduino\Drag`.
- Branch: `master`.
- Remote: `origin` -> `https://github.com/tekrantz57/Drag.git`.
- Commit history was rewritten before the first GitHub push so commits use the
  GitHub noreply address instead of a private Gmail address. Older local notes
  may mention pre-rewrite hashes such as `12862cf`; the equivalent current
  commit is `04ca049 Add sensor test UI`.
- Latest handoff baseline before this note:
  - `c1dc737 Note reference Mega sensor test rig`
  - `c15ad5e Document optional sensor board resistor mod`
  - `cda4ab0 Document sensor wiring pulldowns`
  - `71df19d Add project documentation`
  - `04ca049 Add sensor test UI`

## Project Shape

- `dragMC`: Arduino Mega 2560 firmware.
- `dragWin`: .NET 10 WinForms operator app.
- `dragWin/dragWin.ProtocolTests`: console-style protocol and planning tests.
- `README.md`: project overview and normal build/run commands.
- `HARDWARE.md`: controller, sensor polarity, wiring, pin maps, and hardware
  field notes.
- `dragWin/PROTOCOL.md`: serial protocol reference.
- `TODO.md`: field validation and follow-up engineering work.

## Current Hardware Assumptions

- Controller is an Arduino Mega 2560.
- USB serial runs at 115200 baud.
- LM393 slot sensors are active-HIGH:
  - blocked beam reads `HIGH`
  - clear beam reads `LOW`
- Firmware has `SENSOR_IS_ACTIVE_LOW = false`.
- Current six-input sensor map:
  - Lane 1: A0 pre-stage, A1 stage, D2 Interval 1, D3 Interval 2, A2 speed trap, A3 finish
  - Lane 2: A4, A5, D4, D5, A6, A7
  - Lane 3: A8, A9, D6, D7, A10, A11
  - Lane 4: A12, A13, D8, D9, A14, A15
- The two interval timers are optional per lane and default to not installed.
  Disabled interval inputs are ignored by race readiness and diagnostics, so
  unwired pins cannot prevent a race from starting.
- Unwired active-HIGH Mega inputs float, so disconnected pins can randomly show
  blocked.
- Planned sensor wiring assumes Ethernet twisted pair:
  - pair each sensor `D0` with a ground return
  - connect sensor `VCC` and sensor `GND` back to controller power/ground
  - add a 10k pulldown from each Mega input node to ground
- James Cleave shared two useful field references:
  - a similar Mega-based LM393 sensor test rig
  - an optional sensor-board modification for white/yellow guide flags where a
    small SMD resistor near the slotted opto sensor is replaced or bypassed
    with a through-hole 330 ohm resistor
- Treat the 330 ohm board modification as a note to preserve, not as a default
  build step until the exact board revision and resistor position are verified.

## Current Track Testing

- Track wiring/testing has started with real LM393 sensors installed at the
  track.
- On July 25, 2026, lane 1 was fully wired with A0 pre-stage, A1 stage, A2
  speed trap, and A3 finish. All four inputs were validated in Sensor Test,
  and several complete end-to-end passes were recorded by moving cardboard
  through the beams.
- A car could not yet be used for the pass tests because the track deck is
  3/4 inch thick and the car's guide flag does not extend far enough below the
  track to reach the current optical beam height. The planned correction is
  to route shallow mounting pockets, use longer emitter/detector leads to
  raise the optical path, or combine both approaches.
- The cardboard passes validate the lane 1 wiring, sensors, controller, serial
  protocol, and Windows pass workflow. Moving-car pulse-width validation is
  still pending until the optical path is raised into the guide flag's reach.
- The software and firmware now support two optional interval timers between
  stage and the speed trap in each lane. Physical installation and moving-car
  validation are still pending.

## Current Software Features

- Firmware supports heads-up and bracket racing.
- Firmware supports Full and Pro Tree timing, plus a configurable staged delay.
- Firmware supports two optional interval timers per lane. Interval 1,
  Interval 2, and speed-trap crossing times are cumulative from launch; the
  app and exported reports also calculate the three segment times.
- Firmware version `0.6.1` supports `IDENTIFY`, allowing DragWin to request a
  fresh structured `HELLO` frame after flashing.
- Controller-issued placements are authoritative for close finishes and
  four-lane advancement; negative reaction times are reported before fouls.
- Active lane count can be 2 or 4; 2-lane mode uses physical lanes 1 and 4.
- `SET:HEAT_LANES` lets tournament/practice heats use selected physical lanes.
- Serial protocol uses colon-delimited printable ASCII frames with XOR
  checksums.
- `EVENT` and `RESULT` frames include `SEQ` and controller `MS` metadata.
- Windows app includes:
  - serial connection, ping/status/reset
  - manual practice setup and demo practice runs
  - tournament setup and runner
  - ordered lane choice after round one
  - local SQLite tournament storage
  - tournament reports
  - optional Windows SAPI announcements for lineups, lane choices, advancing
    cars, tournament results, and practice results
  - **File > Update Controller Firmware...**, with a validated bundled Mega
    package, direct avrdude upload, reconnect, and DragMC identity verification
  - Sensor Test window that displays all required sensors and enabled interval
    timers for all four lanes, including raw blocked-edge counts and last
    blocked-pulse widths
- Sensor Test polls current state but obtains edge counts and pulse widths from
  firmware-maintained diagnostics, so short pulses do not need to coincide
  with a Windows polling request to appear in the diagnostic display.

## Validation Baseline

Last known verification before handoff:

```powershell
dotnet build dragWin\dragWin.sln
dotnet run --project dragWin\dragWin.ProtocolTests\dragWin.ProtocolTests.csproj
```

Both passed after documentation work. The solution build may report the known
SQLite RID warning (`NETSDK1206`) for the test project.

DragMC `0.6.1` compiles for the Arduino Mega 2560 and is packaged as
`dragMC/dist/DragMC-mega-0.6.1.dragfw`. The current build uses about 82% of
SRAM, leaving about 1.4 KB; reassess memory use after
physical 24-sensor testing or before adding more controller features.

## Good First Checks on the New Computer

1. Clone or open the private GitHub repo.
2. Run `git status --short --branch` and confirm a clean `master`.
3. Run the .NET build and protocol tests above.
4. Compile `dragMC/dragMC.ino` for Arduino Mega 2560.
5. Connect the Mega by USB and use the Windows app `Test Sensors` window.
6. With one sensor wired to A0, confirm active-HIGH behavior:
   - clear beam -> LOW / clear
   - blocked beam -> HIGH / BLOCKED

## Open Threads

- Decide whether the 10k pulldowns will live on a controller terminal board,
  per-sensor cable breakout, or another wiring harness.
- Verify whether James Cleave's 330 ohm resistor mod applies to the exact
  sensor boards being used.
- Install and validate Interval 1 and Interval 2 at their final physical
  positions. Confirm cumulative and segment times with a moving car.
- Reassess Mega SRAM headroom and serial queue behavior with all 24 sensors
  enabled and producing diagnostics.
