# Drag Strip Hardware

## Controller

- Arduino Mega 2560.
- USB serial connection to the Windows app at 115200 baud.
- Firmware sketch: `dragMC/dragMC.ino`.

James Cleave shared a reference test rig for a similar slot-car sensor project
that also used an Arduino Mega with multiple LM393 slot sensor boards on a
breadboard. That corroborates the Mega choice for this project: enough I/O for
many lane sensors and tree lights, straightforward USB serial, and simple
bench-testing before committing to permanent track wiring.

## Sensor Polarity

The LM393 slot sensors used on this track are active-HIGH:

- blocked beam: `HIGH`
- clear beam: `LOW`

The firmware setting is:

```cpp
constexpr bool SENSOR_IS_ACTIVE_LOW = false;
```

Because the active-HIGH configuration uses normal `INPUT` mode, disconnected
Mega inputs can float. During sensor testing, an unwired analog input may show
as blocked until a sensor output or an external pull-down gives it a stable
level.

## Sensor Wiring

Assume one Ethernet twisted pair per sensor output:

```text
LM393 sensor OUT  ---------------- Mega sensor input, such as A0
LM393 sensor GND  ---------------- Mega GND
                              |
                              +-- 10k pulldown resistor -- Mega GND
```

The 10k pulldown resistor belongs between the Mega input node and ground. In
other words, connect one side of the resistor to the same terminal or trace as
the sensor signal input, and connect the other side to the same ground reference
used by the sensor and Mega. This holds the active-HIGH input LOW when the
sensor output is disconnected, clear, or not actively driving HIGH.

Use the twisted pair as signal plus ground return for that sensor. Avoid using
one cable pair for two unrelated sensor signals, because the twist is most
useful when the signal wire and its return wire run together.

Sensor power can use another Ethernet pair, or multiple pairs in parallel if
the cable run is long:

```text
LM393 VCC  ---------------- Mega/controller +5V
LM393 GND  ---------------- Mega/controller GND
```

Keep sensor cables separated from track power, motor feeds, and high-current
tree-light wiring where practical.

## Optional Sensor-board Modification

James Cleave reported using a similar LM393 slot sensor board with a small
hardware change to improve detection of white or yellow guide flags. The change
shown in the reference photo replaces or bypasses a small surface-mount resistor
near the slotted opto sensor with a through-hole 330 ohm resistor.

Treat this as a field-proven troubleshooting option, not as the default build
until the exact board revision and resistor position are confirmed. Before
modifying all sensors:

1. Trace or photograph the board and confirm which SMD resistor is being
   replaced.
2. Modify one spare sensor first.
3. Verify the modified sensor in the Windows `Test Sensors` window with black,
   white, and yellow guide flags.
4. Compare the trigger point and stability against an unmodified sensor.

This 330 ohm board modification is separate from the 10k Mega input pulldown.
The pulldown defines the controller input when the sensor is not driving the
line; the 330 ohm change appears to alter the sensor board's optical/electrical
behavior.

## Sensor Pin Map

Each lane has four required sensors and two optional interval timers. The
interval timers are physically between stage and the speed trap and default to
disabled in dragWin.
Lane 1 has been fully wired and tested on A0 through A3. Sensor Test validated
all four inputs, and cardboard beam interruptions produced complete passes.

The Windows `Test Sensors` window displays a raw blocked-edge count and the
most recently completed raw blocked-pulse width for each input. These values
are collected before the firmware's 2 ms race debounce. Use them during moving
car tests on A2 and A3: the count confirms that a pulse reached the Mega, and
the pulse width shows whether it was long enough to pass the current debounce.

| Lane | Pre-stage | Stage | Interval 1 | Interval 2 | Speed trap | Finish |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | A0 | A1 | D2 | D3 | A2 | A3 |
| 2 | A4 | A5 | D4 | D5 | A6 | A7 |
| 3 | A8 | A9 | D6 | D7 | A10 | A11 |
| 4 | A12 | A13 | D8 | D9 | A14 | A15 |

Enable installed pairs per lane under **Configure > Race and track settings...
> Track**. Disabled interval inputs are ignored, so unwired `D2` through `D9`
cannot interfere with pass completion or track-clear detection.

## Track Mounting and Guide-flag Clearance

The track deck is 3/4 inch thick. With the current sensor mounting, the
infrared optical path is too far below the underside of the track for a normal
car guide flag to reach and interrupt it. This is a physical mounting-height
issue; the four lane 1 sensors and the complete timing workflow have already
been validated with cardboard beam interruptions.

