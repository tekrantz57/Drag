# Drag Strip Serial Protocol

Each message is one printable ASCII line terminated by `LF`. Bytes outside
ASCII `0x20` through `0x7E` are not permitted inside a message.

The serial link runs at 115200 baud.

Current source firmware is Mega DragMC 0.6.8 / Due 0.1.2, protocol 11. Protocol 9 added optional
starter-button authorization. Protocol 10 captures speed-trap and finish on the
first sampled raw blocked edge and adds requested scan-timing diagnostics. Pins,
staging debounce, and Tree timing are unchanged.
Protocol 11 adds lane-scoped monitoring and `CAPTURE` sensor status. The Due
physically detaches unused inputs; the Mega retains polling and filters diagnostics.

```text
PART:PART:PART:XX
```

`XX` is the two-digit uppercase hexadecimal XOR of every ASCII byte before the
final checksum colon. The checksum excludes that colon and the line ending.

```text
PING:10
ACK:PING:63
```

## Race modes

- `HEADS_UP`: all four lane trees run together. Legal finishers are ranked by
  finish-line crossing order.
- `BRACKET`: each lane has a dial-in. The slowest dial starts its tree first;
  every other lane is delayed by `slowest dial - lane dial`. A legal
  non-breakout finisher beats a breakout. If every eligible finisher breaks
  out, the smallest breakout wins. A foul lane is never eligible.

Dial-ins use integer milliseconds on the wire. The controller accepts
`100` through `60000`.

The active lane count is either `2` or `4`. Two-lane mode uses physical lanes
1 and 4; lanes 2 and 3 are ignored and their lights are forced off.

Track and speed-trap lengths use integer thousandths of an inch. They are
updated together, and the speed-trap length must be shorter than the track.
Track length is race metadata; speed-trap length is used for MPH calculation.

Sensor status fields use logical blocked state, not raw voltage. With the
current active-HIGH LM393 sensors, `1` means the beam is blocked and `0` means
the beam is clear.

Each lane may optionally enable two interval timers between stage and the speed
trap. Their pins are always initialized, but disabled interval timers are
ignored by race and track-clear logic. Interval times are cumulative from the
stage beam clearing at launch.

## Tree modes

- `FULL`: amber 1, amber 2, amber 3, and green are separated by 500 ms.
- `PRO`: all three ambers illuminate together, followed by green after 400 ms.

The staged delay is independent of Tree cadence. In Automatic mode it controls
the wait from all participating lanes being fully staged to the first amber and accepts `0`
through `5000` milliseconds. The default is `500` ms. If any participating car
leaves the stage beam during this delay, the start is aborted and staging
begins again without assigning a red light.

### Starter Button Mode

`SET:START_MODE:MANUAL` selects starter authorization; `AUTO` is the power-up
default. Mode changes are rejected during an active race or result delivery.
`ARM:MANUAL` and `ARM:AUTO` atomically choose the mode and reset the race for a
new run, including if replacing a previous active run. This avoids a reset
briefly falling back to automatic mode while configuration commands arrive.
Legacy `RESET` preserves the selected start mode.

In manual mode all participating lanes must stage before
`EVENT:TREE:WAITING_FOR_START:<token>` is emitted. The operator sends
`START_TREE:<token>`. The controller accepts it only in the staged state, with
all required beams still satisfied, no light test, and no prior authorization.
It replies `ACK:START_TREE` and `EVENT:TREE:START_AUTHORIZED`, then runs the
configured staged delay starting at acceptance, followed by the normal Tree.
An early, duplicate, stale-token, or otherwise invalid request produces
`ERROR:STATE:START_TREE`; it never starts or restarts the delay.

Rollout during the wait or delay aborts staging and cancels authorization.
Restaging creates a different nonzero 16-bit token and requires a fresh click.
The token is an attempt identifier, not authentication. It wraps after 65,535
staging attempts. Each heartbeat now includes `START_TOKEN:<token>`; state is
`WAITING_FOR_START` before authorization or `START_DELAY` afterward. This lets
Windows recover readiness if the initial staged event was missed. Status adds
`STATUS:START_MODE:<AUTO|MANUAL>:START_TOKEN:<token>:AUTHORIZED:<0|1>`.

