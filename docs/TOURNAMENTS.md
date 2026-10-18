# Tournament Behavior

DragWin supports heads-up and bracket tournaments with two or four active
lanes. Two-lane mode uses physical lanes 1 and 4. Tournament setup selects
racers and cars from the local SQLite database, then generates heats and stores
their progression and results.

## Tree Start Control

Under Configure > Race and track settings > Race, Tree start defaults to
Automatic. Select Starter Button to require the on-screen Start Tree button
after all participating lanes stage. The choice is saved and applies to both
practice and tournament heats. Arm Practice Run / Arm Heat still prepares the run;
Start Tree only authorizes the staged start, not an unstaged or unarmed race.
For practice, Start Tree is at the bottom of Practice Pass Results, next to Close.
The main window only arms the practice run; it has no duplicate starter control.
Tournament starts are controlled in the tournament runner. Simulated practice
results do not show a hardware starter control.

The button stays disabled until every participating lane is staged, disables
after one click, and is cancelled on rollout, disconnect, or controller reboot.
After a rollout, restage and press it again. The configured staged delay begins
when the controller accepts the click; amber cadence and bracket offsets remain
controller-timed. A rollout during that delay also cancels the authorization.

This requires the current-source Windows build and DragMC 0.6.6 (protocol 9+).
Beta.2 downloads do not include this feature. Update controller firmware before
testing it; older firmware can still be used in Automatic mode. Simulated demo
heats do not require or exercise the hardware starter button.

## Race Results

The Mega is authoritative for reaction times, fouls, finish order, bracket
breakouts, placements, and winners. DragWin records those controller results
and uses them to advance cars. A red-light reaction remains available for
reporting even though the lane is ineligible to win against a legal finisher.

Four-lane heats can advance two cars. DNFs remain unplaced because the current
sensor arrangement cannot establish a defensible order among cars that never
finish.

Completed heat results remain available for confirmation or rerun while the
controller clears the track and returns to staging. The runner accepts race
events only after that heat has been armed.

## Ordered lane choice

Implemented in the tournament runner.

Rounds after the first use a sequential lane-choice workflow:

1. Present cars in ascending order of valid prior-round reaction time.
2. When the current chooser selects an occupied lane, swap the displaced car
   into the chooser's original lane.
3. Lock the current chooser's selected lane after that choice is confirmed.
4. Later choosers must not be allowed to select a lane locked by an earlier
   chooser.
5. Cars without a valid reaction time choose last, with ties randomized.

Round-one lane assignment remains random.

## Tournament storage

Tournament data is stored in the local SQLite database managed by
`RaceRepository`. By default that file is:

```text
%LOCALAPPDATA%\dragWin\dragWin.db
```

The source repository does not include operator database contents.

Manual backups, verified restore, daily automatic backups, and pre-schema-change
safety backups protect the local database. Tournament reports open inside
DragWin and can optionally include JSON and CSV exports.

If confirmation was saved but the application stopped before creating the next
round, reopening the tournament advances from the saved results. A confirmed
final is marked complete. Failed confirmation or advancement can be retried;
the operator cannot rerun a heat whose confirmation has already been saved.

## Heat setup

The runner sends the active lane count, selected heat lanes, per-car dial-ins,
and race mode before a heat. In 2-lane mode the physical lanes are lanes 1 and
4.

Bye cars are guaranteed advancement by the tournament model. The current venue
decision about whether a bye must stage and take the Tree cleanly remains
recorded in `TODO.md`.

## Demonstration

The Test menu can generate deterministic practice and heat results without
physical sensor input. Demo output exercises result handling and reports but is
not a substitute for controller, sensor, Tree, or moving-car validation.
