# Troubleshooting

## Controller Port Does Not Appear

Close Arduino Serial Monitor and any other program using the Mega, reconnect
USB, select **Refresh ports**, and choose the resulting COM port in DragWin.
Only one process can own the serial port at a time.

Under Wine, map the Linux serial device into the Wine prefix and select that COM
name in DragWin. For example:

```bash
ln -s /dev/ttyACM0 ~/.wine/dosdevices/com33
```

The COM number is local configuration, not a Drag requirement. Testing used
`COM33` on Intel Linux and `COM1` on the ARM64 Rock 5B. The Linux account must
have permission to open the underlying `/dev/ttyACM*` or `/dev/ttyUSB*` device.

## Sensor Randomly Shows Blocked

The installed LM393 sensors are active-HIGH. An unwired Mega input can float
and appear blocked. Connect the sensor output and common ground, or install the
documented 10k pulldown from the Mega input node to ground. Disable optional
interval sensors for lanes where they are not physically installed.

## Sensor Test Changes But A Pass Does Not Complete

Confirm that only the intended lanes participate and that the required beams
occur in race order: staging, launch, speed trap, then finish. In
`BOTH_BLOCKED`, pre-stage and stage must be blocked together. `IN_ORDER` is a
manual-testing alternative that latches pre-stage before stage.

Use Sensor Test to inspect each input's raw blocked-edge count and most recent
blocked-pulse duration. A pulse shorter than the firmware's 2 ms debounce is
recorded diagnostically but is not accepted as a race transition.

## Tree Lights Stay On Or Off During Light Test

The Mega's D22-D53 double-row header has two +5V positions immediately before
D22 and D23. Use the labels printed on the board instead of treating the end of
the connector as D22/D23. LEDs accidentally connected to the +5V pair remain on
regardless of Light Tree Test commands, while a jumper on the wrong digital pin
may remain off or respond to a different control.

Confirm questionable outputs by moving a known-working LED and resistor to the
labeled pin, or measure the pin relative to Mega GND while toggling it. Keep the
Light Tree Test window open during measurement; after it closes, D22 and D23
return to their normal pre-stage and stage sensor behavior.

## Status Or Diagnostic Frames Are Missing

Open Controller Diagnostics or press the appropriate status command in the
main window. The serial log records traffic that was actually requested or sent;
simply opening the serial log does not continuously request every diagnostic
frame.

## Firmware Update Cannot Identify The Existing Sketch

The updater can recover a Mega with a normal USB bootloader even if it runs a
different sketch. Physically verify that the selected port belongs to the Mega
before using the manual confirmation path. The updater cannot repair a missing
or damaged bootloader; that requires an ISP programmer and Arduino tooling.

See [Controller Firmware Updates](CONTROLLER_FIRMWARE_UPDATE.md) for uploader
selection, validation, recovery boundaries, and Wine results.

## Voice Is Unavailable Under Wine

Voice announcements depend on SAPI voices installed in the Wine prefix. Speech
is optional and disabled by default; unavailable speech must not affect race
timing or controller operation.

## Sharing Diagnostics

Serial logs, reports, databases, and screenshots can contain racer names, car
names, local paths, and controller activity. Remove private information before
sharing excerpts.
