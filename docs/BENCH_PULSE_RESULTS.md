# Bench Pulse Capture Results

## October 28, 2026: Direct C3 Hardware Baseline

Before Tree-output caching, the ESP32-C3 Super Mini drove the spare Mega A2
directly in MODE DIRECT using ENGINE HARDWARE_RMT. Grounds were common, no 5 V
pull-up or other driver was connected to A2, and unused A0/A1/A3 had separate
10k resistors to ground. The operator measured 0 V clear and 3.28 V blocked.
The 3.3 V HIGH has limited Mega input margin; the electrical pulse widths were
programmed, not independently measured at the wire. Sensor Test monitored lane
1; record the interval configuration explicitly in subsequent runs. Each run requested 1000 pulses, gap 20000 us,
jitter 997 us, and mask 1; counts and scan metrics were reset between runs.
Results and generator summaries were reported by the operator; the serial log
also records counts and scan metrics. Maxima include time outside the pulse
train until the operator reads them; they are not a distribution or guarantee.

| Width us | Edge counts | Max scan gaps us | Max scan durations us |
| --- | --- | --- | --- |
| 5000 | 1000, 1000, 1000 | 3104, 3064, 3104 | 412, 400, 404 |
| 2000 | 1000, 1000, 1000 | 3948, 3948, 3952 | 408, 408, 412 |
| 1400 | 1000, 1000, 998 | 3976, 3980, 3980 | 412, 404, 408 |
| 1136 | 999, 998, 1000 | 4300, 3980, 3980 | 404, 400, 396 |
| 682 | 1000, 1000, 996 | 3980, 3980, 3980 | 408, 408, 412 |

Generator gap scheduling can stretch clear gaps. The deterministic jitter
sequence and single input do not cover every timing phase or four-lane load.
These Sensor Test results do not qualify race-mode capture or track speed.
Before this series, five reset-separated 5000 us single-pulse runs all counted
one edge without the earlier roughly one-second startup indication. Earlier
NPN hardware runs had that indication; its cause remains unresolved.

With the same C3 direct setup connected instead to Due A2, one 1000-pulse run
at each of 1400, 1136, and 682 us counted 1000 edges. Reported maximum scan
gap/duration were 355/32, 354/31, and 347/31 us respectively. Due interrupts,
not its main-loop scan, capture sensor edges. Single requested 1000/2000/5000 us
pulses were reported as 999/1999/5000 us by the Due; this is a useful cross-check,
not independent calibrated wire measurement. Both targets require repeated
race-mode and physical track validation.

## October 28-29: Tree Cache And Serial Receive Experiment

The operator flashed an experimental shared-sketch Tree-output cache; Mega
firmware identity remained 0.6.8, so identify it by source/build, not version
alone. The same direct C3/Mega wiring and nominal pulse settings were used.
These are operator-reported counts and maxima, not calibrated measurements.

| Width us | Cached Tree edge counts | Max scan gaps us | Max scan durations us |
| --- | --- | --- | --- |
| 5000 | 1000, 1000, 1000 | 4164, 3044, 4164 | 412, 400, 408 |
| 2000 | 1000, 1000, 1000 | 3948, 3968, 3236 | 412, 412, 408 |
| 1400 | 999, 1000, 999 | 3968, 3960, 3956 | 416, 404, 412 |
| 1136 | 999, 1000, 998 | 3960, 3956, 3980 | 412, 412, 412 |
| 682 | 998, 968*, 999 | 3980, 3980, not reported | 408, 416, not reported |

The 968 run remained displayed blocked and the log shows corrupted diagnostic
frames; its final actual controller count is not established. Do not discard it
or treat 32 misses as established. Another repeat reported 999, gap 3948 us,
duration 412 us, with corrupted diagnostic frames followed by a valid clear
snapshot. At 1400 and 1136 us the three-run totals matched the baseline; the
data do not establish a capture improvement or regression from Tree caching.
Windows diagnostic handling differed in a subsequent experiment, so keep build,
monitoring, and wiring conditions explicit in future comparisons.

October 29: with Sensor Test open and 682 us/1000 pulses/20000 us gap, the
original Release application outside the debugger reported 1000 edges but the
log contained six checksum-rejected diagnostic lines. A Windows experiment moved
line delivery/logging off the serial callback into a bounded ordered queue.
Three repeat runs reported 997, 999, and 998 edges, with gaps 4004, 4000, and
4004 us and durations 404, 404, and 396 us. No checksum or receive-queue
warnings were seen in those runs. This supports further evaluation; it does not
prove the root cause or guarantee race result delivery under all conditions.
The loose A2 test jumper was removed before these runs. Neither a direct 3.3 V
signal nor the generator output was independently qualified at the wire.

