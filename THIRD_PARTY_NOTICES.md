# Third-Party Notices

Drag itself is licensed under the MIT License. It uses or interoperates with
the following third-party software. This summary is informational; the upstream
license texts and package metadata are authoritative.

## .NET Packages

- Microsoft.Data.Sqlite 10.0.10: MIT.
  <https://github.com/dotnet/dotnet>
- System.IO.Ports 10.0.10: MIT.
  <https://github.com/dotnet/dotnet>
- SQLitePCLRaw.bundle_e_sqlite3 3.0.4: Apache-2.0 package metadata; bundled
  SQLite has its own upstream terms.
  <https://github.com/ericsink/SQLitePCL.raw>
- SQLite: public-domain dedication.
  <https://www.sqlite.org/copyright.html>

NuGet restores these dependencies during the normal .NET build. Their license
information is also included in their NuGet package metadata.

## Arduino Firmware Toolchain

DragMC builds against the Arduino AVR and SAM Boards cores. The cores and bundled
toolchain contain components under their respective upstream licenses.

- Arduino AVR core source and licensing:
  <https://github.com/arduino/ArduinoCore-avr>
- Arduino SAM core source and licensing:
  <https://github.com/arduino/ArduinoCore-sam>
- Arduino CLI source and licensing:
  <https://github.com/arduino/arduino-cli>

## avrdude

DragWin does not bundle avrdude in the source repository or application
publish. When needed and approved by the operator, it downloads a pinned
official Arduino Windows archive, verifies its size and SHA-256, and extracts
only the matched `avrdude.exe` and `avrdude.conf` files into the user's local
tool cache.

- avrdude source and GPL licensing:
  <https://github.com/avrdudes/avrdude>
- Official Arduino tool downloads:
  <https://downloads.arduino.cc/>

## bossac / BOSSA

Due updates use bossac, the Arduino SAM flash uploader. DragWin can reuse the
installed SAM tool or download Arduino's pinned Windows 1.6.1-arduino archive,
checking its size and SHA-256 before extracting `bossac.exe` into the local tool
cache. The uploader itself is not bundled in Drag's source or application ZIP.
Source and upstream license information: <https://github.com/shumatech/BOSSA>.
Official archive: <https://downloads.arduino.cc/bossac-1.6.1-arduino-mingw32.tar.gz>.

## Optional Speech Engines

DragWin interoperates with optional speech engines but does not redistribute
them or their voice data.

- Piper is GPL-3.0-or-later and is installed separately from DragWin.
  <https://github.com/OHF-Voice/piper1-gpl>
- eSpeak NG is GPL-3.0-or-later and is supplied by the operator's Linux
  distribution.
  <https://github.com/espeak-ng/espeak-ng>

Piper voice models have individual licenses. Review the model's `MODEL_CARD`
before redistribution.
