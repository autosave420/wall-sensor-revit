# API references

Target: Revit 2022 / .NET Framework 4.8, x64. Sources checked against the 2022.1 reference assemblies.

- [Place a family on a face](https://www.revitapidocs.com/2022/be4b822c-829a-7e7b-8c03-a3a324bfb75b.htm): `NewFamilyInstance(Reference, XYZ, XYZ, FamilySymbol)`. Requires a work-plane-based family and a reference direction that is not parallel to the face normal.
- [Family formulas](https://www.revitapidocs.com/2022/cdc3156c-0334-0bba-70af-1df78fb18b50.htm): `FamilyManager.SetFormula`.
- [Associate an element parameter](https://www.revitapidocs.com/2022/a047ea58-0351-b419-d856-85ed23734ee8.htm): `AssociateElementParameterToFamilyParameter`.
- [Include non-visible geometry](https://www.revitapidocs.com/2022/0d277b80-d5e5-1151-d413-b0899da0976f.htm): useful for geometry inspection; it does **not** establish how the native Interference Check handles hidden solids.
- [Revit IFC exporter](https://github.com/Autodesk/revit-ifc): check the installed 2022 exporter and export an IFC4 file before asserting the final entity mapping.
- [Compile-only reference package](https://www.nuget.org/packages/Revit_All_Main_Versions_API_x64/2022.1.0): third-party NuGet package by Matthew.Taylor/WSP, containing Autodesk's API assemblies. The build script pins its SHA-256 and does not run package scripts. It is not Revit, a runtime, or a licence.

Autodesk runtime files, templates and API DLLs are not redistributed in this repository. The MIT licence applies to this repository's own source and documentation.
