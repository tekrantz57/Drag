# Drag Strip Serial Protocol

Each message is one printable ASCII line terminated by `LF`. Bytes outside
ASCII `0x20` through `0x7E` are not permitted inside a message.

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

## Windows-to-controller commands

- `PING`
- `STATUS`
- `RESET`
- `SET:LANES:<2|4>`
- `SET:DISTANCES:<track-inches-x1000>:<trap-inches-x1000>`
- `SET:MODE:HEADS_UP`
- `SET:MODE:BRACKET`
- `SET:DIAL:<lane>:<milliseconds>`

Mode and dial changes are accepted only while waiting for staging or waiting
for the track to clear.

## Controller-to-Windows messages

- `ACK:PING`
- `ACK:RESET`
- `ACK:SET:LANES:<2|4>`
- `ACK:SET:DISTANCES:<track-inches-x1000>:<trap-inches-x1000>`
- `ACK:SET:MODE:<mode>`
- `ACK:SET:DIAL:<lane>:<milliseconds>`
- `STATUS:TREE:<state>:MODE:<mode>:LANES:<2|4>:TRACK_IN_X1000:<value>:TRAP_IN_X1000:<value>`
- `STATUS:LANE:<lane>:DIAL_MS:<milliseconds>:PRESTAGE:<0|1>:STAGE:<0|1>:FOUL:<0|1>:FINISHED:<0|1>`
- `EVENT:TREE:<state>`
- `EVENT:LANE:<lane>:AMBER_<1|2|3>`
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
