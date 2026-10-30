# Drag Strip TODO

## Track sensor validation

- Raise the lane 1 optical paths so normal car guide flags can interrupt them
  despite the 3/4-inch track deck. The venue owner does not want routing. Prototype
  extended leads on the original interrupter and remount it on the underside of
  its PCB, reconnecting the mirrored footprint by electrical function. Retain
  guide clearance, insulated leads, mechanical support, and rigid alignment.
- After the mounting-height correction, run moving-car tests on lane 1 A0-A3
  and record the raw pulse widths reported by Sensor Test at representative
  speeds. Cardboard interruptions have already validated all four sensors and
  produced complete passes.
- Validate firmware 0.6.7 at the venue. October 19 tests captured finish pulses
  around 1.4-2.2 ms that were rejected by the old qualification; the physical
  speed-trap signal also missed raw detection on a powered pass. Source now
  captures trap/finish on the first sampled raw edge, leaving staging and interval
  debounce unchanged. Record scan-start gaps and scan durations after each test;
  use a scope/logic analyzer if raw edges are still missing.
- Decide whether polling remains sufficient after comparing repeated physical
  passes with the edge counts. Consider pulse-stretching hardware or port-level
  pin-change interrupts only if repeatable passes are missed or pulses approach
  the polling interval; the full A0-A15 map cannot use ordinary external
  interrupts uniformly.
- Measure and configure the exact A2-to-A3 sensing-plane distance used for the
  speed calculation. The overall track length is currently metadata only.
- During lane 1 bring-up, select only lane 1 as a heat participant and install
  10k pulldowns on disconnected active-HIGH inputs that are being monitored.
- Physically install and validate the optional interval-timer pairs on D2-D9.
  Compare moving-car edge counts and pulse widths before relying on the times.

## Polling-loop timing investigation

- Use the standalone `dragPulseTest` generator on the spare Due to measure Mega
  capture counts at repeatable pulse widths and varied spacing, then reverse
  roles to test Due capture. The sketch supports four near-simultaneous channels;
  use the required voltage interface and procedure in `dragPulseTest/README.md`.
  Compile/host tests are not physical timing qualification. Record a baseline
  before optimizing, and use independent wire timing measurements when available.
  ESP32-C3 Super Mini support also lets both race boards retain their firmware
  while testing one receiver at a time; C3 direct/NPN wiring and pin mapping are
  documented in the same guide. Software and C3 RMT bench results, including
  Mega and Due comparisons, are in `docs/BENCH_PULSE_RESULTS.md`. Independent
  calibrated wire timing and physical multi-lane race validation remain pending.
- Profile the individual sections between sensor scans, separating ordinary
  race operation from Sensor Test, heartbeat, and command-processing spikes.
  October 19 Sensor Test measurements reached about 2.7 ms between scan starts,
  with about 0.4 ms spent scanning; these are observed maxima, not guaranteed
  bounds or measurements of every loop.
- Consider a compile-time option to omit optional timing instrumentation,
  including the micros() calls used for scan-gap and scan-duration maxima.
  Measure the benefit and SRAM/flash impact; preserve race timestamps and
  required sensor behavior. Explicitly report unavailable diagnostics rather
  than misleading zero values, and retain an instrumented build for diagnosis.
  Compare capture with an independent measurement method when instrumentation
  itself is disabled.
- Tree output state caching is implemented experimentally. Focused tests pass;
  Sensor Test comparisons did not show a clear capture improvement. Retain
  light-test ownership, reset behavior, and prompt staging indication while
  evaluating real race-mode behavior.
- Mega 0.6.10 experimentally skips nonparticipating lanes and disabled interval
  inputs only during an active race with Sensor Test closed. Full polling
  returns when Sensor Test is open or the race is not active; excluded inputs
  do not accumulate complete counts during the skipped period. Focused host
  tests and Mega/Due builds pass; hardware comparisons against 0.6.9 are next.
  Preserve all needed inputs, including finish when trap was missed.
- After measuring the conservative scan, consider the further option of
  scanning only the next needed input in each lane. Account for stage release,
  optional intervals, missing or out-of-order pulses, finish independent of
  trap, first-edge timing, raw diagnostic counts, and multiple lanes. This can
  invalidate repeated-pulse raw-count benchmarks if a sensor is no longer
  scanned after its first edge; design a representative race-event test first.
