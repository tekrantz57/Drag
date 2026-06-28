# Tournament follow-up notes

## Improve ordered lane choice

The current editable lane grid should become a sequential lane-choice workflow
for rounds after the first:

1. Present cars in ascending order of valid prior-round reaction time.
2. When the current chooser selects an occupied lane, swap the displaced car
   into the chooser's original lane.
3. Lock the current chooser's selected lane after that choice is confirmed.
4. Later choosers must not be allowed to select a lane locked by an earlier
   chooser.
5. Cars without a valid reaction time choose last, with ties randomized.

Round-one lane assignment remains random.
