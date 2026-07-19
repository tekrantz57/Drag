# Drag Strip TODO

## Intermediate track sensors

- Investigate the two intermediate sensors seen on the local track and compare
  notes with the 4-lane slot car drag-strip setup in England.
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
