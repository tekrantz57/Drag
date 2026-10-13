# Beta Feedback

Report reproducible bugs through
[GitHub Issues](https://github.com/tekrantz57/Drag/issues/new/choose).
Installation and demo feedback are welcome without a track or controller.

Include the Drag version, operating system and CPU architecture, demo or
hardware mode, steps to reproduce, expected behavior, and actual behavior.
For hardware issues, include controller model, firmware version, and wiring.
Remove private racer data, credentials, and local paths from screenshots and
log excerpts. Do not upload your database.

This experimental beta has no guaranteed support response. General feature
requests and pull requests are not currently accepted. Moving-car and
simultaneous four-lane hardware qualification remain pending.

## Result Sources

New builds retain a source on each confirmed tournament result: `Simulated`
or `Controller`. HTML reports warn whenever simulated results are included;
each row identifies its source. JSON includes each row's `source` and the report's
`hasSimulatedResults`; CSV adds `ResultSource`. JSON archive schema is version 3.
Practice results also show a Source column, including copied table text.
Unclassified results are `Unknown`; no source is inferred from a tournament name.

Demo and hardware operation still share one database. A controller source label
identifies where results came from; it does not certify physical timing accuracy.
The v0.10.0-beta.1 package predates these source labels. They are included in
v0.10.0-beta.2; existing exported files are not retroactively changed.
