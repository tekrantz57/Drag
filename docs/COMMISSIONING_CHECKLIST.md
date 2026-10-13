# Four-Lane Commissioning Checklist

Use this worksheet during installation. It is a test record, not certification
of race timing. Do not rely on competition results until moving-car and
simultaneous-lane tests are repeatable. Keep a copy with the venue's wiring notes.

Venue: ____________________  Date: __________  Operator: ____________________

Drag version: ____________________  DragMC version: ____________________

Mega / clone model: ____________________  Serial port: ____________________

## 1. Before Power

- [ ] Disconnect power before changing wiring. Verify the documented Mega pin map.
- [ ] Sensor ground and controller ground are common; verify sensor power polarity.
- [ ] Each active-HIGH input has the documented 10k pulldown.
- [ ] Extended interrupter terminals are insulated and connected by function:
      LED anode/cathode and phototransistor collector/emitter, not mirrored pad position.
- [ ] Optical components are rigidly supported; leads do not support the assembly.
- [ ] Guide flags reach the optical path without striking it throughout their travel.
- [ ] Sensor wiring is separated from track/motor feeds where practical.
- [ ] Tree outputs use the appropriate current-limited LED circuit or driver.
      Do not connect commercial high-current lights directly to Mega GPIO.
- [ ] Check printed header labels: the two +5V positions are not D22 and D23.

## 2. Power And Configuration

- [ ] Connect using normal hardware startup, not Start-Demo.cmd.
- [ ] Confirm the connected controller is DragMC on a Mega 2560.
- [ ] Save version information from Help > Copy Version Information with this record.
- [ ] Select four lanes. Enable interval pairs only where both sensors are installed.
- [ ] Use BOTH_BLOCKED staging for normal race checks; IN_ORDER is a manual-test option.
- [ ] Record Tree type, race mode, and staged delay below.
- [ ] Measure speed-trap length between the speed-trap and finish sensing planes.
      Enter inches in Configure > Race and track settings > Track.

Mode: __________  Tree: __________  Staged delay (ms): __________

Speed-trap length (inches): __________  Measured by: ____________________

The setting is shared across lanes. Record each lane's distance below; if they
differ materially, resolve that before comparing speeds. Track length alone is
metadata and does not establish speed-trap distance.

## 3. Sensor Test, One Lane At A Time

Open Diagnostics > Sensor test. Verify each required sensor before running a pass.
Use edge-count differences from a baseline; counts need not start at zero.

| Lane | Pre-stage | Stage | Interval 1* | Interval 2* | Speed trap | Finish |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | A0 | A1 | D2 | D3 | A2 | A3 |
| 2 | A4 | A5 | D4 | D5 | A6 | A7 |
| 3 | A8 | A9 | D6 | D7 | A10 | A11 |
| 4 | A12 | A13 | D8 | D9 | A14 | A15 |

*Optional interval pair; leave disabled if not installed.

For each input:

- [ ] Clear beam reports LOW / clear; blocked beam reports HIGH / blocked.
- [ ] One deliberate interruption adds one blocked-edge count, not several.
- [ ] Releasing the beam displays a completed blocked-pulse duration.
- [ ] The corresponding input on other lanes does not change unexpectedly.
- [ ] Repeat with the actual guide flags, including different colors and materials.

## 4. Tree Test, One Lane At A Time

Open Diagnostics > Light tree test. Toggle each output and run the sequence.
Verify lane selection, all seven lamps, and All Off. Close the test before practice.

| Lane | Pre-stage | Stage | Amber 1 | Amber 2 | Amber 3 | Green | Red | Checked |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | D22 | D23 | D24 | D25 | D26 | D27 | D28 | ______ |
| 2 | D29 | D30 | D31 | D32 | D33 | D34 | D35 | ______ |
| 3 | D36 | D37 | D38 | D39 | D40 | D41 | D42 | ______ |
| 4 | D43 | D44 | D45 | D46 | D47 | D48 | D49 | ______ |

