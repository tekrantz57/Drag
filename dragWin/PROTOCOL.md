# Drag Strip Serial Protocol

Each message is one printable ASCII line terminated by `LF`. Bytes outside
ASCII `0x20` through `0x7E` are not permitted inside a message.

The serial link runs at 115200 baud.

```text
PART:PART:PART:XX
```

`XX` is the two-digit uppercase hexadecimal XOR of every ASCII byte before the
final checksum colon. The checksum excludes that colon and the line ending.

```text
PING:10
ACK:PING:63
```

## Race modes

- `HEADS_UP`: all four lane trees run together. Legal finishers are ranked by
  finish-line crossing order.
- `BRACKET`: each lane has a dial-in. The slowest dial starts its tree first;
  every other lane is delayed by `slowest dial - lane dial`. A legal
  non-breakout finisher beats a breakout. If every eligible finisher breaks
  out, the smallest breakout wins. A foul lane is never eligible.

Dial-ins use integer milliseconds on the wire. The controller accepts
`100` through `60000`.

The active lane count is either `2` or `4`. Two-lane mode uses physical lanes
1 and 4; lanes 2 and 3 are ignored and their lights are forced off.

Track and speed-trap lengths use integer thousandths of an inch. They are
updated together, and the speed-trap length must be shorter than the track.
Track length is race metadata; speed-trap length is used for MPH calculation.

Sensor status fields use logical blocked state, not raw voltage. With the
current active-HIGH LM393 sensors, `1` means the beam is blocked and `0` means
the beam is clear.

## Tree modes

- `FULL`: amber 1, amber 2, amber 3, and green are separated by 500 ms.
- `PRO`: all three ambers illuminate together, followed by green after 400 ms.

The staged delay is independent of Tree cadence. It controls the wait from all
participating lanes being fully staged to the first amber and accepts `0`
through `5000` milliseconds. The default is `500` ms. If any participating car
leaves the stage beam during this delay, the start is aborted and staging
begins again without assigning a red light.

## Windows-to-controller commands

- `PING`
- `STATUS`
- `SENSOR_DIAGNOSTICS`
- `RESET_SENSOR_DIAGNOSTICS`
- `RESET`
- `SET:LANES:<2|4>`
- `SET:HEAT_LANES:<comma-separated-physical-lanes>`
- `SET:DISTANCES:<track-inches-x1000>:<trap-inches-x1000>`
- `SET:MODE:HEADS_UP`
- `SET:MODE:BRACKET`
- `SET:TREE:FULL`
- `SET:TREE:PRO`
- `SET:STAGED_DELAY:<0-5000-milliseconds>`
- `SET:DIAL:<lane>:<milliseconds>`

Race, Tree, lane, distance, staged-delay, and dial changes are accepted only
outside staging and an active race.

## Controller-to-Windows messages

- `HELLO:DRAG_MC:<firmware-version>:PROTO:<protocol-version>:MCU:MEGA2560:LANES:<2|4>:HEAT_LANES:<list>`
- `HEARTBEAT:<controller-millis>:SEQ:<last-event-sequence>:STATE:<state>`
- `ACK:PING`
- `ACK:RESET`
- `ACK:RESET_SENSOR_DIAGNOSTICS`
- `ACK:SET:LANES:<2|4>`
- `ACK:SET:HEAT_LANES:<comma-separated-physical-lanes>`
- `ACK:SET:DISTANCES:<track-inches-x1000>:<trap-inches-x1000>`
- `ACK:SET:MODE:<mode>`
- `ACK:SET:TREE:<FULL|PRO>`
- `ACK:SET:STAGED_DELAY:<milliseconds>`
- `ACK:SET:DIAL:<lane>:<milliseconds>`
- `STATUS:TREE:<state>:MODE:<mode>:LANES:<2|4>:HEAT_LANES:<list>:TRACK_IN_X1000:<value>:TRAP_IN_X1000:<value>`
- `STATUS:SETTINGS:TREE:<FULL|PRO>:STAGED_DELAY_MS:<milliseconds>`
- `STATUS:LANE:<lane>:DIAL_MS:<milliseconds>:PRESTAGE:<0|1>:STAGE:<0|1>:SPEED_TRAP:<0|1>:FINISH:<0|1>:FOUL:<0|1>:FINISHED:<0|1>`
- `SENSOR:<lane>:<name>:RAW:<0|1>:EDGES:<count>:PULSE_US:<microseconds|NONE>`
- `EVENT:TREE:<state>`
- `EVENT:TREE:STAGING_ABORTED`
- `EVENT:LANE:<lane>:AMBER_<1|2|3>`
- `EVENT:LANE:<lane>:PRO_AMBERS`
- `EVENT:LANE:<lane>:GREEN`
- `EVENT:LANE:<lane>:FOUL`
- `EVENT:LANE:<lane>:REACTION_US:<microseconds>`
- `EVENT:LANE:<lane>:SPEED_TRAP`
- `RESULT:LANE:<lane>:ELAPSED_US:<microseconds>`
- `RESULT:LANE:<lane>:VALID`
- `RESULT:LANE:<lane>:BREAKOUT_US:<microseconds>`
- `RESULT:LANE:<lane>:SPEED_MPH_X100:<hundredths-of-mph>`
- `RESULT:LANE:<lane>:DNF`
- `RESULT:PLACE:<place>:LANE:<lane>`
- `RESULT:WINNER:LANE:<lane>`
- `RESULT:NO_WINNER`
- `ERROR:CHECKSUM`
- `ERROR:COMMAND:<command>`
- `ERROR:STATE:RACE_ACTIVE`
- `ERROR:VALUE:<setting>`

`EVENT` and `RESULT` frames include trailing metadata:

```text
:SEQ:<sequence>:MS:<controller-millis>
```

The sequence number increments for each `EVENT` or `RESULT` frame and resets
when the controller resets. Windows should treat these fields as metadata; the
race meaning is still in the leading fields.

Sensor diagnostics are collected from raw input transitions before the 2 ms
race debounce is applied. `EDGES` counts clear-to-blocked transitions.
`PULSE_US` is the duration of the most recently completed blocked pulse, or
`NONE` if no pulse has completed since startup or the last diagnostic reset.
Counters saturate rather than wrapping and can be cleared without resetting
the race controller.

`REACTION_US` is sent for both legal and foul starts; a negative value is the
amount by which the lane left before green. It precedes the lane's `FOUL`
event. At race completion, the controller sends authoritative `PLACE` messages.
In bracket mode, legal finishers rank first by stripe crossing, breakouts rank
next by least breakout, and red lights rank next by smallest negative-reaction
magnitude. DNFs are not assigned a place.