Windows enables Start Tree only for an armed manual run, a compatible identified
Mega (protocol 9+), fresh heartbeat, and a current staged token. It disables
after clicking, on rollout, race start/completion, reboot, or disconnect.
Demo simulation remains independent of the physical starter control.

Staging detection defaults to `BOTH_BLOCKED`, which requires the pre-stage and
stage beams to be blocked at the same time. `IN_ORDER` is intended for manual
testing: it latches a pre-stage blockage while the stage beam is clear, then
accepts a later stage blockage even after the pre-stage beam has cleared.

## Windows-to-controller commands

- `PING`
- `IDENTIFY`
- `STATUS`
- `SENSOR_DIAGNOSTICS`
- `SENSOR_MONITOR:START`
- `SENSOR_MONITOR:START:<lane-list|NONE>` (protocol 11; for example `1` or `1,4`)
- `SENSOR_MONITOR:STOP`
- `LIGHT_TEST:START`
- `LIGHT_TEST:SET:<lane>:<PRESTAGE|STAGE|AMBER_1|AMBER_2|AMBER_3|GREEN|RED>:<0|1>`
- `LIGHT_TEST:OFF`
- `LIGHT_TEST:STOP`
- `RESET_SENSOR_DIAGNOSTICS`
- `RESET`
- `SET:LANES:<2|4>`
- `SET:HEAT_LANES:<comma-separated-physical-lanes>`
- `SET:INTERVAL_LANES:<NONE|comma-separated-physical-lanes>`
- `SET:DISTANCES:<track-inches-x1000>:<trap-inches-x1000>`
- `SET:MODE:HEADS_UP`
- `SET:MODE:BRACKET`
- `SET:TREE:FULL`
- `SET:TREE:PRO`
- `SET:STAGED_DELAY:<0-5000-milliseconds>`
- `SET:STAGING_MODE:<BOTH_BLOCKED|IN_ORDER>`
- `SET:DIAL:<lane>:<milliseconds>`

Race, Tree, staging-mode, lane, distance, staged-delay, and dial changes are
accepted only outside staging and an active race.

## Controller-to-Windows messages

- `HELLO:DRAG_MC:<firmware-version>:PROTO:<protocol-version>:MCU:MEGA2560:LANES:<2|4>:HEAT_LANES:<list>`
- Experimental Due race target uses the same commands and results with
  `HELLO:DRAG_MC_DUE:0.1.2:PROTO:11:MCU:SAM3X8E:LANES:<2|4>:HEAT_LANES:<list>`.
  It is not a Mega firmware-update target; see `DUE_PORT.md`. Due intervals,
  trap and finish use ISR raw-edge timestamps, while stage/pre-stage retain
  2 ms qualification. Due scan diagnostics measure main-loop snapshots, not
  interrupt latency. The older `DRAG_MC_DUE_LIGHT_TEST` remains light-only.
  Due 0.1.1 reports `ERROR:SENSOR_CAPTURE_NOISE:LANE:<n>:SENSOR:<name>` and
  disables a pin after 32 ISR callbacks without a main-loop snapshot. Capture
  stays disabled until `RESET_SENSOR_DIAGNOSTICS` or reboot. Fix/bias the input
  before reset; a required sensor fault prevents that lane's race capture.
