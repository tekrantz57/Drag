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
- `dragWin`: .NET 9 WinForms operator app.
- `dragWin/dragWin.ProtocolTests`: console-style protocol and planning tests.
- `README.md`: project overview and normal build/run commands.
- `HARDWARE.md`: controller, sensor polarity, wiring, pin maps, and hardware
  field notes.
- `dragWin/PROTOCOL.md`: serial protocol reference.
- `TODO.md`: future intermediate/split sensor ideas.

## Current Hardware Assumptions

- Controller is an Arduino Mega 2560.
- USB serial runs at 115200 baud.
- LM393 slot sensors are active-HIGH:
  - blocked beam reads `HIGH`
  - clear beam reads `LOW`
- Firmware has `SENSOR_IS_ACTIVE_LOW = false`.
- Current sensor map:
  - Lane 1: A0 pre-stage, A1 stage, A2 speed trap, A3 finish
  - Lane 2: A4, A5, A6, A7
  - Lane 3: A8, A9, A10, A11
  - Lane 4: A12, A13, A14, A15
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
- A0 and A1 have been tested at the track for lane 1 pre-stage and stage.
- A2 and A3 are the next expected lane 1 sensors to add/test for speed trap
  and finish.
- The venue will probably add two intermediate/split sensors to each lane,
  increasing the likely future model from 4 sensors per lane to 6.

## Current Software Features

- Firmware supports heads-up and bracket racing.
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
  - Sensor Test window that displays all four sensors for all four lanes,
    including raw blocked-edge counts and last blocked-pulse widths
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

An Arduino Mega compile had previously passed before the repo rename and docs
work. No firmware code has changed since the sensor test/polarity work, but a
fresh Mega compile on the new computer is still a good first check.

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
- Continue investigating possible two intermediate/split sensors per lane.
- If split sensors are added for all four lanes, sensor count rises from 16 to
  24 and the pin map must expand beyond `A0`-`A15`.
