# Drag Strip Hardware

## Controller

- Arduino Mega 2560.
- USB serial connection to the Windows app at 115200 baud.
- Firmware sketch: `dragMC/dragMC.ino`.

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
