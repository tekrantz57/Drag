# Tournament Notes

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

## Heat setup

The runner sends the active lane count, selected heat lanes, per-car dial-ins,
and race mode before a heat. In 2-lane mode the physical lanes are lanes 1 and
4.