Raise the optical path into the guide flag's reach by routing shallow pockets
for the sensor assemblies, fitting emitters and detectors with longer leads,
or combining both methods. Preserve clearance so the guide cannot strike the
components. Insulate and strain-relieve extended leads, then secure the final
alignment against vibration before collecting moving-car pulse widths.

### Recommended Sensor-height Modification

For the first prototype, reuse the original photo-interrupter rather than
assuming that a different part will fit the generic LM393 board. Desolder the
interrupter, extend its four terminals with short insulated solid wires, and
mount it higher or on the opposite side of the board. This preserves the known
optical behavior, slot clearance, polarity, and LM393 comparator response.
Provide a bracket or other mechanical support so the extended leads do not set
or maintain the sensor alignment.

Identify and label all four PCB connections by function before removing the
original part: LED anode, LED cathode, phototransistor collector, and
phototransistor emitter. Mounting a part on the opposite side of the PCB
mirrors its footprint, so reconnect by function rather than by physical pad
position. After modification, readjust the LM393 threshold if necessary and
confirm clear=`LOW` and blocked=`HIGH` in Sensor Test.

The Lite-On LTH-301-05 remains a possible substitute when additional lead
length is useful, but it is not a confirmed drop-in replacement. Its
manufacturer drawing specifies a 6.0 mm slot, 14.0 mm overall width, 10.45 mm
body length, 5.2 mm lead-row spacing, 2.54 mm spacing between adjacent leads,
and at least 9.12 mm of lead below the case. The 10.45 mm measurement is a body
dimension, not pin pitch. Verify that the 6.0 mm slot provides adequate guide
flag clearance and use insulated jumper leads if its footprint does not match
the board.

Do not assume that the existing interrupter is a particular H2010 variant from
an online summary. Generic parts and modules use that name inconsistently, and
commonly advertised H2010 LM393 modules have a 10 mm slot. Measure the actual
interrupter's slot, body, and hole centers with calipers before selecting a
replacement or routing the track. Prototype one assembly and validate it with
the Sensor Test edge counts and pulse durations before modifying the remaining
sensors.

LTH-301-05 manufacturer drawing:
<https://optoelectronics.liteon.com/upload/download/DS-55-92-0002/H301-05.pdf>

### Commercial Carrier-board Alternative

Trackmate Racing sells guide-flag sensor assemblies for either 1/2-inch or
3/4-inch tracks. Product photographs show a U-shaped photo-interrupter raised
on its leads above a passive carrier PCB, with the bulk of the electronics
located elsewhere. A separate carrier combines the pre-stage and stage
interrupters. This commercially validates the general raised-sensor approach
and could provide a ready-made alternative or a useful reference assembly.

Trackmate does not publish the interrupter part number, pinout, LED current
requirements, or phototransistor characteristics, so its assembly is not a
confirmed electrical replacement for the current LM393 sensors. It may require
an adapter or connection to a separate comparator board. Purchasing a sample
or designing a similar passive carrier would also introduce delay. Therefore,
keep the modification above as the immediate path to a working installation;
consider the Trackmate assembly later if the in-hand prototype is mechanically
unsatisfactory.

Trackmate reference assemblies:
<https://trackmateracing.com/shop/en/drag-racing/31-guide-flag-sensor.html>
<https://trackmateracing.com/shop/en/drag-racing/30-prestage-stage-guide-flag-sensor.html>

## Tree Light Pin Map

Each lane has seven light outputs: pre-stage, stage, amber 1, amber 2,
amber 3, green, and red.

| Lane | Pre-stage | Stage | Amber 1 | Amber 2 | Amber 3 | Green | Red |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 22 | 23 | 24 | 25 | 26 | 27 | 28 |
| 2 | 29 | 30 | 31 | 32 | 33 | 34 | 35 |
| 3 | 36 | 37 | 38 | 39 | 40 | 41 | 42 |
| 4 | 43 | 44 | 45 | 46 | 47 | 48 | 49 |

The Mega's D22-D53 double-row header begins with two +5V positions before
D22/D23. Follow the board's printed pin labels instead of counting from the end
of the connector. Connecting the first two tree LEDs to that +5V pair makes
them remain on continuously; an offset connection can also leave another light
permanently off.

### One-lane Tree Prototype

The first pegboard prototype uses lane 1 only. Obtain seven 470 ohm, 1/4 watt
resistors and use one separate resistor for each LED. Do not share one resistor
among multiple LEDs.

Wire each output as follows:

