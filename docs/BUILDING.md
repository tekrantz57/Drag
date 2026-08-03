# Building From Source

Drag is currently distributed as source. No prebuilt DragWin application
package is offered through GitHub Releases.

## Prerequisites

- .NET 10 SDK on Windows.
- Arduino CLI.
- Arduino AVR Boards core for the Mega 2560.
- Visual Studio is optional.

Install or update the Arduino index and AVR core:

```powershell
arduino-cli core update-index
arduino-cli core install arduino:avr
```

## Windows Application

From the repository root:

```powershell
dotnet restore dragWin\dragWin.sln
dotnet build dragWin\dragWin.sln -c Release --no-restore
dotnet run --project dragWin\dragWin.ProtocolTests\dragWin.ProtocolTests.csproj -c Release --no-build
dotnet format dragWin\dragWin.sln --verify-no-changes --no-restore
```

Run DragWin from source with:

```powershell
dotnet run --project dragWin\dragWin.csproj -c Release
```

## Mega Firmware

Compile the sketch directly:

```powershell
arduino-cli compile --fqbn arduino:avr:mega:cpu=atmega2560 dragMC
```

Create the versioned application-only `.dragfw` package used by DragWin:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\tools\Build-ControllerFirmware.ps1
```

The packaging script reads `dragMC/FirmwareVersion.h`, compiles the Mega image,
records its size and SHA-256, validates the package metadata, and writes the
matching file under `dragMC/dist`. Commit a changed package only with the exact
firmware source that produced it.

## Local Self-Contained Builds

These commands create local test folders; they do not create an official Drag
release:

```powershell
dotnet publish dragWin\dragWin.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=false `
  -o artifacts\release\DragWin-win-x64

dotnet publish dragWin\dragWin.csproj -c Release -r win-arm64 `
  --self-contained true -p:PublishSingleFile=false `
  -o artifacts\release\DragWin-win-arm64
```

Keep the complete publish directory together. DragWin expects its .NET runtime,
native SQLite library, and `Firmware` directory to remain beside the executable.

## Clean-Clone Check

Before treating a commit as a public validation point:

1. Build the solution in Release configuration.
2. Run the protocol tests and formatting check.
3. Compile and package DragMC for the Mega 2560.
4. Confirm the source tree contains no database, log, report, backup, credential,
   IDE-user, or machine-local files.
5. Follow only the commands in this document from a clean checkout.
