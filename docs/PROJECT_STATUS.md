# Project Status

Drag is an experimental slot-car drag timing and tournament-control project.
Its software workflows are substantially implemented, while full physical-track
validation remains in progress.

The current packaged beta milestone is `v0.10.0-beta.2`. DragWin displays the
exact Git tag for tagged builds and includes commit and dirty-state details for
development builds.

Self-contained Windows x64 and ARM64 ZIPs include a no-controller demo launcher.
See `TRY_DEMO.md`; four-lane physical validation is still pending.

## Completed

- Experimental Due target in the shared `dragMC` sketch: up to 24 CHANGE-interrupt
  sensor inputs with ISR timestamps/counters, common race/Tree/protocol code,
  preserved Tree pins, and lane 4 sensor remapping to D50-D53. Separate identity
  `DRAG_MC_DUE` keeps the Mega updater blocked. Current Windows source recognizes
  this full Due target for manual starter operation, not the light-only sketch.
  See `DUE_PORT.md`. Not included in beta.2; full bench qualification remains pending.
  October 20: Due 0.1.1 communication and one real LM393 input were reported
  working at 3.3 V (0.09 V clear, 2.98 V blocked). Moving-car timing is unqualified.
  Due 0.1.2 / Mega 0.6.8 add protocol 11 diagnostic lane selection; Due sensor
  interrupts are off at idle and restricted to race participants/installed
  sensors when armed. Sensor Test has an independent saved lane scope, with
  race priority and lease expiry. Scope changes discard pending crossings.
  An Elegoo Mega clone also arrived October 20, reported on COM26 with firmware
  uploaded; complete clone sensor/Tree and timing qualification is still pending.
  Due Programming USB is COM21 on this bench; COM24 was Native USB.
  Current source adds a Due Programming-port updater with separate validated
  BIN package, pinned official bossac acquisition, reset/write/verify/reboot,
  exact identity checks, and explicit board selection for unknown sketches.
  Host safety/failure tests pass. October 20: the operator successfully flashed
  both the Due and Elegoo Mega through DragWin on native Windows. Due/bossac
  Wine testing and deliberate hardware failure/recovery checks remain pending.
  Existing Mega package selection and avrdude remain supported.

- Current source adds a large completed-pass display for practice and tournament
  heats, with physical lane order, racer/car identities when known, explicit
  outcomes and advancement, optional interval times, and simulation labels.
  It opens automatically and can be reopened with practice's Last Pass button
  or the tournament View > Last Heat Results menu. It remains until closed or
  the next pass is armed (discarding/clearing results also closes it).
  Two-lane finish lead accounts for bracket head starts and is independent of
  the winning decision; it is omitted for missing measurements, DNFs, byes,
  and four-lane heats. Displayed times use four decimals, not a claim of
  validated 0.1 ms accuracy. Existing tables/reports and race rules are unchanged.
  This display is not included in beta.2 downloads.

- The firmware 0.6.7 / protocol 10 change removes speed-trap and finish
  qualification delay, latches the first sampled raw edge, and records requested
  scan-gap/duration diagnostics. Short-pulse, chatter, and unchanged-staging
  host tests pass. This is not yet physically validated or in beta.2 downloads.

- Current source adds optional, saved Starter Button mode for practice and
  tournament heats, with controller-enforced staging, staged delay, and stale-click
  rejection. Requires DragMC 0.6.6 / protocol 9; not included in beta.2 downloads.
  Host state-machine and Windows regression tests pass. October 21: the operator
  reported that both practice-window and tournament starter buttons work as
  intended. This validates those workflows, not at-speed sensor capture.

- Four-lane and two-lane race models, with two-lane mode using lanes 1 and 4.
- Heads-up and bracket logic, Full and Pro Trees, staging modes, staged delay,
  dial-ins, reaction times, breakouts, placements, fouls, and DNFs.
- Practice passes, tournaments, lane choice, byes, history, reports, exports,
  backups, restore, diagnostics, and optional SAPI, Piper, or eSpeak NG voice
  announcements.
- Help menu with documentation, beta bug reports, and copyable version information.
- Per-result simulation labels in history and exports, landscape report printing,
  and a four-lane commissioning worksheet.
- Four required and two optional sensor positions per lane throughout firmware,
  protocol, diagnostics, results, persistence, and reports.
- Firmware-maintained raw edge counts and blocked-pulse durations.
- Lane 1 wiring for pre-stage, stage, speed trap, and finish.
- Sensor Test validation of all four lane 1 inputs.
- Multiple complete lane 1 passes using cardboard to interrupt the beams.
- Lane 1 pegboard Tree validation of all seven outputs, D22 through D28, using
  individual light controls and the automatic diagnostic sequence.
- Arduino Due light-tree diagnostic sketch preserving the Mega D22-D49 Tree
  mapping, with a distinct identity that the Mega firmware updater rejects.
- One-lane physical practice-start validation using A0 pre-stage and A1 stage,
  including the normal Tree sequence, a legal green launch, and an early-stage
  release producing a red-light foul.
- In-app Mega firmware download tooling, upload, reconnect, and identity check.
- Self-contained x64 operation under Wine 11 on Intel Linux.
- Native Windows ARM64 operation under ARM64 Wine 11 on a Rock 5B.
- Successful controller firmware updates from both tested Wine environments.
- Persistent Piper and native Linux eSpeak NG helper support, using
  loopback-only services and a speech engine selected in Race Settings.

## Physical Limitation

The 3/4-inch deck initially left the optical path below the guide flag. As of
October 19, four lane 1 sensors are installed and detect the actual car guide
flag during slow tests. Powered passes are not yet reliable: short finish pulses
were rejected by the previous 2 ms qualification and the physical speed-trap
signal missed raw detection. Wiring-swap tests localized the missed signal to
the physical speed-trap sensor/signal rather than a particular Mega input;
mounting and capture-timing investigation remain necessary. The operator restored
A2/A3 to their normal wiring before leaving the venue. No definitive
optical-versus-polling cause is established.
Lanes 2-4 remain unwired and show floating-input activity. The venue owner has
ruled out routing; preserve alignment and guide clearance in the chosen mounts.

## Still Pending

- Moving-car pulse-width and repeated-pass testing after the optical-height fix.
- Installation and physical validation of both optional interval timers.
- Complete lanes 2 through 4 wiring and simultaneous multi-lane testing.
- Final speed-trap distance measurement and speed verification.
- Additional official Mega and compatible-clone updater coverage where hardware
  is available; native Windows Due and Elegoo Mega success is recorded above.
- Firmware-update cancellation and deliberate failure-path testing.
- Venue decisions documented in `TODO.md`, including staging timeout and DNF
  advancement behavior.
- Production tournament operation and feedback from additional operators.
- Live Piper and eSpeak NG validation with DragWin under Wine. The equivalent
  YATSS helper protocol and Piper model caching have been verified on ARM64
  Wine 11.

The current Mega firmware uses approximately 82 percent of its 8 KB SRAM
(6,760 bytes, leaving 1,432 bytes for local variables, firmware 0.6.8 shared-sketch build).
Reassess memory after all 24 sensor inputs are installed and exercised.
