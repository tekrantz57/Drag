# Drag Strip TODO

## Track sensor validation

- Raise the lane 1 optical paths so normal car guide flags can interrupt them
  despite the 3/4-inch track deck. Evaluate shallow routed mounting pockets,
  longer emitter/detector leads, or both, while retaining guide clearance and
  rigid alignment.
- After the mounting-height correction, run moving-car tests on lane 1 A0-A3
  and record the raw pulse widths reported by Sensor Test at representative
  speeds. Cardboard interruptions have already validated all four sensors and
  produced complete passes.
- Revisit the current 2 ms debounce only after pulse-width measurements are
  available. A pulse shorter than 2 ms is recorded by diagnostics but is not
  accepted as a race sensor transition.
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
  diagnostic inputs and change-only monitoring currently uses 6,656 bytes,
  about 81% of the Mega's 8 KB SRAM.

## Possible future controller

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

## Remaining controller firmware update bench tests

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
- Test at least one official Mega 2560 and one compatible clone if available.
- Run Sensor Test and a physical pass after the first in-app firmware update.
