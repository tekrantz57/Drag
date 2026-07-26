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
  diagnostic inputs currently uses about 82% of the Mega's 8 KB SRAM.
