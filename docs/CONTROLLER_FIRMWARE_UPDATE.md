# Controller Firmware Updates

DragWin can install its bundled DragMC application firmware on an Arduino Mega
2560 through the board's normal USB serial bootloader.

## Operator Workflow

1. Connect the Mega by USB and select its COM port in DragWin.
2. Close Practice Pass Results. Tournament and Sensor Test windows must also
   be closed before returning to the main window.
3. Choose **File > Update Controller Firmware...**.
4. Verify the displayed board, DragMC version, and COM port.
5. If DragWin cannot identify the existing sketch, physically confirm that the
   selected port belongs to a Mega 2560 or compatible clone before continuing.
6. Keep USB connected while DragWin writes, verifies, reconnects, and requests
   the expected DragMC identity.

The update has three possible outcomes:

- **Updated:** avrdude verified flash and DragWin received the expected
  `HELLO:DRAG_MC:<version>:...:MCU:MEGA2560` identity.
- **Written but not confirmed:** avrdude verified flash, but DragWin did not
  receive the expected identity within 20 seconds. Check the selected port and
  power-cycle the Mega.
- **Failed:** no successful upload was reported. DragWin shows the useful final
  avrdude output and attempts to reopen the previous serial connection.

## Supported Recovery

The updater supports a Mega that has its normal bootloader but currently runs
no DragMC sketch, a different sketch, or an application damaged by an
interrupted upload. Compatible clones are supported when their USB-to-serial
interface exposes a working COM port and their bootloader implements the Mega
`wiring` upload protocol.

The updater cannot repair a missing or damaged bootloader. Restore that with an
ISP programmer and Arduino tooling before using DragWin. The normal updater
never writes a `with_bootloader.hex` image and never disables the ATmega2560
signature check or flash verification.

## Uploader Acquisition

DragWin uses `avrdude.exe` and `avrdude.conf` as a matched pair. It checks, in
order:

1. `DRAGWIN_AVRDUDE_PATH` and optional `DRAGWIN_AVRDUDE_CONFIG` development
   overrides.
2. `%LOCALAPPDATA%\dragWin\Tools\avrdude\8.0.0-arduino1`.
3. avrdude installations under Arduino package directories.
4. The pinned official Arduino Windows archive, downloaded only after the
   operator approves the firmware-update confirmation.

The official archive's byte count and SHA-256 are pinned in source and checked
before extraction. avrdude is GPL-licensed; source and license information are
available from [the avrdude project](https://github.com/avrdudes/avrdude). The
tool archive is supplied by [Arduino Downloads](https://downloads.arduino.cc/).

## Developer Packaging

Firmware versioning is defined in `dragMC/FirmwareVersion.h`. Build the current
Mega package from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\tools\Build-ControllerFirmware.ps1 `
  -ArduinoCliPath "C:\Program Files\Arduino CLI\arduino-cli.exe"
```

The script:

- requires the installed `arduino:avr` core;
- compiles `arduino:avr:mega:cpu=atmega2560`;
- selects only `dragMC.ino.hex`;
- validates the version source and captures the core version;
- records the image byte count and SHA-256;
- writes `dragMC/dist/DragMC-mega-<version>.dragfw`; and
- leaves ignored build intermediates under `artifacts/controller-firmware`.

The `.dragfw` ZIP contains exactly `manifest.json` and `dragMC.ino.hex`.
DragWin validates its product, board profile, MCU, uploader settings, image
name, size, SHA-256, Intel HEX records, checksums, and EOF before releasing the
COM port. Publishing fails when the package matching `FirmwareVersion.h` is
absent. Build and commit the generated package with the exact firmware source
that produced it.
