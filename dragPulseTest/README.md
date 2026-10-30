# Repeatable Bench Pulse Generator

This standalone sketch runs on **a Mega 2560, Due Programming port, or ESP32-C3
Super Mini**. One receiving board runs normal DragMC firmware and receives pretend sensor
pulses. The generator is NOT DragMC: it does not connect to DragWin, run a Tree,
or update the other board. Uploading it temporarily replaces that board's race
firmware. No wiring or firmware changes are needed in the receiving sketch.

Use this to compare pulse counts and widths, including before/after Mega polling
changes. It does not test the optical sensor itself or certify absolute accuracy.

Version **0.3.1** defaults to **hardware-timed RMT pulse widths on the C3**.
Mega/Due generator builds retain the original software engine. The C3 can also
select that engine explicitly for comparison. See [bench results](../docs/BENCH_PULSE_RESULTS.md)
for the software-generator observations that motivated this change.

## ESP32-C3 Super Mini: Quick Start

The C3 build defaults to **DIRECT**: LOW = clear, HIGH = blocked, at 3.3 V.
Mega/Due generator builds still default to **NPN**, preserving the original
transistor wiring. The idle-only `MODE DIRECT` / `MODE NPN` commands select the
polarity; a reboot restores the board's default. HELP and each RUN identify the
current mode. Match the mode to your physical wiring before resetting counts.
Changing modes can create a receiver edge even while no train is running.

For a **Due receiver**, start with ONE direct channel:

```text
C3 GPIO4 -------- Due A2 (lane 1 trap input; real sensor DO disconnected)
C3 GND ---------- Due GND
```

Keep leads short and power both boards by USB. Do not join their supply outputs.
Verify your board really is an ESP32-C3 with 3.3 V GPIO. Due A2 must be an input
without a pull-up; no real sensor output or other driver may remain connected.
Do not power one board through the other's signal pin. Disconnect the signal
before powering down either board. This is one receiver at a time, not a shared
Mega/Due signal bus. A small series resistor (e.g. 330 ohms) can limit accidental
contention current, but does NOT make a 5 V signal safe for a 3.3 V input.

1. Select **ESP32C3 Dev Module** in Espressif's Arduino ESP32 board package.
   Set **USB CDC On Boot: Enabled**, and select the C3's own COM port. Native
   USB Serial/JTAG uses GPIO18/19, which this sketch leaves alone.
2. Upload to the C3 only. If automatic upload fails, hold BOOT, tap RESET, release
   BOOT, select the enumerated port and retry. After upload, tap RESET if needed
   and reselect the port. Board implementations vary; confirm printed GPIO
   numbers on your actual Super Mini, not header position or assumed Dx aliases.
3. Open Serial Monitor at 115200 with newline endings and send HELP. No Wi-Fi or
   Bluetooth is initialized. GPIO4/5/6/7 are the four channel outputs; GPIO8/9
   (common LED/BOOT and strapping pins), GPIO2, UART20/21, and USB18/19 are unused.
   GPIO4..7 also have external JTAG functions: do not attach a pin-based debugger
   or other peripherals there during this test.
4. Send `MODE DIRECT`, `LEVEL 1 0`, then `LEVEL 1 1`. Check receiver A2 with a
   meter: approximately 0 V and 3.3 V respectively. Send STOP to return to clear.
5. In DragWin select the Due's port, open Sensor Test, select lane 1, define clear
   levels on other enabled inputs, and Reset Counts. Then on the C3 monitor send
   `RUN 2000 20000 1000 997 1`. Expect 1000 trap pulses after DONE. HELP should
   identify version 0.3.1 and engine HARDWARE RMT.

For a **Mega receiver**, use the same C3 output and Mega A2/GND instead, but
prefer `MODE NPN` and the interface below for a dependable voltage margin.
A direct 3.3 V bench signal will often work: the ATmega2560 ordinary-input HIGH
threshold is 0.6 * VCC (3.0 V at VCC = 5.0 V). However, the C3's guaranteed HIGH
output specification can be below 3.0 V under specified loading conditions.
Direct operation is not guaranteed across all conditions. Measure and validate
it if used; marginal voltage can masquerade as missed polling. Never enable a
Mega 5 V input pull-up on a direct C3 connection. Disconnect real sensor DO and
all other drivers. A higher-voltage receiver does not inherently drive its
voltage back through an ordinary input; pull-ups or output configuration can.

