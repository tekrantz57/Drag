# Drag Strip TODO

## Track sensor validation

- Run moving-car tests on lane 1 A2 and A3 and record the raw pulse widths
  reported by the Sensor Test window at representative speeds.
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
- Reserve Mega D2-D9 as the tentative eight-input map for two future split
  sensors per lane, pending confirmation of their locations and purpose.

## Venue rule decisions

- Decide whether to enable a staging timeout after one or more lanes stage.
  The current controller waits indefinitely until every participating lane is
  staged, then uses the operator-configured staged delay.
- Decide whether deep staging is allowed. The current start requires both the
  pre-stage and stage sensors to remain blocked through the staged delay.
- Confirm whether a bye car must stage and take the Tree cleanly. The current
  tournament model guarantees its advancement even after a red light or DNF.
- Decide how a four-lane heat should fill a second advancing position when
  fewer than two cars receive controller placements. DNFs are intentionally
  unplaced because the current sensor set cannot order them defensibly.

## Intermediate track sensors

- Investigate the two intermediate sensors expected on the local track and
  compare notes with the 4-lane slot car drag-strip setup in England.
- Likely future per-lane sensor model:
  - Pre-stage
  - Stage
  - Split/intermediate 1
  - Split/intermediate 2
  - Speed trap
  - Finish
- Treat intermediate sensors as optional split/diagnostic timing points at
  first. Do not make them required for staging, starting, winner calculation,
  or bracket advancement until the venue's exact use is understood.
- If implemented for all four lanes, sensor count rises from 16 to 24. The Mega
  2560 has enough pins, but the sensor pin map will need to expand beyond
  `A0`-`A15`.
- Possible protocol shape:
  - `EVENT:LANE:<n>:SPLIT_1`
  - `EVENT:LANE:<n>:SPLIT_2`
- Possible Windows display/reporting:
  - split times from launch or green
  - diagnostic missed-sensor visibility
  - richer race reports