- `HEARTBEAT:<controller-millis>:SEQ:<last-event-sequence>:STATE:<state>`
- `ACK:PING`
- `ACK:RESET`
- `ACK:SENSOR_MONITOR:START`
- `ACK:SENSOR_MONITOR:STOP`
- `ACK:LIGHT_TEST:START`
- `ACK:LIGHT_TEST:SET:<lane>:<light>:<0|1>`
- `ACK:LIGHT_TEST:OFF`
- `ACK:LIGHT_TEST:STOP`
- `ACK:RESET_SENSOR_DIAGNOSTICS`
- `ACK:SET:LANES:<2|4>`
- `ACK:SET:HEAT_LANES:<comma-separated-physical-lanes>`
- `ACK:SET:INTERVAL_LANES:<NONE|comma-separated-physical-lanes>`
- `ACK:SET:DISTANCES:<track-inches-x1000>:<trap-inches-x1000>`
- `ACK:SET:MODE:<mode>`
- `ACK:SET:TREE:<FULL|PRO>`
- `ACK:SET:STAGED_DELAY:<milliseconds>`
- `ACK:SET:STAGING_MODE:<BOTH_BLOCKED|IN_ORDER>`
- `ACK:SET:DIAL:<lane>:<milliseconds>`
- `STATUS:TREE:<state>:MODE:<mode>:LANES:<2|4>:HEAT_LANES:<list>:TRACK_IN_X1000:<value>:TRAP_IN_X1000:<value>`
- `STATUS:SETTINGS:TREE:<FULL|PRO>:STAGED_DELAY_MS:<milliseconds>:STAGING_MODE:<BOTH_BLOCKED|IN_ORDER>`
- `STATUS:INTERVAL_LANES:<NONE|comma-separated-physical-lanes>`
- `STATUS:INTERVALS:LANE:<lane>:ENABLED:<0|1>:INTERVAL_1:<0|1>:INTERVAL_2:<0|1>`
- `STATUS:LANE:<lane>:DIAL_MS:<milliseconds>:PRESTAGE:<0|1>:PRESTAGE_LATCHED:<0|1>:STAGE:<0|1>:SPEED_TRAP:<0|1>:FINISH:<0|1>:FOUL:<0|1>:FINISHED:<0|1>`
- `SENSOR:<lane>:<name>:INSTALLED:<0|1>:CAPTURE:<0|1>:BLOCKED:<0|1>:RAW:<0|1>:EDGES:<count>:PULSE_US:<microseconds|NONE>`
  (`CAPTURE` added in protocol 11; older firmware omits it.)
- `EVENT:TREE:<state>`
- `EVENT:TREE:STAGING_ABORTED`
- `EVENT:LANE:<lane>:AMBER_<1|2|3>`
- `EVENT:LANE:<lane>:PRO_AMBERS`
- `EVENT:LANE:<lane>:GREEN`
- `EVENT:LANE:<lane>:FOUL`
- `EVENT:LANE:<lane>:REACTION_US:<microseconds>`
- `EVENT:LANE:<lane>:SPEED_TRAP`
- `RESULT:LANE:<lane>:INTERVAL_1_US:<microseconds>`
- `RESULT:LANE:<lane>:INTERVAL_2_US:<microseconds>`
- `RESULT:LANE:<lane>:INTERVAL_1_UNAVAILABLE`
- `RESULT:LANE:<lane>:INTERVAL_2_UNAVAILABLE`
- `RESULT:LANE:<lane>:SPEED_TRAP_US:<microseconds>`
- `RESULT:LANE:<lane>:ELAPSED_US:<microseconds>`
- `RESULT:LANE:<lane>:VALID`
- `RESULT:LANE:<lane>:BREAKOUT_US:<microseconds>`
- `RESULT:LANE:<lane>:SPEED_MPH_X100:<hundredths-of-mph>`
- `RESULT:LANE:<lane>:DNF`
- `RESULT:PLACE:<place>:LANE:<lane>`
- `RESULT:WINNER:LANE:<lane>`
- `RESULT:NO_WINNER`
- `ERROR:CHECKSUM`
- `ERROR:COMMAND:<command>`
- `ERROR:STATE:RACE_ACTIVE`
- `ERROR:STATE:LIGHT_TEST_INACTIVE`
- `ERROR:VALUE:<setting>`

`EVENT` and `RESULT` frames include trailing metadata:

```text
:SEQ:<sequence>:MS:<controller-millis>
```

The sequence number increments for each `EVENT` or `RESULT` frame and resets
when the controller resets. Windows should treat these fields as metadata; the
race meaning is still in the leading fields.

Sensor diagnostics are collected from raw input transitions. Staging and interval
inputs retain 2 ms debounce; speed-trap and finish bypass that qualification in
firmware 0.6.7+. Those race results latch once per pass, using the first sampled
blocked edge. Chatter cannot overwrite the latched timestamps. No interrupt
capture is added: a pulse entirely between scans can still be missed.
`EDGES` counts clear-to-blocked transitions.
`PULSE_US` is the duration of the most recently completed blocked pulse, or
`NONE` if no pulse has completed since startup or the last diagnostic reset.
Counters saturate rather than wrapping and can be cleared without resetting
the race controller.