With live Sensor Test closed during a separate 682 us/1000-pulse run, a later
snapshot reported 999 edges, gap 2828 us and duration 380 us, without checksum
warnings. A second closed-monitor run under the original Release application
reported 998, 2828 and 380. This is not an active-race comparison.

For the next proposed bench procedure, see [race-mode test design](BENCH_RACE_MODE_TEST.md).

## October 29: Active Practice Baseline

Three manual-start, lane-1 practice tests used the Tree-cache Mega build and
direct C3-to-A2 wiring. Sensor Test was stopped during each pass. Each generator
run reported 500 completed pulses, programmed width 1400 us, gap 20000 us,
jitter 997 us. A2 snapshots after the runs reported **499, 499, and 500**
edges. Each pass produced a legal launch, trap event, and valid finish. The
operator used jumpers to simulate staging and finish, so reported race times and
speeds are not physical car results. Intermittent staging aborts while
positioning jumpers preceded successful starts. Scan maxima for these three
runs were not recorded; do not infer them. These are raw-count stress tests,
not 1500 separate race passes or track qualification. Generator width was not
independently measured at the wire, and the direct Mega signal has limited HIGH
voltage margin. See [race-mode design](BENCH_RACE_MODE_TEST.md).

With Mega 0.6.9, retaining the Tree cache and adding heartbeat deferral, six
repeat runs under the same nominal conditions each reported 500 completed C3
pulses and 500 A2 edges. All six produced valid simulated finishes. The log
shows the heartbeat-quiet event after manual start and a heartbeat on race exit.
The matching experimental DragWin receive-queue build was used. These six
runs are encouraging but do not establish a causal improvement or a miss-free
rate; repeat and alternate builds/conditions where practical. This is a
single-input bench test, not four-lane or optical track qualification.

Three additional Mega 0.6.9 manual-start practice runs used 1136 us programmed
pulses, gap 20000 us, jitter 997 us, and 500 pulses per run. The operator
reported 500 generator completions each time; A2 snapshots recorded 500, 500,
and 500 edges, and all three passes were valid. Heartbeat quiet/resume was
observed in the log. At a full inch of optical interruption, 1136 us corresponds
to approximately 60 mph; the electrical bench test does not validate sensor
mechanics or a maximum track speed. There is no matching pre-deferral race-mode
baseline at this width. The direct signal has limited HIGH margin on the Mega.

Three further Mega 0.6.9 manual-start practice runs used 682 us programmed
pulses, gap 20000 us, jitter 997 us, and 500 pulses per run. The operator
reported 500 generator completions each time; A2 snapshots recorded 500, 500,
and 500 edges, and all three passes were valid. Heartbeat quiet/resume appeared
in the log. At a full inch of optical interruption, 682 us corresponds to
approximately 100 mph; neither optical capture nor track speed is qualified by
these electrical bench runs. No matching pre-deferral race-mode baseline exists
at this width. Generator width was not independently measured at the wire, and
the direct 3.3 V connection has limited HIGH margin on the Mega.

## October 30: Conservative Selective Race Scan

Mega 0.6.10 retains Tree caching and heartbeat deferral and skips polling
nonparticipating lanes and disabled interval inputs during active racing when
Sensor Test is closed. The operator reported three 500-pulse C3 RMT runs at
each programmed width, with gap 20000 us, jitter 997 us, mask 1, direct GPIO4
to Mega A2, lane 1 manual-start practice, and Sensor Test closed during the
pass. Each generator run completed 500 pulses and each final A2 snapshot
reported 500 edges: 500/500 in all three runs at **1400, 1136, and 682 us**.
The logs show valid completed passes and heartbeat quiet/resume in each run.
The previous 0.6.9 baseline also recorded 500/500 under these nominal conditions
(six runs at 1400 us, three at each other width). These counts show no observed
regression in this small set, but do not demonstrate a performance improvement.
Scan maxima for these runs were not recorded as race-only measurements. The C3
programmed width was not independently calibrated and the direct 3.3 V signal
has limited Mega HIGH margin. Repeated single-lane electrical pulses are not
qualification of the optical installation, racing speeds, or four-lane loads.

## October 24, 2026: Software Generator Baseline

Operator-reported tests used the ESP32-C3 Super Mini software pulse generator,
GPIO4 through a 2N3904 NPN interface to the spare Elegoo Mega's A2 speed-trap
input. The collector pull-up was 10k to Mega 5 V, base drive 2.2k, base-to-ground
47k, and grounds common. Static receiver levels were approximately 0 V clear
and 4.6 V blocked. C3 used MODE NPN; Mega retained DragMC race firmware.