Test lamps retain their state until All Off, test-window closure, controller
reset, or power removal. Serial disconnection alone does not turn them off.

## 5. Individual-Lane Practice Passes

Select only the lane being tested, then Arm Practice Run. This is a hardware
test; Generate demo practice run does not validate any sensor.

- [ ] Block pre-stage and stage together and keep stage blocked through the delay.
- [ ] Verify the selected Tree sequence and a legal launch after green.
- [ ] Separately verify an early stage release produces a red-light foul.
- [ ] Run a moving car through speed trap then finish; verify a completed pass.
- [ ] If installed, verify both interval times and their physical order.
- [ ] Repeat at representative speeds; record the shortest observed raw pulse.
- [ ] Compare blocked-edge increments with the number of physical passes.
- [ ] Check RT, ET, and speed for consistency; compare with independent timing
      where available. A completed pass alone does not prove measurement accuracy.

The firmware requires a stable 2 ms transition for race acceptance. A shorter
raw pulse may appear in diagnostics but be rejected by race timing. Stop and
record misses or marginal pulses; do not change debounce blindly.

| Lane | Trap distance (in) | Passes attempted / completed | Shortest trap pulse (us) | Shortest finish pulse (us) | Legal / foul checked |
| --- | --- | --- | --- | --- | --- |
| 1 | ______ | ______ / ______ | ______ | ______ | ______ |
| 2 | ______ | ______ / ______ | ______ | ______ | ______ |
| 3 | ______ | ______ / ______ | ______ | ______ | ______ |
| 4 | ______ | ______ / ______ | ______ | ______ | ______ |

Optional intervals: record each sensor's shortest pulse and distance from stage:

Lane 1: I1 __________ / __________  I2 __________ / __________

Lane 2: I1 __________ / __________  I2 __________ / __________

Lane 3: I1 __________ / __________  I2 __________ / __________

Lane 4: I1 __________ / __________  I2 __________ / __________

## 6. Simultaneous Passes And Tournament Workflow

Proceed only after all four lanes pass their individual tests.

- [ ] Test lanes 1 and 4 together in two-lane mode.
- [ ] Test all four participating lanes together in four-lane mode.
- [ ] Verify the start waits for every participating lane to stage.
- [ ] Verify each lane receives its own RT, ET, outcome, and installed intervals.
- [ ] Repeat near-simultaneous finishes and differently spaced finishes.
- [ ] Confirm results remain available for confirmation with no partial output,
      frozen UI, missing lanes, or unexpected cross-lane changes.
- [ ] Run a small hardware tournament through lane choice, confirmation,
      advancement, and the final report. Check that source is Controller.
- [ ] Check HTML and enabled CSV/JSON exports against displayed results.
- [ ] Exercise bracket dial-ins, breakouts, and a bye; agree venue rules before racing.
- [ ] Make a database backup and retain test logs with private data removed if shared.

Four-lane attempts / complete results: ______ / ______

Observed RT / ET / speed discrepancies: ______________________________________

## 7. Stop Points And Venue Sign-Off

Stop testing and record the problem if a moving pass is missed, pulses are
marginal, results are incomplete, optics move, or unrelated inputs change.
Use Help > Report a beta bug with version information and reproduction steps.

Decisions still required: staging timeout, bye behavior, and advancement when
too few cars have a finish placement. Current staging waits indefinitely;
bye cars advance even after a foul/DNF, and DNFs are unplaced.

Remaining wiring / mounting issues: _________________________________________

Venue rule decisions: _______________________________________________________

Next test and responsible person: ___________________________________________

Operator: ____________________  Venue owner: ____________________

See [Hardware](https://github.com/tekrantz57/Drag/blob/master/HARDWARE.md),
[Tournament behavior](https://github.com/tekrantz57/Drag/blob/master/docs/TOURNAMENTS.md), and
[Troubleshooting](https://github.com/tekrantz57/Drag/blob/master/docs/TROUBLESHOOTING.md). No sign-off here removes the project's
experimental-beta limitations.
