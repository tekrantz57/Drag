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

Each lane currently has four sensors: pre-stage, stage, speed trap, and finish.

| Lane | Pre-stage | Stage | Speed trap | Finish |
| --- | --- | --- | --- | --- |
| 1 | A0 | A1 | A2 | A3 |
| 2 | A4 | A5 | A6 | A7 |
| 3 | A8 | A9 | A10 | A11 |
| 4 | A12 | A13 | A14 | A15 |

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

## Future Split Sensors

There may be two intermediate/split sensors per lane later. Treat them as
optional diagnostic or split-timing sensors until the venue's exact use is
confirmed. Adding them for all four lanes would raise the sensor count from 16
to 24 and require expanding the pin map beyond `A0` through `A15`.