- Measure formatting/checksum cost and consider deferring or budgeting sensor
  diagnostic preparation and expensive command work between scans. Serial output
  is already queued and limited to available transmit-buffer space; do not
  assume wire transmission itself accounts for the measured gap.
- Mega 0.6.9 experimentally defers heartbeats during the critical window,
  preserving them during manual-start wait and resuming on race exit. A matching
  DragWin build labels the link unverified while quiet. Bench runs and a manual
  timeout showed resume; validate aborts, automatic start, disconnect behavior,
  firmware-update safeguards, and varied race conditions. The observed
  before/after sample is encouraging but does not establish causation.
- Consider dedicating four Mega external-interrupt pins to finish sensors.
  Check pin conflicts and remapping (D2/D3 currently serve lane 1 intervals),
  wiring and compatibility, ISR overhead and SRAM. This does not protect the
  speed trap or stage-release capture; compare with polling and the Due.
- Inspect the compiled Mega state-machine dispatch and measure whether a
  function-pointer dispatch table would help. The compiler may already use an
  efficient jump table; an indirect call also has a cost. Avoid changing the
  state machine unless measured gains justify the added complexity.
- The experimental DragWin ordered receive queue reduces serial callback work.
  Three live-monitor stress runs showed no checksum errors; verify longer runs,
  overload reporting, reconnects, and critical race-result delivery before
  claiming that the original corruption is resolved.
- Compare scan-gap distributions and repeated short-pulse capture before/after
  changes, including all four lanes and diagnostic traffic. Keep changes scoped
  and regression-test Tree timing, first-edge latching, and result delivery.

## Dial-In Workflow

- Per-pass practice dial-ins are editable alongside selected main-window lanes
  before arming. They return to saved lane defaults after a pass is armed;
  actual pass results retain the values sent. Verify on a controller and in
  two- and four-lane practice. The practice results window opens after arming.
- Make the tournament runner's existing editable `Dial` column more prominent;
  its per-run override already leaves the car's saved default unchanged. Show
  which racer/car and physical lane each value applies to.
- Confirm pass/heat overrides and sent, stored, and reported values agree in
  physical practice and tournaments. Do not silently change saved lane or car
  defaults. Practice inputs are disabled for heads-up mode; validate equivalent
  tournament behavior and two- and four-lane workflows.

## Venue rule decisions

- Decide whether to enable a staging timeout after one or more lanes stage.
  The current controller waits indefinitely until every participating lane is
  staged, then uses the operator-configured staged delay.
- Decide whether actual venue racing will always use the default
  `BOTH_BLOCKED` staging mode. The optional `IN_ORDER` mode latches pre-stage
  for manual testing, while the stage beam must still remain blocked through
  the staged delay.
- Confirm whether a bye car must stage and take the Tree cleanly. The current
  tournament model guarantees its advancement even after a red light or DNF.
- Decide how a four-lane heat should fill a second advancing position when
  fewer than two cars receive controller placements. DNFs are intentionally
  unplaced because the current sensor set cannot order them defensibly.

## Interval timers

- Confirm the physical locations of Interval 1 and Interval 2 for each lane
  and record their distances from stage for later speed/acceleration analysis.
- Reassess Mega SRAM after physical testing. Firmware with all 24 debounced
  diagnostic inputs, starter control, scan timing, and scoped monitoring currently uses 6,760 bytes,
  about 82% of the Mega's 8 KB SRAM (firmware 0.6.8).

## Possible future controller

