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

## Two-lane Mode

Two-lane mode uses physical lanes 1 and 4. Lanes 2 and 3 are ignored and their
race lights are forced off.

## Interval Timing

Interval 1 and Interval 2 report cumulative times from launch. dragWin also
calculates Interval 1-to-Interval 2, Interval 2-to-speed-trap, and
speed-trap-to-finish segments for practice and tournament reports.
