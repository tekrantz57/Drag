# Try Drag Without Hardware

## Download And Start

1. Download `DragWin-v0.10.0-beta.2-win-x64.zip` from the
   [GitHub prerelease](https://github.com/tekrantz57/Drag/releases/tag/v0.10.0-beta.2).
   Most Intel and AMD Windows computers use this package. Use the `win-arm64`
   package only for Windows on ARM.
2. Right-click the ZIP and choose **Extract All**. Keep all extracted files
   together; do not run the application from inside the ZIP.
3. Double-click **Start-Demo.cmd**. The main window says **Demo - simulated results**.

No track, Arduino, .NET installation, Python, or speech engine is required.
The self-contained app targets Windows 10/11. It is unsigned; Windows may show
an unknown-publisher warning. Only proceed with a download you trust from the
repository above, and do not disable Windows security protections.

Demo startup prevents automatic and manual controller connections and firmware
updates for that session. Double-clicking `dragWin.exe` instead starts normal
hardware-capable operation, which may automatically connect to a detected port.

## A Quick Practice Pass

Choose **Test > Generate demo practice run** on the main window. Practice Pass
Results displays simulated reaction, elapsed time, and speed for the selected
lanes. Repeat the menu command for another pass. Practice lanes and optional
interval timers can be selected in **Configure > Race and track settings...**.
The **Arm Practice Run** button is for real hardware, not simulated passes.

## Run A Demo Tournament

1. Click **New Demo Tournament**. Drag supplies four fictional racers and
   eight cars and opens the first heat. No typing is necessary.
2. In the tournament window, click **Run Demo Heat**.
   Review the simulated results, then click **Confirm Results**.
3. Repeat for the remaining heats. From round two onward, confirm each
   highlighted entrant's lane choice before clicking **Run Demo Heat**.
   The suggested lane is already selected; click **Confirm Current Lane Choice**
   once for each entrant, or choose a different available lane first.
4. After the final confirmation, the tournament report opens inside DragWin.
   HTML is always saved; JSON and CSV exports are enabled by default and can
   be changed in Race Settings.

Click **New Demo Tournament** again for a fresh draw. To create your own racers
and cars, close Drag and start `dragWin.exe` normally, then choose **New
Tournament > Manage Racers & Cars**. Simulated heats work there too, even
without a controller.

Simulated heats can include red lights and breakouts. These are examples of
the rules, not evidence of sensor or controller performance. Tournament demos
do not animate a physical light tree.

## Data And Limits

The package contains no personal database, racers, cars, or voice models.
Fictional sample entries are generated only when you request a demo tournament.
Your entries and simulated tournament results are saved locally and survive
restarts. Demo and normal startup share the same database and settings, so
give demo tournaments recognizable names; do not use demo results as real
race records. **File > Open Database Folder** and **File > Open Backup Folder**
show the local storage locations.

Saved tournament rows identify their source as Simulated or Controller in history
and HTML/JSON/CSV exports. Reports containing simulated rows carry a warning.
Practice results also include a Source column.

Voice announcements are optional and off by default. Windows voices can be
enabled in Race Settings; Piper requires a separate installation and model.

This is an experimental beta. Complete moving-car and simultaneous four-lane
hardware validation is still pending. The bundled controller firmware is
**DragMC 0.6.5**, but it is not needed for the demo.

## Report A Beta Bug

Use the [Beta Bug Report](https://github.com/tekrantz57/Drag/issues/new/choose)
for installation problems, crashes, incorrect results, or a blocked workflow.
Include the version, operating system, and steps to reproduce. Remove private
data from attachments; do not upload your database. Support is best-effort.
Help > Copy Version Information supplies the build and platform details without
private racer data. Help also links to instructions, wiring, and the commissioning
checklist. See [Beta Feedback](BETA_FEEDBACK.md) for result-source labels.
