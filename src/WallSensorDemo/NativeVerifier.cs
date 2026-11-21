using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace WallSensorDemo
{
    /// <summary>Records only checks actually run in Revit. Manual/export checks stay PENDING.</summary>
    public static class NativeVerifier
    {
        private const double ToleranceMm = 0.0001;

        private sealed class ResultRow
        {
            public string Check;
            public string Fixture;
            public string Status;
            public string Detail;
        }

        private sealed class Piece
        {
            public string Subcategory;
            public Solid WorldSolid;
            public double MinimumZ = Double.PositiveInfinity;
            public double MaximumZ = Double.NegativeInfinity;
        }

        public static void Verify(Document doc, string outputDirectory)
        {
            List<ResultRow> results = new List<ResultRow>();
            string path = Path.Combine(outputDirectory, "acceptance.csv");
            try
            {
                Check(results, "Revit runtime", "application", doc.Application.VersionNumber == "2022",
                    "Observed VersionNumber=" + doc.Application.VersionNumber + "; VersionBuild=" + doc.Application.VersionBuild);
                List<FamilyInstance> instances = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>().Where(x => x.Symbol.Family.Name == Contract.FamilyName).ToList();
                Check(results, "Sample fixture count", "project", instances.Count == 7,
                    "Expected 2 display instances and 5 boundary fixtures; observed " + instances.Count);
                Family family = instances.Select(x => x.Symbol.Family).FirstOrDefault();
                if (family == null) throw new InvalidOperationException("No sensor family instances found.");
                string[] types = family.GetFamilySymbolIds().Select(x => ((FamilySymbol)doc.GetElement(x)).Name)
                    .OrderBy(x => x).ToArray();
                Check(results, "Family types", "family", types.SequenceEqual(new[] { "SE-200", "SE-300" }), String.Join("; ", types));
                Check(results, "Face placement type", "family", family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased,
                    family.FamilyPlacementType.ToString());
                Level floor = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(x => x.Name == SampleBuilder.FloorLevelName);
                if (floor == null) throw new InvalidOperationException("Finished floor level is missing.");
                foreach (FamilyInstance instance in instances) VerifyInstance(doc, instance, floor.Elevation, results);
                VerifySchedule(doc, results);
                VerifyClearanceOverlap(doc, instances, results);
                FamilyInstance specimen = instances.FirstOrDefault(x => Mark(x) == "DISPLAY-200");
                if (specimen != null) FlexSpecimen(doc, specimen, floor.Elevation, results);

                string sensorPath = Path.Combine(outputDirectory, Contract.FamilyName + ".rfa");
                if (File.Exists(sensorPath))
                {
                    long bytes = new FileInfo(sensorPath).Length;
                    Check(results, "Sensor file under 1 MB", "file", bytes < 1000000L,
                        bytes.ToString(CultureInfo.InvariantCulture) + " bytes; strict decimal MB threshold");
                }
                else Pending(results, "Sensor file under 1 MB", "Expected sensor source path was not present; check the actual saved .rfa.");
                Pending(results, "Native Interference Check", "Run Revit Collaborate > Interference Check with Generic Models versus Mechanical Equipment/Doors. Check visibility ON and OFF separately. API solid overlap does not validate this UI feature.");
                Pending(results, "IFC export entity", "Export IFC using the agreed schema/exporter. Parse the resulting file and verify IfcSensor; the parameter value alone is insufficient evidence.");
                Pending(results, "Tag label formatting", "Verify actual label content, Type Mark, 1400 AFF C/L (55 in), rounding at other heights, and readability in the saved views. Seed labels are authored in Revit UI.");
                Pending(results, "Plan annotation appearance", "Open plan views at coarse/medium/fine and relevant cut planes. Verify the 2D annotation symbol in the view, not only geometry existence.");
                Pending(results, "Saved file reopen", "Close and reopen all delivered .rfa/.rvt files in Revit 2022; verify no warnings, links, missing labels or upgrade prompts.");
                Pending(results, "Client final specification", "Category, mounting plate footprint, 1000 mm clearance interpretation, full parameter table, reference IFC and installation detail await client confirmation.");
            }
            catch (Exception exception)
            {
                results.Add(new ResultRow { Check = "Verifier execution", Fixture = "runtime", Status = "FAIL",
                    Detail = exception.GetType().Name + ": " + exception.Message });
                Write(path, results);
                throw new InvalidOperationException("Native verification could not complete. Diagnostic report: " + path, exception);
            }
            Write(path, results);
            if (results.Any(x => x.Status == "FAIL"))
                throw new InvalidOperationException("Native verification failed. Review " + path + ". The sample has not been saved as an accepted deliverable.");
        }

        private static void VerifyInstance(Document doc, FamilyInstance instance, double floorElevation, List<ResultRow> results)
        {
            string fixture = Mark(instance);
            Parameter height = instance.get_Parameter(Contract.MountingHeightGuid);
            Parameter status = instance.get_Parameter(Contract.BandStatusGuid);
            Check(results, "Shared Mounting Height GUID", fixture, height != null && height.IsShared && height.StorageType == StorageType.Double,
                Contract.MountingHeightGuid.ToString());
            Check(results, "Shared Band Status GUID", fixture, status != null && status.IsShared && status.StorageType == StorageType.String,
                Contract.BandStatusGuid.ToString());
            if (height == null || status == null) return;
            Parameter typeMark = instance.Symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
            Check(results, "Type Mark matches provisional type", fixture,
                typeMark != null && typeMark.AsString() == instance.Symbol.Name,
                "Expected " + instance.Symbol.Name + "; actual=" + (typeMark == null ? "missing" : typeMark.AsString()));
            double heightMm = Contract.Millimetres(height.AsDouble());
            // Integer tenth-millimetre fixture positions are compared with a small tolerance only at the native-geometry boundary.
            bool inside = heightMm - Contract.HeightMm / 2.0 >= Contract.BandBottomMm - ToleranceMm &&
                heightMm + Contract.HeightMm / 2.0 <= Contract.BandTopMm + ToleranceMm;
            string expected = inside ? "OK" : "OUT OF BAND";
            Check(results, "Calculated whole-device status", fixture, status.AsString() == expected,
                "Height=" + Number(heightMm) + " mm; expected=" + expected + "; actual=" + status.AsString());
            Transform transform = instance.GetTransform();
            Check(results, "Local vertical alignment", fixture, transform.BasisY.DotProduct(XYZ.BasisZ) > 0.999999,
                "Local Y dot world Z=" + Number(transform.BasisY.DotProduct(XYZ.BasisZ)));
            Check(results, "Family origin on FFL datum", fixture,
                Near(Contract.Millimetres(transform.Origin.Z - floorElevation), 0),
                "Origin AFF=" + Number(Contract.Millimetres(transform.Origin.Z - floorElevation)) + " mm");
            Check(results, "Actual face host", fixture, instance.HostFace != null, "HostFace reference must exist.");

            List<Piece> pieces = GetPieces(doc, instance, true);
            CheckExtent(results, fixture, "Device bottom AFF", pieces, Contract.DeviceSubcategory, floorElevation,
                heightMm - Contract.HeightMm / 2.0, false);
            CheckExtent(results, fixture, "Device top AFF", pieces, Contract.DeviceSubcategory, floorElevation,
                heightMm + Contract.HeightMm / 2.0, true);
            CheckExtent(results, fixture, "Fixed band bottom AFF", pieces, Contract.BandSubcategory, floorElevation,
                Contract.BandBottomMm, false);
            CheckExtent(results, fixture, "Fixed band top AFF", pieces, Contract.BandSubcategory, floorElevation,
                Contract.BandTopMm, true);
            List<Piece> clearance = pieces.Where(x => x.Subcategory == Contract.ClearanceSubcategory).ToList();
            Check(results, "Keep-clear is a subcategory solid", fixture, clearance.Count > 0 && clearance.Sum(x => x.WorldSolid.Volume) > 1e-9,
                "Solid count=" + clearance.Count + "; cubic feet=" + Number(clearance.Sum(x => x.WorldSolid.Volume)));
            VerifyLocalEnvelope(results, fixture, pieces, transform.Inverse);
            string mapping = TextParameter(instance, Contract.IfcExportAs);
            Check(results, "IFC mapping parameter", fixture, mapping == "IfcSensor", "IfcExportAs=" + mapping + "; actual IFC export remains pending.");
        }

        private static void VerifyLocalEnvelope(List<ResultRow> results, string fixture, List<Piece> pieces,
            Transform worldToFamily)
        {
            double[] body = LocalBounds(pieces.Where(x => x.Subcategory == Contract.DeviceSubcategory), worldToFamily);
            double[] clearance = LocalBounds(pieces.Where(x => x.Subcategory == Contract.ClearanceSubcategory), worldToFamily);
            if (body == null || clearance == null) return; // Missing solids are already recorded as failures above.
            Check(results, "Body width and depth", fixture,
                Near(body[3] - body[0], Contract.WidthMm) && Near(body[5] - body[2], Contract.DepthMm),
                "Measured width=" + Number(body[3] - body[0]) + ", depth=" + Number(body[5] - body[2]) + " mm");
            bool sides = Near(body[0] - clearance[0], Contract.ClearanceMm) &&
                Near(clearance[3] - body[3], Contract.ClearanceMm) &&
                Near(body[1] - clearance[1], Contract.ClearanceMm) &&
                Near(clearance[4] - body[4], Contract.ClearanceMm);
            Check(results, "1000 mm side/top/bottom clearance", fixture, sides,
                "Left=" + Number(body[0] - clearance[0]) + "; right=" + Number(clearance[3] - body[3]) +
                "; bottom=" + Number(body[1] - clearance[1]) + "; top=" + Number(clearance[4] - body[4]) + " mm");
            Check(results, "1000 mm front clearance and no behind-wall extension", fixture,
                Near(clearance[5] - body[5], Contract.ClearanceMm) && Near(clearance[2], 0),
                "Front allowance=" + Number(clearance[5] - body[5]) + "; back local Z=" + Number(clearance[2]) +
                " mm. This is the demo's stated clearance interpretation, pending client confirmation.");
        }

        private static double[] LocalBounds(IEnumerable<Piece> pieces, Transform worldToFamily)
        {
            double[] values = { Double.PositiveInfinity, Double.PositiveInfinity, Double.PositiveInfinity,
                Double.NegativeInfinity, Double.NegativeInfinity, Double.NegativeInfinity };
            bool any = false;
            foreach (Piece piece in pieces)
                foreach (Edge edge in piece.WorldSolid.Edges)
                    foreach (XYZ point in edge.Tessellate())
                    {
                        XYZ local = worldToFamily.OfPoint(point);
                        double[] coordinates = { Contract.Millimetres(local.X), Contract.Millimetres(local.Y), Contract.Millimetres(local.Z) };
                        for (int i = 0; i < 3; ++i)
                        {
                            values[i] = Math.Min(values[i], coordinates[i]);
                            values[i + 3] = Math.Max(values[i + 3], coordinates[i]);
                        }
                        any = true;
                    }
            return any ? values : null;
        }

        private static void FlexSpecimen(Document doc, FamilyInstance specimen, double floorElevation, List<ResultRow> results)
        {
            using (Transaction transaction = new Transaction(doc, "Verify parameter flex; always roll back"))
            {
                transaction.Start();
                try
                {
                    double[] positions = { 1164.4, 1164.5, 1400, 1635.5, 1635.6 };
                    Parameter height = specimen.get_Parameter(Contract.MountingHeightGuid);
                    if (height == null || height.IsReadOnly) throw new InvalidOperationException("Mounting Height is not editable during flex verification.");
                    foreach (double position in positions)
                    {
                        height.Set(Contract.Feet(position));
                        doc.Regenerate();
                        int start = results.Count;
                        VerifyInstance(doc, specimen, floorElevation, results);
                        for (int i = start; i < results.Count; ++i) results[i].Fixture = "FLEX " + Number(position) + " mm";
                    }
                    Parameter visible = specimen.LookupParameter(Contract.ShowKeepClear);
                    Check(results, "Keep-clear visibility control", "FLEX", visible != null && visible.StorageType == StorageType.Integer && !visible.IsReadOnly,
                        "Editable Yes/No instance parameter required.");
                    if (visible != null && !visible.IsReadOnly)
                    {
                        visible.Set(1);
                        doc.Regenerate();
                        int on = GetPieces(doc, specimen, false).Count(x => x.Subcategory == Contract.ClearanceSubcategory);
                        visible.Set(0);
                        doc.Regenerate();
                        int off = GetPieces(doc, specimen, false).Count(x => x.Subcategory == Contract.ClearanceSubcategory);
                        Pending(results, "Keep-clear rendered visibility",
                            "Observed API solid counts ON=" + on + ", OFF=" + off + ". Inspect the actual view; API extraction is not proof of rendered visibility.");
                        int hidden = GetPieces(doc, specimen, true).Count(x => x.Subcategory == Contract.ClearanceSubcategory);
                        Pending(results, "Hidden clearance interference behavior",
                            "Observed IncludeNonVisibleObjects=true count=" + hidden + ". Retrieval is not a release criterion or proof of native interference eligibility. Test the agreed OFF-state behavior in Revit.");
                    }
                }
                finally { transaction.RollBack(); }
            }
        }

        private static void VerifySchedule(Document doc, List<ResultRow> results)
        {
            ViewSchedule schedule = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .FirstOrDefault(x => !x.IsTemplate && x.Name == "04 — Sensor mounting schedule");
            Check(results, "Sensor schedule exists", "project", schedule != null, "04 — Sensor mounting schedule");
            if (schedule == null) return;
            HashSet<int> fields = new HashSet<int>();
            foreach (ScheduleFieldId id in schedule.Definition.GetFieldOrder())
            {
                ScheduleField field = schedule.Definition.GetField(id);
                fields.Add(field.ParameterId.IntegerValue);
            }
            SharedParameterElement height = SharedParameterElement.Lookup(doc, Contract.MountingHeightGuid);
            SharedParameterElement status = SharedParameterElement.Lookup(doc, Contract.BandStatusGuid);
            Check(results, "Schedule Type Mark", "project", fields.Contains((int)BuiltInParameter.ALL_MODEL_TYPE_MARK), "Built-in Type Mark field.");
            Check(results, "Schedule Mounting Height", "project", height != null && fields.Contains(height.Id.IntegerValue), "Shared GUID field; not copied text.");
            Check(results, "Schedule Band Status", "project", status != null && fields.Contains(status.Id.IntegerValue), "Shared GUID field; native formula result.");
        }

        private static void VerifyClearanceOverlap(Document doc, List<FamilyInstance> sensors, List<ResultRow> results)
        {
            FamilyInstance sensor = sensors.FirstOrDefault(x => Mark(x) == "DISPLAY-200");
            FamilyInstance clearSensor = sensors.FirstOrDefault(x => Mark(x) == "DISPLAY-300");
            FamilyInstance grille = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .FirstOrDefault(x => x.Symbol.Family.Name == SampleBuilder.GrilleFamilyName);
            if (sensor == null || clearSensor == null || grille == null)
            {
                Check(results, "Clearance test fixtures", "project", false, "Missing sensor or grille fixture.");
                return;
            }
            List<Piece> grillePieces = GetPieces(doc, grille, true);
            double collision = IntersectionVolume(GetPieces(doc, sensor, true).Where(x => x.Subcategory == Contract.ClearanceSubcategory), grillePieces);
            double clear = IntersectionVolume(GetPieces(doc, clearSensor, true).Where(x => x.Subcategory == Contract.ClearanceSubcategory), grillePieces);
            Check(results, "Native solid overlap positive control", "DISPLAY-200 / grille", collision > 1e-9,
                "Intersecting volume=" + Number(collision) + " ft^3; this is a Boolean-solid check, not the Interference Check UI.");
            Check(results, "Native solid overlap negative control", "DISPLAY-300 / grille", clear < 1e-9,
                "Intersecting volume=" + Number(clear) + " ft^3.");
        }

        private static double IntersectionVolume(IEnumerable<Piece> first, IEnumerable<Piece> second)
        {
            double sum = 0;
            foreach (Piece a in first)
                foreach (Piece b in second)
                {
                    Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(a.WorldSolid, b.WorldSolid, BooleanOperationsType.Intersect);
                    if (intersection != null) sum += Math.Abs(intersection.Volume);
                }
            return sum;
        }

        private static List<Piece> GetPieces(Document doc, Element element, bool includeHidden)
        {
            Options options = new Options { IncludeNonVisibleObjects = includeHidden, DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
            List<Piece> result = new List<Piece>();
            GeometryElement geometry = element.get_Geometry(options);
            if (geometry != null) Collect(doc, geometry, Transform.Identity, result);
            return result;
        }

        private static void Collect(Document doc, GeometryElement geometry, Transform transform, List<Piece> output)
        {
            foreach (GeometryObject item in geometry)
            {
                GeometryInstance instance = item as GeometryInstance;
                if (instance != null)
                {
                    Collect(doc, instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), output);
                    continue;
                }
                Solid solid = item as Solid;
                if (solid == null || solid.Faces.Size == 0 || Math.Abs(solid.Volume) < 1e-12) continue;
                string subcategory = Style(doc, solid.GraphicsStyleId);
                if (String.IsNullOrEmpty(subcategory))
                    foreach (Face face in solid.Faces)
                    {
                        subcategory = Style(doc, face.GraphicsStyleId);
                        if (!String.IsNullOrEmpty(subcategory)) break;
                    }
                Solid world = SolidUtils.CreateTransformed(solid, transform);
                Piece piece = new Piece { Subcategory = subcategory, WorldSolid = world };
                foreach (Edge edge in world.Edges)
                    foreach (XYZ point in edge.Tessellate())
                    {
                        piece.MinimumZ = Math.Min(piece.MinimumZ, point.Z);
                        piece.MaximumZ = Math.Max(piece.MaximumZ, point.Z);
                    }
                output.Add(piece);
            }
        }

        private static string Style(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return String.Empty;
            GraphicsStyle style = doc.GetElement(id) as GraphicsStyle;
            return style == null || style.GraphicsStyleCategory == null ? String.Empty : style.GraphicsStyleCategory.Name;
        }

        private static void CheckExtent(List<ResultRow> results, string fixture, string check, List<Piece> pieces,
            string subcategory, double floorElevation, double expected, bool upper)
        {
            List<Piece> selected = pieces.Where(x => x.Subcategory == subcategory).ToList();
            if (selected.Count == 0)
            {
                Check(results, check, fixture, false, "No measurable solid in " + subcategory);
                return;
            }
            double z = upper ? selected.Max(x => x.MaximumZ) : selected.Min(x => x.MinimumZ);
            double actual = Contract.Millimetres(z - floorElevation);
            Check(results, check, fixture, Near(actual, expected), "Actual=" + Number(actual) + " mm; expected=" + Number(expected) + " mm; tolerance=" + Number(ToleranceMm) + " mm");
        }

        private static string TextParameter(FamilyInstance instance, string name)
        {
            Parameter parameter = instance.LookupParameter(name) ?? instance.Symbol.LookupParameter(name);
            return parameter == null ? String.Empty : parameter.AsString();
        }

        private static string Mark(Element element)
        {
            Parameter mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            return mark == null || String.IsNullOrEmpty(mark.AsString()) ? element.Id.IntegerValue.ToString(CultureInfo.InvariantCulture) : mark.AsString();
        }

        private static bool Near(double actual, double expected) { return Math.Abs(actual - expected) <= ToleranceMm; }
        private static string Number(double value) { return value.ToString("0.########", CultureInfo.InvariantCulture); }

        private static void Check(List<ResultRow> results, string check, string fixture, bool passed, string detail)
        {
            results.Add(new ResultRow { Check = check, Fixture = fixture, Status = passed ? "PASS" : "FAIL", Detail = detail });
        }

        private static void Pending(List<ResultRow> results, string check, string detail)
        {
            results.Add(new ResultRow { Check = check, Fixture = "manual", Status = "PENDING", Detail = detail });
        }

        private static string Csv(string value) { return "\"" + (value ?? String.Empty).Replace("\"", "\"\"") + "\""; }

        private static void Write(string path, IEnumerable<ResultRow> results)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("Check,Fixture,Status,Detail");
                foreach (ResultRow row in results)
                    writer.WriteLine(String.Join(",", new[] { Csv(row.Check), Csv(row.Fixture), Csv(row.Status), Csv(row.Detail) }));
            }
        }
    }
}
