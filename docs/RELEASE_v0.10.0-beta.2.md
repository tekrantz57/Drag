# Drag v0.10.0-beta.2

An updated, free Windows demo beta for two- and four-lane slot-car drag racing.
No track or controller is needed to try the software.

## Download And Start

- **Windows x64:** most Intel/AMD Windows PCs.
- **Windows ARM64:** Windows on ARM.
- Extract the entire ZIP, then double-click **Start-Demo.cmd**.
- Click **New Demo Tournament** for sample racers, cars, and simulated heats.
- Packages are self-contained; no separate .NET installation is required.
- `SHA256SUMS.txt` supplies the checksums for both ZIPs.

## New Since Beta.1

- **Help menu:** demo instructions, wiring, a four-lane commissioning checklist,
  beta bug reports, About Drag, and Copy Version Information. Copied information
  includes app/platform/controller details, not racer data or local paths.
- **Explicit result sources:** confirmed tournament rows retain Simulated or
  Controller labels through restart, history, and HTML/JSON/CSV exports.
  Reports warn when simulated results are included; mixed-source tournaments
  retain the individual row labels. Practice results also show their source.
- **Print-friendly reports:** landscape pages, repeated column headings, wrapped
  names, compact summary, and rows kept together where they fit. Modern-browser
  PDF output was checked with a nine-page long-name stress report.
- **Commissioning worksheet:** pin maps, sensor and Tree checks, moving-car pulse
  measurements, trap-distance records, simultaneous-lane tests, and venue sign-off.
- Focused GitHub beta bug-report form and updated sensor mounting documentation.

JSON archive schema is now version 3; CSV adds `ResultSource`. Unclassified
records are Unknown rather than inferred from tournament names. Controller
firmware remains **DragMC 0.6.5**; this update does not change its timing or pins.

## Important Limits

- Experimental prerelease: moving-car and simultaneous four-lane track
  qualification remains pending. Demo results are not hardware measurements.
- Demo and normal operation use the same local database and settings.
- Unsigned Windows application: a publisher/security warning may appear. Use
  the official download and do not disable Windows security protections.
- x64 startup was tested on the development PC. Fresh-PC warning behavior and
  this exact ARM64 package's runtime behavior still need independent testing.
- Prior builds worked under Wine 11 on Intel Linux and ARM64 Wine 11 on Rock 5B;
  that does not constitute runtime validation of this exact package.
- Piper/models and avrdude are not bundled. Speech is optional and off by default;
  firmware updating obtains its verified uploader separately.
- Mega firmware uses 6,711 bytes (81%) of SRAM, leaving 1,481 bytes.

## Feedback

[Report a beta bug](https://github.com/tekrantz57/Drag/issues/new/choose).
Include Help > Copy Version Information and steps to reproduce. Remove private
data from attachments and do not upload a database. Support is best-effort;
general feature requests and pull requests are not currently accepted.

No personal database, settings, logs, backups, reports, or voice models are included.
