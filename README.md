# Drag Strip

Slot car drag strip controller for an Arduino Mega 2560 and a Windows
operator app.

The repository has two main parts:

- `dragMC`: Arduino Mega 2560 firmware for tree lights, beam sensors, race
  timing, bracket logic, and the serial protocol.
- `dragWin`: .NET 9 WinForms app for serial control, practice runs,
  tournament setup/running, sensor testing, and race/tournament records.

## Current Hardware Model

- Controller: Arduino Mega 2560.
- Serial: USB serial at 115200 baud.
- Lanes: 4 physical lanes, with optional 2-lane mode using lanes 1 and 4.
- Sensors: 4 required beam sensors per lane plus 2 optional interval timers
  between stage and the speed trap.
- Tree: selectable 500 ms Full Tree or 400 ms Pro Tree, with an independently
  configurable staged-to-first-amber delay.
- Sensor polarity: LM393 slot sensors are active-HIGH on this track. A blocked
  beam reads `HIGH`; a clear beam reads `LOW`.
- Wiring assumption: each sensor signal uses an Ethernet twisted pair with its
  own ground return, plus a 10k pulldown from the Mega input node to ground.

See [HARDWARE.md](HARDWARE.md) for the full pin map and setup notes.

## Windows App

The WinForms app is in `dragWin`.

```powershell
dotnet build dragWin\dragWin.sln
dotnet run --project dragWin\dragWin.csproj
dotnet run --project dragWin\dragWin.ProtocolTests\dragWin.ProtocolTests.csproj
```

The app stores tournament data in:

```text
%LOCALAPPDATA%\dragWin\dragWin.db
```

Use **Data > Back Up Database...** to create and verify a portable SQLite copy.
Backups default to `Documents\dragWin Backups`. **Data > Open Database Folder**
opens the folder containing the active database.

Use **Data > Restore Database...** to validate and restore one of those copies.
dragWin automatically backs up the current database before replacing it.

dragWin also creates one verified automatic backup per day at startup and keeps
the newest 14 under `Documents\dragWin Backups\Automatic`. A safety copy is
created there before any database schema upgrade. Use **Data > Open Backup
Folder** to open the backup location.

Tournament reports open inside dragWin in a browser-style report window. HTML
is always written under `%LOCALAPPDATA%\dragWin\Reports`; optional JSON and CSV
exports can be enabled independently under **Configure > Race and track
settings... > Reports**. The JSON file is a versioned archive, while the CSV is
a flat row-per-entrant result file intended for spreadsheets and custom reports.
Enabled interval timers add cumulative and segment timing to practice results,
tournament history, HTML reports, JSON archives, and CSV exports.

Serial logs are written by date under:

```text
%LOCALAPPDATA%\dragWin\logs
```

## Firmware

The firmware sketch is:

```text
dragMC\dragMC.ino
```

Build and upload it for an Arduino Mega 2560. The firmware sends a `HELLO`
frame after startup, accepts checksum-protected commands from the Windows app,
and sends status, event, result, heartbeat, and error frames.

## Protocol

The controller and Windows app use colon-delimited printable ASCII frames with
a two-digit XOR checksum. See [dragWin/PROTOCOL.md](dragWin/PROTOCOL.md).

## Operator Notes

- Use `Test Sensors` in the Windows app after connecting to the Mega to verify
  each lane's pre-stage, stage, speed-trap, and finish sensors.
- In bracket mode, dial-ins are sent as integer milliseconds.
- Tournament records are local SQLite records. GitHub is only backing up the
  source code unless the app-data database is copied separately.

## License

MIT. See [LICENSE](LICENSE).
