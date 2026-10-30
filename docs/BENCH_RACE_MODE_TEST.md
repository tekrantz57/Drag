# Race-Mode Bench Test Design

This is a proposed controlled bench procedure, not yet physically validated.
Its purpose is to compare Mega firmware versions during an actual active
practice session with live Sensor Test monitoring off. It is not track-speed
qualification and does not simulate 500 complete passes.

## Why This Test

Sensor Test generates frequent diagnostic frames while the generator is
running. With the window closed, the Mega still polls its inputs and keeps raw
edge counts, but does not stream the sensor changes. In a real race the first
speed-trap edge is latched as a race event; further trap pulses exercise input
polling and diagnostics, not repeated race results. A detected raw edge is not
proof of accurate race timing or of a valid finish. Record race results and raw
edge counts separately.

## Setup And Safety

- Use the spare Mega and C3 generator, not a live track. Record exact source or
  build identity, including whether Tree caching is present, DragWin build,
  generator version/engine, lane scope, settings, and wiring. Version 0.6.8
  alone does not distinguish the experimental cache build from the baseline.
- Use one active practice lane, lane 1, heads-up, and a known Tree and staging
  mode. Disable optional intervals for this comparison. Record staged delay.
  Disable manual starter for the initial run or explicitly record its use.
- Power down both boards before changing wiring. The existing direct C3 GPIO4
  to Mega A2 is a 3.3 V signal with limited Mega HIGH margin, not a guaranteed
  interface. No 5 V pull-up or real sensor may be connected to GPIO4/A2. Tie
  C3 and Mega grounds together, do not tie their supply outputs together, and
  disconnect the signal before powering down either board. Use a measured and
  validated NPN interface for stronger HIGH margin if available, documenting it
  as a different test condition.
- Define Mega A0, A1, and A3 with individual 10k pulldowns as in the prior
  bench tests. To simulate staging and finish, use controlled switches/jumpers
  from Mega 5 V to those Mega inputs while retaining their pulldowns; verify the
  connections with power off before applying power. Never connect Mega 5 V to
  C3 GPIO4 or to a Due input. No live Tree or track power is needed.

## Proposed Sequence

1. With Sensor Test open, select lane 1, check clear inputs, reset counts and
   scan timing, and confirm A2 is clear with zero edges. Close Sensor Test and
   wait for `ACK:SENSOR_MONITOR:STOP` in the log. Do not reset counters again
   until the later snapshot is read.
2. Arm a lane-1 practice pass. Hold both pre-stage A0 and stage A1 blocked
   using the controlled Mega inputs, respecting the configured staging mode.
   Confirm the Tree starts and reaches green. Release A1 only after green and
   long enough for its normal stage debounce; release A0 as well. Verify a
   non-negative reaction/launch event. If a foul or abort occurs, discard the
   attempt, return the inputs clear, and restart with a fresh reset.
3. While the race is still active and before the finish input is triggered,
   run `MODE DIRECT`, `ENGINE HARDWARE`, then
   `RUN 1400 20000 500 997 1`. It takes roughly 11 seconds including startup.
   The race timeout is 30 seconds after the latest scheduled green; start
   promptly and confirm from the log that the run completed before timeout.
   Never rely on a host timestamp alone to establish timing alignment.
4. After generator DONE, hold finish A3 blocked briefly and release it to end
   the pass. Wait for completed practice results; no physical car is involved.
   If the race has already timed out, do not interpret it as a valid pass.
5. Reopen Sensor Test and read the A2 raw edge count and scan timing without
   resetting. Save the generator summary, raw count, scan maxima, race events,
   result, warnings, and whether a valid final clear state was observed. Reset
   only when preparing the next attempt.

Start with a single 5000 us pulse as a wiring and workflow check; then repeat
the 500-pulse 1400 us run at least three times. Compare the same procedure on
before/after builds, same wiring, settings, monitor state, and application.
Optionally test other widths, but record them separately. Do not extrapolate a
speed limit from this test. An idle/monitor-off control and an actual track
pass series are separate comparisons.

The scan maxima cover the whole interval since reset, including staging,
arming, and idle before/after the pulse train; they are not race-only maxima.
An exact phase-specific scan-gap distribution needs separate instrumentation.
Generator width is hardware programmed, not independently measured at the
wire; scheduled clear gaps can grow. Deterministic jitter and timing phase may
hide or exaggerate misses. Direct-input voltage margin and sensor mechanics
are further limitations. Do not claim that an optimization improved real-race
reliability without repeated representative testing.

The experimental Mega 0.6.9 build retains Tree caching and defers periodic
heartbeats once an automatic staged hold begins or a manual start is accepted,
until the race ends or staging is aborted. The Due target remains unchanged.
For the controlled comparison, keep the experimental DragWin receive-queue
build and physical setup unchanged; record each firmware identity. A quiet
heartbeat does not verify the link. The matching DragWin build reports the quiet
period as unverified, returns to stale if it persists beyond two minutes, and
keeps firmware updating guarded. On exit the Mega sends an immediate heartbeat.

The experimental Mega 0.6.10 build retains those changes and, only while a race
is active and Sensor Test monitoring is off, scans participating lanes and
installed interval sensors. Pre-stage, stage, trap, and finish remain scanned
for each participating lane, including finish when trap is missed. When Sensor
Test is open, or outside the active-race state, all inputs are polled. Unscanned
inputs cannot accumulate complete raw edge counts during that period; opening
Sensor Test restores full polling on the next scan. The Due path is unchanged.
Host tests and both firmware builds pass; hardware comparisons remain pending.
Use 0.6.9 as the previous active-race baseline, with the same experimental
DragWin build and wiring. First validate real trap and finish events, then
repeat the same 500-pulse A2 raw-count stress procedure. Results for excluded
inputs cannot be compared as though they were continuously monitored.
