# DragWin Mega 2560 Firmware Flashing Handoff

## Purpose

Add controller firmware installation and update support to DragWin, modeled on
the working YATSS firmware updater but specialized for the Arduino Mega 2560.

The desired operator experience is:

1. Select the Mega's COM port.
2. Choose `File > Update Controller Firmware...`.
3. Confirm the board and bundled DragMC version.
4. DragWin closes its serial connection, uploads the packaged firmware, then
   reconnects and verifies the expected `HELLO` message.

This should support:

- An existing DragMC installation.
- A Mega 2560 with its normal bootloader but no DragMC sketch.
- Official Arduino Mega 2560 boards and compatible clones that expose a usable
  serial port.

It should not claim to recover an ATmega2560 whose bootloader is missing or
damaged. That case requires an ISP programmer and is outside the normal DragWin
workflow.

## Existing Drag Project Context

- Firmware: `dragMC/dragMC.ino`
- Windows app: `dragWin`
- Target framework: .NET 9 WinForms
- Controller: Arduino Mega 2560
- Arduino FQBN: `arduino:avr:mega:cpu=atmega2560`
- Normal serial link: 115200 baud
- Current firmware identity:

  ```text
  HELLO:DRAG_MC:<firmware-version>:PROTO:<protocol-version>:MCU:MEGA2560:LANES:<2|4>:HEAT_LANES:<list>
  ```

- `DragSerialClient` already has explicit `Connect` and `Disconnect` methods.
  The updater must disconnect it before invoking an uploader and reconnect it
  afterward.
- `MainForm` already tracks the selected COM port, controller readiness,
  practice/tournament activity, and the latest `HELLO`/heartbeat times.

The current sketch declares its version directly:

```cpp
constexpr char FIRMWARE_VERSION[] = "0.6.0";
```

For reliable packaging, either parse that declaration in the build script or
move the version to a small shared firmware header. The latter matches YATSS
and reduces the risk that the package version and reported version diverge.

## Recommended Scope

Implement one board profile and one update package:

| Field | Value |
| --- | --- |
| Product | `DRAG_MC` |
| Board profile | `ARDUINO_MEGA_2560` |
| Display name | `Arduino Mega 2560` |
| MCU | `atmega2560` |
| Arduino FQBN | `arduino:avr:mega:cpu=atmega2560` |
| Uploader | `avrdude` |
| Upload protocol | `wiring` |
| Upload speed | `115200` |
| Firmware image | Application-only Intel HEX |

Do not package the `with_bootloader.hex` image for normal updates. Uploading
through the existing Mega bootloader needs only the application `.hex` and
must preserve the installed bootloader.

Do not require a particular USB VID/PID. Official Mega boards have known USB
identities, but many compatible boards use CH340, FTDI, or other USB-to-serial
devices. The operator-selected COM port plus an `atmega2560` signature check
from `avrdude` is the practical compatibility boundary.

## Firmware Package

Use a Drag-specific extension such as `.dragfw`. It can be a ZIP containing:

```text
manifest.json
dragMC.ino.hex
```

Suggested manifest:

```json
{
  "formatVersion": 1,
  "product": "DRAG_MC",
  "firmwareVersion": "0.6.0",
  "boardProfile": "ARDUINO_MEGA_2560",
  "boardDisplayName": "Arduino Mega 2560",
  "mcu": "atmega2560",
  "uploaderBackend": "avrdude",
  "arduinoFqbn": "arduino:avr:mega:cpu=atmega2560",
  "arduinoCoreVersion": "1.8.8",
  "uploadProtocol": "wiring",
  "uploadBaud": 115200,
  "imageFile": "dragMC.ino.hex",
  "imageSizeBytes": 123456,
  "sha256": "<uppercase SHA-256>"
}
```

Package validation should occur before DragWin releases the COM port:

- Exactly the supported package format.
- Product is exactly `DRAG_MC`.
- Board profile, MCU, backend, protocol, and baud match the supported Mega
  profile.
- Image filename is a simple leaf filename ending in `.hex`.
- Image exists, is nonempty, and has a conservative size limit.
- Byte count and SHA-256 match the manifest.
- Intel HEX has valid-looking records and an EOF record. SHA-256 remains the
  primary integrity check; a lightweight HEX format check gives better errors
  for an accidentally packaged file.

The package is not a security signature. It detects corruption and packaging
mistakes, but anyone able to replace both the package and manifest can replace
the hash. Code signing could be considered later if distribution risk warrants
it.

## Build and Packaging Script

Add a repository script such as:

```text
tools/Build-ControllerFirmware.ps1
```

Recommended build command:

```powershell
arduino-cli compile `
  --fqbn arduino:avr:mega:cpu=atmega2560 `
  --output-dir artifacts/controller-firmware/mega-build `
  dragMC
