# Drag v0.10.0-beta.1

First packaged beta for trying DragWin without a track or controller.

## Downloads

- **Windows x64:** for most Intel/AMD Windows computers.
- **Windows ARM64:** for Windows on ARM.
- Both ZIPs are self-contained; extract the complete folder and run
  **Start-Demo.cmd**. No .NET installation is required.
- See the included **TRY_DEMO.md** for practice passes, tournament setup,
  lane selection, simulated heats, and reports.
- `SHA256SUMS.txt` contains the checksums of both ZIPs.

## Included

- Two- and four-lane tournament workflows, simulated practice and bracket heats,
  ordered lane choice, byes, advancement, and final reports.
- HTML reports and optional JSON/CSV exports; local database backup and restore.
- Optional Windows SAPI, external Piper, and native Linux speech helper support.
- A demo launcher that disables controller connections and firmware updates.
- One-click sample tournaments with four fictional racers and eight cars.
- DragMC **0.6.5**, including incremental final-result delivery without increasing
  the Mega serial queue size.
- Fixes for post-race confirmation controls, interrupted tournament advancement,
  oversized dial-in input, and background speech voice discovery.

## Important Limits

- **Experimental prerelease:** complete moving-car and simultaneous four-lane
  hardware validation remains pending. Automated simulation tests are not a
  substitute for electrical or physical timing tests.
- The application is unsigned and may trigger an unknown-publisher warning.
- Demo results use the same local database as normal operation. Name demo
  tournaments clearly; simulated results are not real race measurements.
- Controller operation requires a Mega 2560 and appropriately wired sensors
  and tree outputs. No hardware is required for simulation.
- Piper, voice models, and avrdude are not bundled. Piper/model installation is
  optional; firmware updating obtains its verified uploader separately.
- Mega firmware uses 6,711 bytes (81%) of SRAM, leaving 1,481 bytes. Larger
  physical installations still need memory and stability validation.
- Earlier self-contained builds worked under Wine 11 on Intel Linux and ARM64
  Wine 11 on a Rock 5B, including firmware updates. This release has not been
  revalidated on those Linux machines.

No personal database, settings, logs, backups, or reports are included.
