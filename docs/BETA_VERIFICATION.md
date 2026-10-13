# Public Beta Verification

Beta.1 checked October 11, 2026; beta.2 source checks completed October 13, 2026.
Package startup checks on the development PC are not fresh-PC or ARM64 runtime tests.

## Published v0.10.0-beta.1

- GitHub release is public, not a draft, and marked as a prerelease.
- Downloaded both ZIPs and SHA256SUMS.txt directly from the GitHub release.
- Both SHA-256 checksums match the published values.
- Both archives contain the executable, self-contained runtime, demo launcher,
  startup instructions, build metadata, and license/notice files.
- Executable architecture matches x64/ARM64 build metadata.
- No database, logs, settings, voice models, or PDB files were found in either ZIP.
- Extracted x64 package and ran its actual Start-Demo.cmd from a folder with
  spaces. The application reached an idle UI, stayed running, and closed normally.
- Package source commit is b858a570e24f0253018820e7250ba24a67310d81.
- [GitHub validation](https://github.com/tekrantz57/Drag/actions/runs/37482085510)
  passed for that exact commit: Windows build, protocol/regression tests,
  formatting, Mega compilation, firmware result-delivery tests, and speech tests.

## Beta.2 Preparation And Release Checks

- GitHub Issues enabled for focused beta bug reports; the repository includes
  a dedicated beta bug-report form.
- Result source retained with confirmed tournament results and shown in history,
  HTML, JSON, and CSV. Practice results show a Source column.
- Regression tests cover reopening saved simulated results, exports, mixed
  controller/simulated tournaments, and practice labels.
- Local Release build and protocol/regression tests passed; formatting passed.
- All three local speech-helper tests passed using Python 3.
- Venue mounting documentation updated to the no-routing, extended-lead prototype.
- Help menu tests cover documentation actions and non-private version information.
- The commissioning checklist is included in beta.2 packages.
- Modern Edge PDF rendering confirms landscape Letter output, no table overflow,
  and repeated headings on every page of a nine-page, 120-row long-name test.
  Rendered first, continuation, and final pages were visually inspected.
- Beta.2 packaging uses a clean tagged commit, with checksum/layout/architecture
  verification for both ZIPs and actual x64 demo-launch testing before publication.
  Beta.1 downloads remain unchanged.

## Still Requires A Different Environment

- First-time download/extraction and Windows security warnings on a fresh PC.
  Startup here does not establish how SmartScreen behaves on another machine.
  Existing instructions warn that the app is unsigned and say not to disable
  Windows security protections.
- Runtime testing of each exact ARM64 ZIP on ARM64 Windows/Wine.
- Moving-car and simultaneous four-lane hardware validation.

## Repeat The Package Check

After downloading assets to a folder, run from the repository root:

```powershell
.\tools\Test-ReleasePackage.ps1 -Archive PATH_TO_ZIP -Checksums PATH_TO_SHA256SUMS
```

Add `-LaunchDemo` for an x64 package on an x64 Windows host. It extracts into
a new folder with spaces, runs the real launcher, and closes only that test
application. Startup uses the normal operator profile; there is no separate
database or historical-data inference. ARM64 archives are inspected without
launching them on an x64 host. The extracted test folder is kept for inspection.
