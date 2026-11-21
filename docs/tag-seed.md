# Tag seed in Revit 2022

The Revit 2022 public API cannot create or edit the label definitions used by a tag. This is a manual seed, not a generated text note. Do this once in the Family Editor before running the add-in.

1. In Revit **2022**, create a family from **Metric Generic Model Tag.rft**. The category must remain **Generic Model Tags**.
2. Open **Manage > Shared Parameters** and select `parameters/shared-parameters.txt` from this repository. Do not create a different parameter with the same name: the GUID is the link between model and tag.
3. Create a label for built-in **Type Mark**. Set a suffix of ` / `.
4. Create a label for shared **Mounting Height** (GUID `683c7d22-6dd7-4f63-b753-f38b50c043f7`). In the label's unit format, turn off project settings, choose millimetres, round to **1 mm**, suppress the unit symbol, and use suffix ` AFF C/L`.
5. Create a second label using **the same shared Mounting Height parameter**. Set its own format to **fractional inches** or **decimal inches**, round to **1 inch**, suppress the unit symbol, and use prefix ` (` and suffix ` in)`.
6. Align the labels on one baseline, with room for the longer text `OUT OF BAND` only if you add an optional fourth status label. A text size around 2.5 mm is adequate for this example. Test spacing with realistic sample values, not question marks.
7. Save as `WS_SensorTag.rfa`. The build command will validate the family category and load it into the example project. Label content and formatting still need visual verification.

Expected output for the SE-200 type at 1400 mm:

```text
SE-200 / 1400 AFF C/L (55 in)
```

1400 mm is 55.1181 inches. The displayed 55 inches is whole-inch rounding. There must be one stored length and two label formats; separate manually entered metric and imperial values would drift.

The job's `KT-200P` is an example Type Mark, not one of the supplied type names. This study uses `SE-200` and `SE-300` as provisional Type Marks. Set the final naming when the parameter table arrives.

The project deliberately does not ship a fabricated `.rfa` or claim that this manual seed exists. A completed native release must include the actual tested tag file.