```text
D22 -> 470 ohm -> Pre-stage LED anode -> LED cathode -> GND
D23 -> 470 ohm -> Stage LED anode     -> LED cathode -> GND
D24 -> 470 ohm -> Amber 1 LED anode   -> LED cathode -> GND
D25 -> 470 ohm -> Amber 2 LED anode   -> LED cathode -> GND
D26 -> 470 ohm -> Amber 3 LED anode   -> LED cathode -> GND
D27 -> 470 ohm -> Green LED anode     -> LED cathode -> GND
D28 -> 470 ohm -> Red LED anode       -> LED cathode -> GND
```

After installing controller firmware 0.6.4 or newer, use **Diagnostics > Light
tree test...** to select a physical lane, switch each output individually, or
run all seven outputs in order. Closing the window turns every test output off;
lights otherwise retain their selected state until **All Off**, controller
reset, or power removal, including if serial communication is lost.

### Lane 1 Tree and Practice Validation

On August 4, 2026, the lane 1 pegboard tree was tested with controller firmware
0.6.4. **Diagnostics > Light tree test...** successfully switched D22 through
D28 individually and ran the complete seven-output diagnostic sequence. The
test initially exposed two LEDs connected to the header's +5V positions and an
incorrect D24 connection; correcting those three jumpers made every output
respond as expected.

A subsequent lane 1 practice test used the physical pre-stage sensor on A0 and
stage sensor on A1. With only lane 1 participating, blocking both beams armed
the start and produced the normal Tree sequence. Releasing stage before green
produced a red-light foul, while holding stage until green and then releasing
it produced a legal green-light launch. This validates the lane-selection,
physical staging inputs, staged delay, Tree output sequence, and immediate foul
detection together. Speed-trap and finish inputs are still required to complete
a pass rather than eventually report a DNF.

On a typical discrete LED, the longer lead is the anode. The shorter lead and
flat side of the body identify the cathode. The resistor may physically be on
either side of its LED as long as it remains in series. Firmware output `HIGH`
turns the corresponding light on.

With 470 ohm resistors, ordinary 5 V indicator LEDs draw approximately 4 to
6 mA each. The one-lane prototype can therefore be driven directly from the
Mega without a separate LED supply or driver IC. This advice applies to
individual indicator LEDs, not 12 V lamps, LED strips, or modules with unknown
internal wiring. Use transistor or ULN2803A drivers and a separate supply before
expanding to a complete four-lane tree.

### Trackmate Commercial Tree Option

For the first venue installation, consider purchasing a finished Trackmate
tree while retaining the one-lane pegboard tree as the firmware and wiring test
fixture. Prices observed on August 3, 2026 were:

- 16-inch LED tree: USD $335
- 6-inch LED tree: USD $145

The 16-inch tree is 15.5 inches high and 5.5 inches wide, with seven 10 mm LEDs
in each 1.3-inch light cluster. Trackmate includes a 24 V adapter and a 5-foot
cable. The smaller tree is 6 inches high and includes a 5-foot cable. Both are
advertised for Trackmate DP2000/DP3000 systems, so neither is a confirmed
drop-in electrical replacement for the LEDs connected directly to the Mega.

Before purchasing, ask Trackmate for:

1. The tree connector pinout.
2. Whether the lights use a common-positive or common-negative connection.
3. The current drawn by each independently controlled light group.
4. Confirmation that all LED current limiting is contained inside the tree.
5. Whether each light can be controlled by an open-collector output without a
   DP2000 or DP3000 controller.

Do not connect the tree's 24 V wiring directly to a Mega pin. If the tree is
common-positive and each channel is activated by switching its negative side,
two ULN2803A driver ICs may provide a simple interface for its 14 left/right
light channels. A common-negative tree would instead require suitable high-side
drivers. Confirm the actual pinout, polarity, and current before selecting
either circuit. The Trackmate tree is intended for a conventional two-lane
display; confirm whether two trees or a custom arrangement would be required
for the eventual four-lane installation.

Product references:
<https://trackmateracing.com/shop/en/drag-racing/25-16-inch-led-tree.html>
<https://trackmateracing.com/shop/en/drag-racing/26-led-tree-6-inch.html>

## Two-lane Mode

Two-lane mode uses physical lanes 1 and 4. Lanes 2 and 3 are ignored and their
race lights are forced off.

## Interval Timing

Interval 1 and Interval 2 report cumulative times from launch. dragWin also
calculates Interval 1-to-Interval 2, Interval 2-to-speed-trap, and
speed-trap-to-finish segments for practice and tournament reports.