Sources: [C3 pin restrictions](https://docs.espressif.com/projects/esp-hardware-design-guidelines/en/latest/esp32c3/schematic-checklist.html),
[USB setup](https://docs.espressif.com/projects/arduino-esp32/en/latest/tutorials/cdc_dfu_flash.html),
[C3 electrical specifications](https://documentation.espressif.com/esp32-c3_datasheet_en.html),
[ATmega2560 electrical specifications](https://ww1.microchip.com/downloads/en/devicedoc/atmel-2549-8-bit-avr-microcontroller-atmega640-1280-1281-2560-2561_datasheet.pdf).

## NPN Electrical Interface

**Never connect a Mega generator's 5 V output directly to a Due input.** The Mega
uses 5 V logic; the Due and ESP32-C3 are not 5 V tolerant. Select `MODE NPN` and
use one NPN interface per test channel, with the collector
pulled up to the **receiving board's** logic supply:

```text
Generator output -- 2.2k -- base   NPN (e.g. 2N3904)
                           |
                          47k
                           |
Both board GND ------------+---- emitter

Receiver logic supply --- 10k ---+--- receiver sensor input
                                 |
                              collector
```

Check the actual transistor's datasheet for emitter/base/collector pin order;
packages and manufacturers can differ. Repeat the circuit for each channel.

- **Mega generator -> Due receiver:** collector pull-up goes to Due **3.3 V**.
- **Due generator -> Mega receiver:** collector pull-up goes to Mega **5 V**.
- **C3 generator -> Due/Mega receiver:** choose `MODE NPN`; collector pull-up
  goes to Due **3.3 V** or Mega **5 V**, respectively. No collector pull-up is
  used for a direct push-pull C3 connection.
- Connect the board grounds. Power each by its own USB. Do NOT connect the two
  5 V or 3.3 V supply outputs together. Make wiring changes with both unplugged.
- Disconnect real sensor DO wires from the inputs being driven. Do not connect
  this circuit to Tree outputs. Remove temporary pulldowns from these receiver
  inputs: they would divide the collector pull-up voltage.
- Generator HIGH turns the transistor on, pulling the receiver LOW (clear).
  Generator LOW releases the collector, making the receiver HIGH (blocked).
  This inversion is built into NPN mode; C3 users must explicitly select it.
- On reset/unpowered generator, the transistor may be OFF and the receiver HIGH.
  Stop the receiver test before resets; establish clear levels before arming or
  resetting counts. Both boards must be powered before testing. Never rely on
  reset behavior for race operation.
- Verify with a meter at the receiver input before enabling capture. Use
  `LEVEL 1 0` for clear, then `LEVEL 1 1` for blocked, then `STOP`. Due input
  must never exceed 3.3 V. Verify LOW/HIGH are valid for the receiving board.
- Keep leads short. Pull-up resistance, wiring capacitance and transistor
  switching add delay. Software-reported widths do not measure those effects.

Voltage references: [Arduino Due datasheet](https://docs-content.arduino.cc/resources/datasheets/A000062-datasheet.pdf),
[Arduino Mega datasheet](https://docs.arduino.cc/resources/datasheets/A000067-datasheet.pdf).

## Pins And First Wiring

Generator **output** pins and suggested receiver inputs:

| Channel | Mega/Due generator | C3 Super Mini generator | Suggested receiver input |
| --- | --- | --- | --- |
| 1 | D22 | GPIO4 | A2, lane 1 trap |
| 2 | D23 | GPIO5 | A6, lane 2 trap |
| 3 | D24 | GPIO6 | A10, lane 3 trap |
| 4 | D25 | GPIO7 | D52 on Due / A14 on Mega, lane 4 trap |

In NPN mode each row goes THROUGH its own transistor circuit, not a direct wire.
For C3 direct mode, use the direct-wiring precautions above. The Mega/Due
generator pins are normally Tree pins in DragMC. Remove any Tree wiring from
the generator. Start with channel 1 only. Optionally move its signal to A3
to test lane 1 finish instead. See [Due map](../docs/DUE_PORT.md) and
[Mega wiring](../HARDWARE.md) before testing other positions.

For Due receivers, Sensor Test must select the lane(s) under test. Enabled,
unused inputs in those lanes still need defined clear levels, per the Due
guide; unselected lanes remain interrupt-disabled. Installed interval inputs
must be enabled in Race Settings to test them.

## Upload And Controls

Open `dragPulseTest.ino` in Arduino IDE, select Mega 2560, **Due (Programming
Port)**, or **ESP32C3 Dev Module** and check which board/port you are replacing. Current bench
ports were Due Programming COM21 and Elegoo Mega COM26; confirm each time.
Close DragWin's connection to the generator before uploading. Leave normal
DragMC on the receiver. Due Native USB is not supported by this sketch.

Compile-only CLI examples (none uploads):

```powershell
arduino-cli compile --fqbn arduino:avr:mega:cpu=atmega2560 dragPulseTest
arduino-cli compile --fqbn arduino:sam:arduino_due_x_dbg dragPulseTest
arduino-cli compile --fqbn esp32:esp32:esp32c3:CDCOnBoot=cdc dragPulseTest
```

Open the generator's Serial Monitor at **115200 baud**, newline line ending.
DragWin uses the OTHER COM port (the receiver). Send `HELP` if startup text was
missed. Commands are uppercase; all numbers are unsigned decimal.

```text
RUN width_us gap_us count jitter_us mask
RUN 2000 20000 1000 997 1
STOP
LEVEL 1 0
LEVEL 1 1
HELP
MODE DIRECT
MODE NPN
ENGINE HARDWARE
ENGINE SOFTWARE
```

- Width is the requested blocked/HIGH duration at the receiver, in microseconds.
  1000 us = 1 ms. Width and base clear gap each allow 100..1,000,000 us.
- Count is 1..1,000,000 completed pulses **per selected channel**.
- Jitter is 0..1,000,000 us, ADDED to each base gap. The fixed-seed pseudo-random
  sequence restarts for each run, making spacing repeatable. It helps avoid
  synchronizing the generator with the receiver's polling loop; it is not an
  exhaustive test of every relative phase.
- Mask selects channels: 1 = channel 1, 2 = channel 2, 4 = channel 3,
  8 = channel 4; add values for combinations (3 = first two, 15 = all four).
  **C3 HARDWARE mode accepts one selected channel only: 1, 2, 4, or 8.**
  Multi-channel masks are rejected, never silently changed to software timing.
  Use `ENGINE SOFTWARE` on the C3 for the original four-output mode.
- RUN clears all channels, flushes console output, then waits one second before
  the first pulse. In software mode selected channels change together, using
  sequential writes; this is near-simultaneous, NOT atomic and not independently
  staggered traffic.
- No console output occurs during generation. Only STOP is acted on while
  running; other commands are ignored. Do not flood the generator with serial
  traffic. STOP clears all channels, including held LEVEL outputs.
- LEVEL is a persistent idle-only meter check, not a pulse train. All unselected
  channels are cleared. STOP is required to return held blocked outputs to clear.
- A completed SOFTWARE run prints counts, per-channel software width/gap ranges, and max
  scheduler lateness. These are software timestamps taken after GPIO writes,
  NOT calibrated receiver-wire timings. The generator never compresses pulses
  to catch up after a late iteration; inspect its actual ranges, not just the
  requested width. An aborted run can have one extra partial blocked pulse that
  the receiver counted but the completed-pulse report excludes.

## C3 Hardware-Timed Widths

`ENGINE HARDWARE` is the C3 default after reset. `ENGINE SOFTWARE` retains the
original loop-driven generator. Engine changes are idle-only and do not change
DIRECT/NPN mode. An ESP32-C3 Super Mini still defaults to DIRECT after reset;
**send MODE NPN again when using the transistor circuit**. Mega/Due builds have
no ENGINE command and remain software-timed. Keep existing transistor wiring.

The RMT peripheral uses 1 MHz resolution (one programmed tick per microsecond).
The complete selected pulse, including the maximum 1,000,000 us width, fits in
the first half of one RMT memory block. Long widths are split into adjacent
same-level segments, not multiple pulses. There is no CPU-driven falling edge
or streaming refill while the pulse is active. The peripheral ends with 2 us of
clear level and holds clear between transactions. GPIO hold is used during
peripheral setup/teardown to avoid mode-handoff glitches. Verify this behavior
on actual hardware; compilation and mocks are not a wire-level measurement.

Software starts each transaction and schedules the following clear gap only
after observing its completion. This means a late task can extend the clear
gap, but does not extend the programmed blocked pulse. The initial one-second
wait and inter-pulse gap are NOT hardware-accurate. This is a pulse-width/capture
test, not a precise-frequency source or full hardware-timed race simulator.

Example, using the existing C3 GPIO4 -> NPN -> Mega A2 interface:

```text
MODE NPN
ENGINE HARDWARE
STOP
RUN 5000 20000 100 997 1
```

Reset the Mega's counts and confirm zero before RUN. After DONE wait two seconds
for diagnostics, then record A2 count and Read Scan Timing. Repeat at 2000 and
1400 us, then longer 1000-pulse runs as appropriate, without changing Mega code.

Hardware summary fields:

- `completed_pulses`: RMT transactions confirmed complete, not a physical loopback
  measurement. In HARDWARE mode only one channel is selected.
- `programmed_width_us`: the width programmed into RMT, NOT a measured min/max.
  Clock accuracy, transistor delay and receiver thresholds still affect wire timing.
- `requested_clear_gap_us`: range of base gap + deterministic jitter requests,
  excluding the first-start wait; n/a for a single pulse.
- `software_rearm_wait_us`: time from software observing completion to requesting
  the next pulse. It excludes completion-observation latency and RMT start delay,
  so it is NOT a measured wire clear-gap range.
- `max_rearm_lateness_us`: maximum software lateness for the initial start or a
  subsequent rearm deadline. It does NOT indicate blocked-width variation.

STOP terminates the RMT transaction, restores GPIO control and clears all outputs.
An aborted run can have a partial/unconfirmed pulse not included in its count;
do not use it for capture percentages. Initialization/transmission failure is
reported as ERROR, never DONE. A missing completion has a width + 2 second
watchdog timeout. No automatic software fallback is used after RMT errors.

The C3 implementation targets the tested Espressif Arduino core **3.3.12** and
its ESP-IDF RMT APIs. No additional Arduino library is needed. Consult
[Espressif's RMT documentation](https://docs.espressif.com/projects/esp-idf/en/v5.5/esp32c3/api-reference/peripherals/rmt.html).

Version 0.3.1 uses an RMT completion callback with a short ISR-safe flag update.
Busy checks read that flag; they do not call the driver's logging timeout API.
The first 0.3.0 hardware run captured 100/100, but emitted repeated
`rmt_tx_wait_all_done: flush timeout` messages because its zero-timeout polls
were logged while a transaction was still active. Those were polling timeouts,
not failed transmissions. Repeat that run on 0.3.1 for a quiet baseline.

## Repeatable Capture Test

1. Disconnect the real sensor from the test input, wire the interface, and check
   clear/blocked levels. Return to clear with STOP.
2. Connect DragWin to the receiving board; open Sensor Test and select lane 1.
   Do not arm a race for this raw-count test. Reset Counts and check for zero
   unexpected activity while idle. Resolve floating-input noise first.
3. Run `RUN 5000 20000 100 997 1` as a short sanity test. After DONE and the
   diagnostic screen catches up, the receiving trap count should increase by
   exactly 100. Compare its latest pulse width with the generator's width range;
   the display is not a history of every individual width.
4. Reset Counts between runs. Try 1000 pulses each at widths 5000, 2700, 2000,
   1000, 500, 250 and 100 us, keeping gap 20000 and jitter 997. Example:
   `RUN 1000 20000 1000 997 1`. A run takes roughly count * (width + gap +
   half the jitter), plus the initial second. Do not close the generator monitor
   mid-run (opening a serial port can reset a board).
5. Record sent count, received count, capture percentage, generator width range,
   generator lateness, receiver firmware, selected lanes, wiring and diagnostic
   load. On Mega also use Read Scan Timing, resetting scan metrics between runs.
   Due scan spacing is NOT an ISR latency measurement.
6. For a separate SOFTWARE-mode test, wire more channels and select corresponding
   diagnostic lanes; repeat with mask 15 to exercise four near-simultaneous inputs.
   C3 hardware mode is currently one channel only. Keep all other conditions
   constant when comparing firmware changes. This initial harness drives four
   inputs, NOT all 24 inputs, and does not generate a complete staged pass.
7. Reverse roles: upload the generator build for the other board and restore
   normal DragMC on the new receiver. With a C3 generator, keep its sketch and
   move the interface to the other receiver. For NPN wiring move each pull-up to the NEW receiver's
   logic supply and recheck meter levels before enabling capture. Never reuse a
   Mega-receiver 5 V collector pull-up on a Due receiver.

For comparisons without live diagnostic traffic, measure a count delta before
and after a controlled armed run, keeping capture enabled for the entire train.
Account for staging and race timeouts; closing Sensor Test disables idle Due
capture when its lease expires. Do not call a no-capture run a missed-pulse test.
The first useful baseline is Sensor Test under the same load as venue testing.

Suggested record:

| Receiver / firmware | Mask | Requested us | Generated width range us | Sent | Captured | Percent | Max scan gap us / generator lateness us | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | | |

Counts and ranges establish capture reliability under a particular test load,
not a guaranteed minimum detectable pulse or absolute timing accuracy. Repeat
runs; use an oscilloscope/logic analyzer at the receiver for independent wire
timing validation. ESP32 task scheduling and USB services can lengthen SOFTWARE
pulses even with Wi-Fi/Bluetooth unused; inspect the ranges/lateness for each
run. C3 HARDWARE mode instead programs the blocked width into RMT and explicitly
does not claim to measure it. The real LM393 board's
filtering and optical mounting remain separate tests. No board has been flashed
by adding these files.