```

Using `--output-dir` makes the generated `.hex` available without depending on
Arduino's temporary build cache. The script should:

1. Locate Arduino CLI or accept an explicit path.
2. Confirm that `arduino:avr` is installed and capture its version.
3. Read the DragMC firmware version.
4. Compile for the Mega 2560.
5. Select `dragMC.ino.hex`, never `dragMC.ino.with_bootloader.hex`.
6. Calculate its byte count and SHA-256.
7. Create the manifest and `.dragfw` ZIP.
8. Replace stale versioned DragMC packages under `dragMC/dist`.
9. Leave build intermediates under ignored `artifacts/controller-firmware`.

The generated package should be committed with the exact firmware source that
created it. A publish-time MSBuild target should fail if no matching Mega
package exists.

Add the package to `dragWin/dragWin.csproj`:

```xml
<ItemGroup>
  <Content Include="..\dragMC\dist\*.dragfw">
    <Link>Firmware\%(Filename)%(Extension)</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </Content>
</ItemGroup>
```

Confirm the relative path when implementing; `dragWin.csproj` is one directory
below the repository root.

## Uploader Strategy

### Recommended production path: direct avrdude

Use `avrdude` directly so end users do not need Arduino IDE, Arduino CLI, or
the entire AVR board core installed.

The Arduino AVR platform defines the Mega 2560 upload as:

- MCU: `atmega2560`
- Protocol: `wiring`
- Port: selected COM port
- Speed: 115200
- Input format: Intel HEX

Equivalent arguments:

```text
-C <avrdude.conf>
-p atmega2560
-c wiring
-P <COM port>
-b 115200
-D
-U flash:w:<firmware.hex>:i
```

Keep verification enabled. Do not add avrdude's no-verify option. A normal
successful upload therefore checks the programmed flash before DragWin performs
its own protocol-level verification.

Use `ProcessStartInfo.ArgumentList` rather than constructing one quoted command
string. Capture both stdout and stderr, show useful progress, impose a
reasonable timeout, and include the final output lines in any failure message.

### Uploader acquisition

Follow YATSS's provider pattern:

1. Respect a development override such as `DRAGWIN_AVRDUDE_PATH`.
2. Reuse a previously approved per-user cache under:

   ```text
   %LOCALAPPDATA%\dragWin\Tools\avrdude\<version>
   ```

3. Search Arduino IDE/CLI package locations for a compatible installed
   `avrdude.exe` and its matching `avrdude.conf`.
4. If neither exists, ask permission before downloading an official Arduino
   avrdude tool archive.

Pin the exact official URL, archive byte count, and SHA-256 in source. Verify
all three before extraction. Treat `avrdude.exe` and `avrdude.conf` as a matched
pair from the same archive.

Do not silently download a tool after the COM port has been released. Acquire
and validate everything first, then begin the firmware update.

`avrdude` is GPL-licensed. The simplest distribution model is the one used by
YATSS for its uploaders: do not bundle the executable; locate an installed copy
or download the official archive after the user approves it. Include upstream
license/source links in DragWin's firmware-update documentation.

### Development fallback: Arduino CLI

During initial implementation, Arduino CLI can prove the packaged image and
COM-port workflow:

```powershell
arduino-cli upload `
  --fqbn arduino:avr:mega:cpu=atmega2560 `
  --port COM3 `
  --input-file dragMC.ino.hex `
  --verify
