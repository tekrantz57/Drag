# Arduino Due Light Tree Test

This diagnostic-only sketch lets DragWin operate the existing four-lane Light
Tree Test on an Arduino Due. It is not race firmware and has no sensor, staging,
timing, result, or tournament behavior.

For the newer experimental Due race/ISR build of the shared `dragMC` sketch,
see [Due Race Firmware](../docs/DUE_PORT.md). This light-only sketch remains
available for isolated output testing.

## Upload

1. Install **Arduino SAM Boards (32-bit ARM Cortex-M3)** in Arduino IDE Boards
   Manager.
2. Select **Arduino Due (Programming Port)**.
3. Connect the computer to the Due's **Programming** USB port, not its Native
   USB port.
4. Open `dragMCDueLightTest.ino` and upload it.
5. In DragWin, connect to the resulting COM port.
6. Open **Diagnostics > Light tree test...**.

The diagnostic sketch identifies itself as `DRAG_MC_DUE_LIGHT_TEST` on
`SAM3X8E`. Current DragWin can migrate it to the experimental Due **race**
firmware with **File > Update Controller Firmware** through the Programming
port; this replaces the light-only sketch. Mega packages remain incompatible.
Older DragWin builds have a Mega-only updater. To retain/upload this diagnostic
sketch, continue using Arduino IDE as described above.

## Light Pins

The test deliberately preserves the Mega Tree mapping:

| Lane | Pre-stage | Stage | Amber 1 | Amber 2 | Amber 3 | Green | Red |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | D22 | D23 | D24 | D25 | D26 | D27 | D28 |
| 2 | D29 | D30 | D31 | D32 | D33 | D34 | D35 |
| 3 | D36 | D37 | D38 | D39 | D40 | D41 | D42 |
| 4 | D43 | D44 | D45 | D46 | D47 | D48 | D49 |

The Due uses 3.3 V logic and permits no more than 3.3 V on an I/O pin. The
documented one-lane prototype LEDs may be connected from each output through
its own 470 ohm resistor and LED to ground. Use external drivers for commercial
Tree hardware or higher-current lamps.

Selected lights retain their commanded state until they are toggled off,
**All Off** is pressed, the diagnostic window closes normally, `RESET` is sent,
or board power is removed.