Protocol 10 adds `SCAN_DIAGNOSTICS`, which replies once with
`STATUS:SCAN:MAX_GAP_US:<microseconds>:MAX_DURATION_US:<microseconds>`.
These are the largest intervals between starts of full 24-input scans and the
largest scan execution time since startup or `RESET_SENSOR_DIAGNOSTICS`.
They are not per-pin measurements; gap plus duration is a conservative bound
on spacing between two samples of an individual input in the observed window.
Observed maxima are not a guarantee against future longer delays.
Use Sensor Test > Read Scan Timing after a pass. Nothing is automatically
streamed for this measurement, and the values persist after a race completes.

Protocol 6 adds leased sensor monitoring. `SENSOR_MONITOR:START` sends an
initial snapshot incrementally and then sends change-only `SENSOR` frames. The
same command renews the three-second lease without repeating the snapshot.
`SENSOR_MONITOR:STOP`, lease expiration, or a controller reset stops the
stream. Diagnostic frames retain four free output-queue positions for
race-critical traffic, and a pending sensor remains pending until its newest
state is successfully queued. DragWin renews the lease once per second.

Protocol 11 adds `SENSOR_MONITOR:START:<lane-list|NONE>`. The list accepts unique
physical lane numbers 1-4, independently of race lane count; NONE selects no
lanes. Malformed/duplicate lists return `ERROR:VALUE:SENSOR_MONITOR` without
changing scope. Identical renewals retain the current capture baseline and do
not repeat snapshots. A changed list replaces pending diagnostics and snapshots
the new scope. Required sensors and configured installed intervals are captured;
uninstalled interval positions are reported as not installed, not enabled for ISR
capture. The unscoped legacy START selects all lanes' installed sensors.

On Due 0.1.2, idle startup has all sensor interrupts detached. ARM or legacy
RESET enables only participating/installed inputs, taking a fresh baseline
without counting an edge. An armed race overrides diagnostic scope. Once the
race finishes, the active monitor lease resumes, or all sensor interrupts are
detached if there is no monitor. STOP/lease expiry cannot disable race inputs.
Disabled inputs discard pending edges but retain diagnostic counters; noise
faults require explicit diagnostic reset after correcting wiring. `CAPTURE:0`
also covers noise-quarantined inputs. Mega 0.6.8 filters diagnostic traffic by
scope but continues polling all inputs.

Protocol 8 adds guarded tree-light testing. `LIGHT_TEST:START` is accepted only
while the controller is waiting for staging and temporarily suspends normal
staging and race-tree updates. `SET` controls one of the seven outputs for a
physical lane, while `OFF` clears all 28 tree outputs without leaving test
mode. Lights retain their commanded state until another `SET`, `OFF`, `STOP`,
or `RESET` command is received. `STOP` and `RESET` clear all outputs and return
the controller to normal staging behavior. If serial communication is lost,
the selected lights remain on; reconnect and send `STOP` or `RESET`, or power
cycle the controller, to clear them.

`BLOCKED` is the debounced state used by race logic; `RAW` is the immediate
electrical state. `INSTALLED` is `0` only for optional interval positions that
are disabled. `SENSOR_DIAGNOSTICS` remains available as a compatibility
snapshot, but its frames are also emitted incrementally rather than filling
the serial queue in one command.

An enabled interval timer sends its cumulative launch-relative time when its
beam is blocked. If the finish is reached without that interval, the controller
sends the corresponding `UNAVAILABLE` result. Missing or disabled interval
timers never prevent a pass from finishing. Segment times are calculated by
Windows from adjacent cumulative values.

Firmware 0.6.5 queues completed-lane results incrementally and retries when the
output queue is full. Final lane data, placements, and the bracket winner are
queued before `EVENT:TREE:RACE_COMPLETE`; the result hold begins only after
that completion frame is queued. Diagnostics pause during final result delivery.
Reaction, foul, and available interval data may be repeated in the final lane
snapshot so a previously dropped live frame does not leave the result incomplete.

`IDENTIFY` makes the controller resend its `HELLO` frame. DragWin uses this
after a firmware upload so verification does not depend on catching the one
startup transmission while the serial port is reopening.

`REACTION_US` is sent for both legal and foul starts; a negative value is the
amount by which the lane left before green. It precedes the lane's `FOUL`
event. At race completion, the controller sends authoritative `PLACE` messages.
In bracket mode, legal finishers rank first by stripe crossing, breakouts rank
next by least breakout, and red lights rank next by smallest negative-reaction
magnitude. DNFs are not assigned a place.