DragWin Sensor Test selected lane 1, with interval inputs disabled. Unused
lane 1 A0/A1/A3 inputs were subsequently grounded through individual 10k
resistors. The operator then reported no other edges in Sensor Test. This does
not prove that unselected Mega inputs stopped being physically sampled.
No Tree-caching or selective-race-polling optimization had been applied.
Exact running build identities were not captured with these summaries; record
both firmware identities and generator version in subsequent controlled tests.

All listed runs requested gap 20,000 us, jitter 997 us, mask 1. Width ranges
are generator SOFTWARE timestamps, not independent receiver-wire measurements.
Scan figures are observed maxima, not continuous distributions or guarantees.

| Requested width us | Sent | Reported A2 count | Generator width us | Generator max lateness us | Mega max scan gap us | Mega max scan duration us |
| --- | --- | --- | --- | --- | --- | --- |
| 5000 | 100 | 100 | 5003-5004 | 9 | 3808 | 386 |
| 2000 | 100 | 100 | 2004-2011 | 8 | 2828 | 396 |
| 1400 | 100 | 100 | 1402-5060 | 3656 | 2968 | 400 |
| 1400 | 100 | 100 | 1402-4430 | 3027 | 3920 | 392 |
| 1400 | 1000 | 1000 | 1402-4698 | 3295 | 3960 | 396 |

The 2000 us / 100-pulse run explicitly followed resetting and confirming zero.
Other table rows followed the reset-between-runs procedure; the final 1000-pulse
run's initial zero was not separately confirmed in the transcript. Treat counts
as operator reports rather than an independently audited capture rate.

Earlier bring-up observations are not clean baselines:

- First 5000 us run reported 84 edges for 100 pulses. Starting count/reset and
  floating-input conditions were not controlled. Cause remains unexplained.
- A later 5000 us run reported 100 edges, generator width 5003-7525 us and max
  lateness 2522 us; Mega scan gap/duration 3400/412 us. Other inputs were floating.
- An intermediate 2000 us run ended at 200 edges with unchanged scan maxima,
  consistent with cumulative counters rather than a confirmed fresh run.

## Interpretation And Next Comparison

These are encouraging zero-observed-miss results in the reported clean runs,
not qualification of uniform 1.4 ms pulses or race timing. The software generator
occasionally stretched blocked pulses by milliseconds. Its min/max summary does
not say how many pulses were stretched. A maximum Mega scan gap longer than a
pulse creates a possible blind interval, but does not imply a miss on every run:
the worst gap may be rare and may not overlap any blocked pulse.

Version 0.3.0 adds a one-channel C3 RMT engine to program blocked widths in
hardware. Clear gaps may still grow with software scheduling. This removes the
software-controlled end of each blocked pulse, but programmed width must not
be described as measured wire accuracy. Physical validation remains pending.

Repeat the same widths/counts with ENGINE HARDWARE before changing Mega code.
Then, one change at a time, compare Tree-output caching, selective race polling,
and further evidence-driven optimizations. Sensor Test must keep polling its
selected inputs continuously, so race-only optimizations also require a separate
repeatable race-mode test. Preserve raw output, exact firmware versions, initial
zero checks, lane scope and wiring conditions for each comparison.

See [generator wiring and operation](../dragPulseTest/README.md).

## First RMT Run: Repeat After Logging Fix

The operator uploaded C3 generator 0.3.0, kept MODE NPN and the same Mega wiring,
and requested `RUN 5000 20000 100 997 1`. Reported completion was 100 pulses;
Mega A2 count was 100, maximum scan gap 3204 us and duration 396 us.
Programmed width was 5000 us (not a measured wire width). Requested clear gaps
were 20001-20968 us, software rearm waits 20003-20971 us, max rearm lateness 7 us.

The run also emitted many driver `flush timeout` messages. Version 0.3.0 used
`rmt_tx_wait_all_done(channel, 0)` as a nonblocking busy check; the driver logs
the expected timeout whenever the transaction is not yet complete. Its DONE
summary indicates confirmed completions, but logging adds an uncontrolled load.
Do not discard the observation or classify these messages as transmit failures;
also do not use the run as the final quiet baseline.

Version 0.3.1 replaces the busy-check API with the RMT completion callback and
an ISR-safe flag. Repeat the same 100-pulse 5000 us run after uploading it,
resetting the receiver counts and confirming zero. Callback behavior has host
tests; physical quiet-run verification remains pending.
