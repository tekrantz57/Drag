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

DragMC builds against the Arduino AVR Boards core. The core and bundled
toolchain contain components under their respective upstream licenses.

- Arduino AVR core source and licensing:
  <https://github.com/arduino/ArduinoCore-avr>
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