```

This is useful for development but is not the preferred end-user dependency.
Arduino CLI requires its own installation and an installed AVR platform/tools.

## DragWin Workflow

Add `File > Update Controller Firmware...` and permit it only while no practice
pass, tournament heat, sensor-test session, or other controller operation is
active.

Recommended sequence:

1. Capture the selected COM port.
2. Load and validate the bundled `.dragfw` package.
3. If a current `HELLO` is available, verify `DRAG_MC` and `MCU:MEGA2560`.
4. If no `HELLO` is available, allow manual confirmation for a blank-sketch
   Mega on the selected port.
5. Display firmware version, board, COM port, and uploader acquisition notice.
6. Warn the operator that controller outputs will be unavailable during the
   update and that the USB cable must remain connected.
7. Acquire and validate `avrdude` before disconnecting DragWin.
8. Disable conflicting UI and close child controller-operation windows.
9. Call `DragSerialClient.Disconnect()` and confirm the port is released.
10. Extract the packaged HEX to a unique temporary directory.
11. Run `avrdude` against the selected COM port.
12. Delete the temporary directory.
13. Reconnect `DragSerialClient` to the same port.
14. Wait up to approximately 20 seconds for:

    ```text
    HELLO:DRAG_MC:<expected-version>:...:MCU:MEGA2560:...
    ```

15. Report one of three outcomes:

    - Upload and identity verification succeeded.
    - Upload succeeded, but expected DragMC identity was not received.
    - Upload failed; show the useful avrdude detail and reconnect if possible.

The second outcome matters. A successful avrdude exit proves that bytes were
written and verified, but the post-flash `HELLO` proves that the intended
application started and speaks the expected Drag protocol.

Refactor `DragSerialClient` or `MainForm` so the most recent parsed controller
identity is available as structured data. Do not verify the result by searching
raw log text.

## Blank and Recovery Cases

Terminology should be explicit in UI and documentation:

- **No DragMC sketch / different sketch:** supported if the Mega bootloader is
  intact. Select its COM port, manually confirm Mega 2560, and upload.
- **Interrupted application upload:** normally retryable because the bootloader
  is separate from the application area.
- **Missing or damaged bootloader:** not supported through the USB serial
  updater. Restore the Mega bootloader with an ISP programmer and Arduino
  tooling, then retry DragWin.
- **Wrong board on selected COM port:** avrdude's ATmega2560 signature check
  should fail before writing. DragWin must not weaken or override that check.
- **Clone Mega:** supported when its USB-serial interface provides a working COM
  port and its bootloader implements the expected Mega `wiring` protocol.

Never offer the `with_bootloader.hex` package through the normal COM-port
updater. Installing a bootloader is a separate ISP operation with different
hardware, fuse, and lock-bit consequences.

## Suggested New Files

Names can follow DragWin conventions, but the responsibilities should remain
separate:

```text
dragWin/ControllerFirmwarePackage.cs
dragWin/ControllerFirmwareFlasher.cs
dragWin/AvrDudeProvider.cs
dragWin/ArduinoMegaFirmwareFlasher.cs
dragWin/FirmwareUpdateProgressForm.cs
tools/Build-ControllerFirmware.ps1
docs/CONTROLLER_FIRMWARE_UPDATE.md
```

YATSS contains working examples of each role:

- Package parsing, size/hash validation, and bundled-package discovery.
- A common external-tool runner using `ProcessStartInfo.ArgumentList`.
- Per-user uploader discovery/download/cache behavior.
- UI suspension, progress reporting, reconnect, and identity verification.
- MSBuild inclusion of packaged firmware in build and publish output.
- Build-script generation of versioned firmware packages.

Reuse the architecture, not YATSS-specific board assumptions. Drag needs no
flash-capacity variants, DFU mode, ESP chip probing, merged binary, or flash
offset selection.

## Test Plan

### Automated

- Valid package loads and its SHA-256 matches.
- Wrong product, board, MCU, backend, protocol, baud, filename, size, and hash
  are rejected.
- Path traversal or nested image names are rejected.
- Malformed/truncated Intel HEX and missing EOF are rejected.
- Generated avrdude arguments contain:

  ```text
  -patmega2560 -cwiring -P<port> -b115200 -D -Uflash:w:<path>:i
  ```

- Arguments do not disable verification.
- Tool-provider hash mismatch is rejected before extraction.
- `HELLO` parsing produces structured product, firmware, protocol, and MCU
  identity.
- Verification accepts only `DRAG_MC`, expected version, and `MEGA2560`.
- Publish fails when the DragMC package is absent.

### Bench

1. Update a connected Mega already running the previous DragMC version.
2. Confirm the COM port is released for avrdude and automatically reopened.
3. Confirm avrdude verifies flash and DragWin receives the expected `HELLO`.
4. Upload a harmless different sketch with the bootloader intact, then recover
   it using DragWin's manual Mega confirmation path.
5. Cancel before flashing and confirm DragWin reconnects normally.
6. Select the wrong COM port and confirm no device is altered.
7. Disconnect USB before upload and during upload; confirm useful failure and a
   retry path.
8. Test at least one official Mega and, if available, one CH340-compatible Mega.
9. Run Sensor Test and a complete demo/physical pass after updating.
10. Confirm published self-contained and non-self-contained folders both carry
    exactly one current `.dragfw` package under `Firmware`.

Do not deliberately remove the bootloader as part of routine testing.

## Recommended Implementation Order

1. Add a firmware version header and package build script.
2. Generate and inspect the first application-only Mega package.
3. Add package loading/validation and automated tests.
4. Add direct avrdude invocation and argument tests.
5. Add installed-tool discovery, then approved official download/cache support.
6. Add structured DragMC identity parsing and post-flash verification.
7. Add the guarded WinForms update workflow and progress UI.
8. Include firmware in build/publish output and enforce it at publish time.
9. Complete bench tests and operator documentation.

## Primary References

- Arduino CLI upload command, including `--input-file` and `--verify`:
  <https://arduino.github.io/arduino-cli/dev/commands/arduino-cli_upload/>
- Arduino AVR upload recipe:
  <https://github.com/arduino/ArduinoCore-avr/blob/master/platform.txt>
- Arduino Mega 2560 board definition, including `wiring`, 115200 baud,
  `atmega2560`, and the bootloader filename:
  <https://github.com/arduino/ArduinoCore-avr/blob/master/boards.txt>
- AVRDUDE source and licensing:
  <https://github.com/avrdudes/avrdude>

