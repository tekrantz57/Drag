# Experimental Arduino Due Race Firmware

## Status and shared code

The same `dragMC/dragMC.ino` now builds for the Mega 2560 or Arduino Due. Tree
sequencing, race rules, starter controls, serial checksums/commands, diagnostics,
and result delivery are shared. `BoardProfile.h` supplies the pin map and identity;
`DueBeamSensor.h` supplies Due-only sensor interrupt capture. The Mega retains
its polling implementation and original pin assignments.

The Due build identifies as `DRAG_MC_DUE`, version `0.1.2`, protocol `11`, MCU
`SAM3X8E`. It is an experimental bench port, not a qualified race timer. A
successful compile and simulated interrupt tests do not establish physical
accuracy, minimum detectable pulse width, electrical compatibility, or four-lane
reliability. Tree output scheduling still runs in the main loop and must also
be validated. The older `dragMCDueLightTest` remains a separate light-only sketch.

## Electrical precautions

- Due GPIO is 3.3 V and not 5 V tolerant. Begin with one sensor module powered
  at 3.3 V. With DO disconnected from the Due, measure DO-to-GND clear and
  blocked before connecting it. Use a common ground. The current sensors are
  active HIGH when blocked; confirm this on the actual module.
- If a sensor needs 5 V power, use a reviewed level interface. These LM393
  modules can have both a pull-up and an indicator-LED path to VCC; replacing
  only the pull-up does not necessarily isolate a 5 V supply.
- Do not leave interrupt-enabled inputs floating. Fit external 10k pulldowns on
  enabled but unwired active-HIGH sensor inputs. Version 0.1.2 starts with all
  sensor interrupts detached; inactive lanes and uninstalled intervals stay
  detached. Floating enabled inputs can still produce an interrupt storm.
  The noise guard quarantines a pin after 32 interrupts without a main-loop snapshot.
  It reports `ERROR:SENSOR_CAPTURE_NOISE:LANE:<n>:SENSOR:<name>` once per
  affected input; its capture remains disabled until Reset Counts or reboot.
  Correct the wiring before resetting. A required input with a capture fault
  prevents staging/race capture for that lane; an unfinished pass times out.
  Grounding unused sensor inputs is also suitable for an isolated bench setup;
  never ground an output pin or a connected module's driven output.
  Remove temporary bench pulldowns when connecting a module unless its loaded
  output levels have been verified: a weak module pull-up and a pulldown can
  form a voltage divider and leave HIGH too low for reliable recognition.
- Do not assume the Due's 3.3 V regulator can supply 24 modules. Measure current
  and budget the supply before expanding. Do not join independent supply outputs.
- Tree pin numbers are preserved, but 3.3 V output drive/current limits differ
  from the Mega. Review LED resistor values and use suitable transistor/driver
  stages for assembled trees. Do not transfer a 5 V-powered interface blindly.

## Sensor pin map

These are digital signal connections, including pins labelled `A` on the board.
The optional interval pair remains disabled in race configuration by default.

| Lane | Pre-stage | Stage | Interval 1 | Interval 2 | Speed trap | Finish |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | A0 | A1 | D2 | D3 | A2 | A3 |
| 2 | A4 | A5 | D4 | D5 | A6 | A7 |
| 3 | A8 | A9 | D6 | D7 | A10 | A11 |
| 4 | D50 | D51 | D8 | D9 | D52 | D53 |

Only the four lane 4 inputs formerly on Mega A12-A15 move. Do not use the Due's
DAC pins as substitutes. D0/D1 are reserved for the Programming USB UART.

## Tree pin map

| Lane | Pre-stage | Stage | Amber 1 | Amber 2 | Amber 3 | Green | Red |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | D22 | D23 | D24 | D25 | D26 | D27 | D28 |
| 2 | D29 | D30 | D31 | D32 | D33 | D34 | D35 |
| 3 | D36 | D37 | D38 | D39 | D40 | D41 | D42 |
| 4 | D43 | D44 | D45 | D46 | D47 | D48 | D49 |

## Capture behavior

Selected sensor inputs use CHANGE interrupts. An ISR records the sampled level,
`micros()` timestamp, blocked-edge counter, and latest completed pulse width.
It performs no serial output, string formatting, speed calculation or race
decision. The main loop takes short interrupt-protected snapshots, restoring
the previous interrupt mask afterward.

Trap, finish, and installed interval timers use raw ISR crossing timestamps.
The first blocked and unblocked edges since the last main-loop snapshot are
retained even if an entire pulse happens between snapshots. Additional chatter
still increments diagnostics, but does not replace that first crossing. This is
not an unlimited event history: diagnostics report cumulative counts and the
latest pulse width, not every individual pulse. Race logic latches its first
crossing once per pass.

Pre-stage/stage retain 2 ms stable-state qualification, using the ISR timestamp
for accepted transitions. Unlike the Mega, Due interval timers have no 2 ms
qualification delay. ISR timestamps reflect interrupt service time, not hardware
input-capture latches; interrupt latency, simultaneous edges, sensor filtering,
and main-loop Tree scheduling still require measurement.