- Validate the experimental shared-sketch Arduino Due port documented in
  `docs/DUE_PORT.md` as an available alternative to waiting for an ESP32-S31
  board. Its 84 MHz 32-bit CPU and 96 KB SRAM provide substantially
  more processing/memory headroom than the 16 MHz, 8 KB Mega. Measure actual
  timing rather than extrapolating scan gaps from the clock ratio. Prefer short
  sensor ISRs that capture timestamps/state, with reporting in the main loop.
  The older `dragMCDueLightTest` remains light-tree only; the shared `dragMC`
  sketch now includes experimental Due race/ISR support with lane 4 inputs on
  D50-D53 instead of Mega A12-A15. Validate safe 3.3 V sensor/Tree interfacing,
  defined levels on unused ISR inputs, repeated passes, four-lane load, and
  physical timing accuracy. Native Windows in-app flashing succeeded on the Due
  and Elegoo Mega on October 20, 2026. Deliberate hardware failure/recovery checks
  remain; separately test Wine (Mega updater success does not qualify bossac).
  Never connect 5 V sensor outputs directly
  to Due inputs or flash the Mega image onto it. Faster capture cannot fix a
  guide flag that does not interrupt the beam.
  References: [Due specifications](https://docs.arduino.cc/hardware/due),
  [Mega specifications](https://docs.arduino.cc/hardware/mega-2560).
- Revisit the ESP32-S31 after mature development boards or suitable carrier
  boards are available. Espressif's preliminary ESP32-S31-WROOM-1/-1U and
  WROOM-3/-3U documentation exposes 54 general-purpose GPIO signals; six of
  the chip's 60 GPIO are consumed by module flash connections. USB D+/D- use
  separate dedicated pins. The current Drag design requires 52 direct GPIO
  connections (24 sensor inputs and 28 Tree outputs), so these modules could
  support the existing one-signal-per-pin design with two GPIO remaining.
- Confirm that the eventual board actually brings out all 54 module GPIO.
  GPIO36, GPIO37, GPIO60, and GPIO61 are reset strapping pins, GPIO54-GPIO57
  can provide JTAG, and GPIO58-GPIO59 default to UART0. These pins can become
  regular GPIO after reset, but connected circuits must not interfere with
  boot. Prefer using strapping pins for high-impedance Tree-driver inputs
  rather than sensor signals that could impose a level during reset.
- If evaluating an ESP32-S31 board, give the 24 timing-critical sensor inputs
  priority for direct interrupt-capable GPIO. Consider moving the 28 Tree
  outputs to suitable drivers, shift registers, or output expanders to reduce
  pin pressure and provide better electrical isolation.
- Verify the final board schematic, pin restrictions, interrupt support, and
  Arduino/ESP-IDF toolchain maturity before starting a port. Provide level
  adaptation for any LM393 sensor output that can reach 5 V because ESP32-S31
  GPIO uses 3.3 V logic.

## Firmware availability notice

- Implemented October 26, 2026 in current source: offline, board-specific
  comparison with validated bundled firmware; a persistent main-window notice
  shows installed/available versions and opens the existing updater. Numeric
  comparison, demo/disconnect suppression, and no guessed offers for unknown
  versions/products or missing/invalid packages. Updates remain manual and are
  guarded during known armed/running passes. See `docs/CONTROLLER_FIRMWARE_UPDATE.md`.
- Bench-check the notice with an older physical controller, complete the normal
  update, and confirm it disappears after reconnect. Host coverage includes
  comparisons, corrupt/missing packages, race guards, and notice UI states.

## Remaining controller firmware update bench tests

- Completed October 20, 2026: the operator successfully flashed the Due and
  Elegoo Mega through DragWin on native Windows. October 21: practice-window
  and tournament starter buttons were reported working as intended. These
  successes do not qualify at-speed capture, four-lane operation, or Due/bossac
  under Wine.
- Completed July 30, 2026: a self-contained x64 DragWin publish ran under Wine
  11 on Intel Linux with `/dev/ttyACM0` linked as `COM33`; DragWin downloaded
  its verified Windows avrdude package and successfully flashed the bundled
  firmware. The separate Windows ARM64 publish also ran natively under ARM64
  Wine 11 on a Rock 5B without x64 emulation or CPU translation for DragWin
  itself; using `COM1`, its pinned Windows 32-bit avrdude also ran under Wine
  and successfully updated the controller.
- Recover a Mega running a harmless different sketch through the manual board
  confirmation path.
- Test upload cancellation/failure behavior by selecting an unused COM port
  and by disconnecting USB before upload. Do not deliberately remove the
  bootloader.
- Extend updater coverage to available official Mega 2560 hardware; the Elegoo
  Mega clone's native Windows upload has succeeded.
- Run Sensor Test and a physical pass after the first in-app firmware update.
