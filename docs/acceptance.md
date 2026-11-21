# Acceptance record

**Native validation status: pending.** Revit 2022 is not installed in the development environment. No native `.rfa` or `.rvt` output, interference result, IFC export or file-size pass is claimed here.

The full client parameter table, reference IFCs and mounting detail have not been provided. This record covers the published brief and records provisional decisions in [design.md](design.md). It does not replace the client's eventual acceptance tests.

## Checks outside Revit

Build the add-in against the pinned Revit 2022 API references and run `ContractTests`. Keep the actual compiler/test output with a release. The test process exits nonzero on failure.

| Check | Expected |
|---|---|
| C# compilation | No compiler errors against the Revit 2022 assemblies |
| Height boundaries | Explicit cases immediately below, on, and above each valid centre limit |
| Unit conversion | 304.8 mm = 1 ft; 25.4 mm = 1 in; 1400 mm = 55.1181102362 in |
| Display rounding | 1400 mm displays as 55 in at whole-inch precision |
| Shared parameter identities | Height and status GUIDs match the model/tag contract |

These checks do not load Revit, execute a family formula, generate native geometry or render a view.

## Revit 2022 run

Use a valid Revit 2022 installation with the matching family templates and a prepared tag seed. Record the exact Revit build, language, IFC exporter version and IFC configuration. Create the example in a new output folder. Do not overwrite a previous acceptance run.

| ID | Procedure | Pass condition | Current evidence |
|---|---|---|---|
| R01 | Generate, save, close, then reopen the sensor in Revit 2022. | No corruption, upgrade prompt or family constraint warning. | Pending native run |
| R02 | Inspect Family Category and Parameters, host behavior and Family Types. | Face-based family; exactly SE-200 and SE-300; intended category. | Pending native run |
| R03 | Inspect the saved sensor `.rfa` size after final save/compact. | Under **1,000,000 bytes**. Record exact bytes. | Pending native run |
| R04 | Measure the physical housing with helpers hidden. | 155 W ×129 H ×34 D mm, rounded corners; no CAD import. | Pending native run |
| R05 | Place each type on a vertical wall with origin at the finished-floor datum. | Centre is 1400 mm AFF by default; local Y maps to project vertical. | Pending native run |
| R06 | Change one instance's height. | Only that instance changes; schedule and tag update. | Pending native run |
| R07 | Flex the heights in the table below, regenerating after each change. | Family formula, measured edges and shared schedule text all agree. | Pending native run |
| R08 | Measure the band while height changes from 1000 to 1400 to 1800 mm. | Band edges remain at 1100 and 1700 mm AFF in world coordinates. | Pending native run |
| R09 | Repeat placement on a rotated wall and at a nonzero level elevation. | Absolute world coordinates reflect the correct finished floor; no axis flip. | Pending native run |
| R10 | Deliberately place the datum 200 mm above finished floor. | Verifier reports the datum error; documentation explains correction. Do not accept the parameter alone as true AFF. | Pending native run |
| R11 | Mirror or flip an instance, then inspect normals, body and band. | Geometry remains on the intended wall side with a vertical datum; unsupported transformations are rejected or documented. | Pending native run |
| R12 | Inspect clearance geometry and subcategory; toggle Show Keep Clear. | Solid on WS_KeepClear; visible switch works; measured offsets match the agreed envelope. | Pending native run |
| R13 | Run Revit Interference Check against the nearby door and grille categories. | Known overlaps are reported. Save the native interference report. | Pending native run |
| R14 | Move one obstacle beyond the clearance, then repeat. | Negative control no longer clashes; other known clashes remain. | Pending native run |
| R15 | Repeat R13 with Show Keep Clear off and with its subcategory hidden. | Record actual behavior for both controls. Agree required hidden-solid behavior with client. | Pending native run |
| R16 | Inspect plan at typical 1:50 and 1:100 scales and different cut-plane heights. | Intended 2D symbol is legible; no unexpected elevation-only symbol or duplicated geometry. | Pending native run |
| R17 | Open the provided tag family, then tag both types and flex heights. | Dynamic Type Mark and height labels; whole mm and inches; suitable AFF C/L text. | Pending native run |
| R18 | Inspect sample project and schedule. | Wall, door and grille are present; sensor type, height and calculated status are correctly scheduled. | Pending native run |
| R19 | Save, close and reopen the sample and tag in Revit 2022. | All dependencies load and schedule/tag values remain correct. | Pending native run |
| R20 | Export IFC4 and inspect with an independent parser or viewer. | Each intended device maps to IfcSensor; correct location and dimensions; helper geometry follows the agreed export policy. | Pending native run |
| R21 | Compare final output with the client parameter table and reference files. | Names, units, GUIDs, plate footprint, category and required metadata agreed. | Awaiting full brief |

## Whole-device boundary cases

Values assume the provisional **129 mm vertical dimension**. Equality at the band limit is allowed. Test both types.

| Centre AFF (mm) | Bottom AFF (mm) | Top AFF (mm) | Status |
|---:|---:|---:|---|
| 1100.0 | 1035.5 | 1164.5 | OUT OF BAND |
| 1164.4 | 1099.9 | 1228.9 | OUT OF BAND |
| 1164.5 | 1100.0 | 1229.0 | OK |
| 1400.0 | 1335.5 | 1464.5 | OK |
| 1635.5 | 1571.0 | 1700.0 | OK |
| 1635.6 | 1571.1 | 1700.1 | OUT OF BAND |
| 1700.0 | 1635.5 | 1764.5 | OUT OF BAND |

Small unit-conversion roundoff must not silently change the intended inclusive limits. If Revit evaluates an exact boundary incorrectly, record the failing native case and resolve it in the geometry/formula contract; do not simply relabel a failing test as a pass.

## Evidence to keep with a validated release

- Three native deliverables: the embedded sensor family, the dynamic tag family and the sample project.
- Exact sensor byte count and hashes of delivered files.
- Revit build and IFC exporter/configuration used.
- Screenshots of Family Types, the height-flex cases, the schedule and the tag.
- Native interference reports for the positive, negative and visibility-off cases.
- IFC4 output and an entity/geometry check report.
- A dated acceptance record separating pass, fail and untested items.

The automatically generated native report, when available, covers only checks performed by its code. A geometric intersection test does not certify Revit's Interference Check command. A family parameter named `IfcExportAs` does not certify the exported IFC. An unchecked test remains pending.