Read Scan Timing continues to measure main-loop scan-start spacing and snapshot
duration. On the Due it is **not** a measurement of ISR capture latency.

## Capture scope

- Idle startup has no sensor interrupts enabled. `ARM` (or legacy `RESET`)
  enables only the heat's participating lanes and configured installed sensors:
  four per lane, or six with interval timers. Re-arming discards pending edges,
  samples a fresh baseline without incrementing counts, and requalifies stage
  levels. A beam already blocked at enable is not a new downstream crossing.
- Sensor Test has independent lane checkboxes, defaulting to lane 1. The saved
  diagnostic selection does not change race participants. It captures the four
  required inputs plus installed intervals for each selected lane. No selected
  lanes means no diagnostic capture. Configure interval installation in Race
  Settings before testing those inputs.
- An armed race takes priority over diagnostic selection. Finishing a race
  releases its scope; an active monitor lease can then resume its selected lanes.
  Closing Sensor Test or expiration of its 3-second lease removes diagnostic
  capture, but never disconnects sensors needed by an armed race.
- Sensor frames include `CAPTURE:0|1`. Disabled or noise-quarantined inputs are
  shown as Capture disabled rather than a fresh clear reading. Unselected lanes
  are marked Not monitored. Changing scope does not clear cumulative counts or
  recover noise faults; correct wiring and use Reset Counts for fault recovery.
- Legacy clients can still send unscoped `SENSOR_MONITOR:START`, which monitors
  all lanes' installed sensors. Older firmware does not support physical scope
  selection; DragWin shows its legacy all-lane behavior and disables checkboxes.

The Mega keeps polling all inputs; its protocol 11 update limits diagnostic
traffic to the selected lanes, not physical input sampling.

## Build and upload

Open `dragMC/dragMC.ino` in Arduino IDE and select **Arduino Due (Programming
Port)** from Arduino SAM Boards. Use the Micro-USB Programming connector nearest
the barrel power jack. Serial protocol speed is 115200. The initial bench board
uses COM21 on the current bench PC. COM24 was the Native port; verify the
current port before uploading. The Elegoo Mega clone is on COM26.

Equivalent commands from the repository root:

```powershell
arduino-cli compile --fqbn arduino:sam:arduino_due_x_dbg --output-dir artifacts/due-firmware dragMC
# Only after checking wiring, board type, port, and closing DragWin/Serial Monitor:
arduino-cli upload --fqbn arduino:sam:arduino_due_x_dbg --port COM21 --input-dir artifacts/due-firmware dragMC
```

Current DragWin source also supports **File > Update Controller Firmware** for
the Due Programming port. It selects the separate Due `.dragfw` BIN package,
uses Arduino SAM's bossac uploader, performs the 1200-baud Programming-port
reset/erase, writes/verifies/reboots, then checks the Due identity and version.
The full Due race and older light-test identities select the same Due race
package; an update replaces the light-only sketch. Unknown sketches require
explicit manual board selection. Native USB updating is not supported.
See [Controller Firmware Updates](CONTROLLER_FIRMWARE_UPDATE.md).
Never upload the Mega HEX or run the Mega avrdude updater against the Due.
On October 20, 2026, the operator successfully flashed both the Due and an Elegoo
Mega through DragWin on native Windows. Due/bossac under Wine and deliberate
failure/recovery tests remain unqualified; successful uploading does not establish
sensor timing accuracy.

Rebuild DragWin for experimental Due starter support. Both automatic and manual
starter-controlled races use the shared commands; the light-only Due sketch is
still not starter/race capable. Select lane 1 only for the first bench pass.

On October 21, the operator reported that the practice-window and tournament
starter buttons work as intended. This confirms those operator workflows, not
moving-car timing or simultaneous four-lane capture performance.

## Bench validation sequence

1. Check the supply, common ground, and disconnected sensor DO voltage with a
   meter. Ensure other inputs in the selected lane are held at a defined clear
   level; unselected lanes can remain disconnected with interrupts off.
2. Upload the Due-target build only when ready, then connect DragWin and verify
   `DRAG_MC_DUE`, version 0.1.2, protocol 11, MCU SAM3X8E in the serial log.
3. Use Sensor Test, reset counts, and verify repeated blocks and clears on each
   connected input. Record pulse counts and widths. The meter cannot qualify
   short pulses; use repeated physical tests and, eventually, reference timing.
4. Test the Tree separately, then a single-lane staged practice pass. Test both
   automatic and Starter Button operation, rollout cancellation and red lights.
5. Validate optional intervals and multi-lane capture, chatter, missing sensors,
   timeout/DNF, communications load and result delivery before venue reliance.

Host coverage is in `tools/firmware-tests/due.cpp`: four-lane short pulses
entirely between snapshots, first-crossing retention, counts/pulse widths,
staging qualification and timestamps, clock wrap, reset, identity, pin map,
and shared starter/result-delivery tests. CI also compiles both board targets.
