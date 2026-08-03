# Project Status

Drag is an experimental slot-car drag timing and tournament-control project.
Its software workflows are substantially implemented, while full physical-track
validation remains in progress.

The current public source milestone is `v0.05.0-beta.1`. DragWin displays the
exact Git tag for tagged builds and includes commit and dirty-state details for
development builds.

## Completed

- Four-lane and two-lane race models, with two-lane mode using lanes 1 and 4.
- Heads-up and bracket logic, Full and Pro Trees, staging modes, staged delay,
  dial-ins, reaction times, breakouts, placements, fouls, and DNFs.
- Practice passes, tournaments, lane choice, byes, history, reports, exports,
  backups, restore, diagnostics, and optional voice announcements.
- Four required and two optional sensor positions per lane throughout firmware,
  protocol, diagnostics, results, persistence, and reports.
- Firmware-maintained raw edge counts and blocked-pulse durations.
- Lane 1 wiring for pre-stage, stage, speed trap, and finish.
- Sensor Test validation of all four lane 1 inputs.
- Multiple complete lane 1 passes using cardboard to interrupt the beams.
- In-app Mega firmware download tooling, upload, reconnect, and identity check.
- Self-contained x64 operation under Wine 11 on Intel Linux.
- Native Windows ARM64 operation under ARM64 Wine 11 on a Rock 5B.
- Successful controller firmware updates from both tested Wine environments.

## Physical Limitation

The current lane 1 optical path is below the reach of a normal car guide flag
because the track deck is 3/4 inch thick. The electronics and timing path work
with cardboard interruptions, but moving-car validation waits on raised optical
components, routed mounting pockets, or both.

## Still Pending

- Moving-car pulse-width and repeated-pass testing after the optical-height fix.
- Installation and physical validation of both optional interval timers.
- Complete lanes 2 through 4 wiring and simultaneous multi-lane testing.
- Final speed-trap distance measurement and speed verification.
- Official Mega and compatible-clone updater coverage where hardware is
  available.
- Firmware-update cancellation and deliberate failure-path testing.
- Venue decisions documented in `TODO.md`, including staging timeout and DNF
  advancement behavior.
- Production tournament operation and feedback from additional operators.

The current Mega firmware uses approximately 82 percent of its 8 KB SRAM.
Reassess memory after all 24 sensor inputs are installed and exercised.
