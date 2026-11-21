using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace WallSensorDemo
{
    /// <summary>Creates a small, reproducible native test scene. All lengths are millimetres at this boundary.</summary>
    public static class SampleBuilder
    {
        public const string FloorLevelName = "FFL 00";
        public const string DoorFamilyName = "WS_DoorProxy";
        public const string GrilleFamilyName = "WS_GrilleProxy";

        public static string Build(Application app, string sensorFamilyPath, string tagSeedPath,
            string genericTemplate, string outputDirectory)
        {
            if (app.VersionNumber != "2022")
                throw new InvalidOperationException("The sample must be generated in Revit 2022.");
            RequireFile(sensorFamilyPath, "sensor family");
            RequireFile(tagSeedPath, "prepared Generic Model tag family");
            RequireFile(genericTemplate, "non-hosted Generic Model family template");
            Directory.CreateDirectory(outputDirectory);
            string samplePath = Path.Combine(outputDirectory, "WS_SensorSample.rvt");
            if (File.Exists(samplePath)) throw new IOException("Refusing to overwrite " + samplePath);

            string doorPath = BuildProxy(app, genericTemplate, outputDirectory, true);
            string grillePath = BuildProxy(app, genericTemplate, outputDirectory, false);
            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            try
            {
                using (Transaction transaction = new Transaction(doc, "Build wall sensor test scene"))
                {
                    transaction.Start();
                    Level level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                        .FirstOrDefault(x => Math.Abs(x.Elevation) < 1e-8);
                    if (level == null) level = Level.Create(doc, 0.0);
                    level.Name = FloorLevelName;

                    WallType wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                        .FirstOrDefault(x => x.Kind == WallKind.Basic);
                    if (wallType == null) throw new InvalidOperationException("Metric project has no basic wall type.");
                    Wall displayWall = Wall.Create(doc, Line.CreateBound(Point(0, 0, 0), Point(10000, 0, 0)),
                        wallType.Id, level.Id, Contract.Feet(3000), 0, false, false);
                    Wall testWall = Wall.Create(doc, Line.CreateBound(Point(0, 8000, 0), Point(22000, 8000, 0)),
                        wallType.Id, level.Id, Contract.Feet(3000), 0, false, false);
                    displayWall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                        .Set("Display wall. Door and grille are native geometric proxies, not manufacturer products.");
                    testWall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                        .Set("Boundary fixtures: 1164.4 / 1164.5 / 1400 / 1635.5 / 1635.6 mm AFF C/L.");
                    doc.Regenerate();

                    Family sensor = Load(doc, sensorFamilyPath);
                    Family tag = Load(doc, tagSeedPath);
                    Family door = Load(doc, doorPath);
                    Family grille = Load(doc, grillePath);
                    Dictionary<string, FamilySymbol> sensors = sensor.GetFamilySymbolIds()
                        .Select(id => (FamilySymbol)doc.GetElement(id)).ToDictionary(x => x.Name);
                    if (!sensors.ContainsKey("SE-200") || !sensors.ContainsKey("SE-300") || sensors.Count != 2)
                        throw new InvalidOperationException("Sensor family must contain exactly SE-200 and SE-300.");
                    foreach (FamilySymbol symbol in sensors.Values) if (!symbol.IsActive) symbol.Activate();
                    FamilySymbol doorSymbol = FirstSymbol(doc, door);
                    FamilySymbol grilleSymbol = FirstSymbol(doc, grille);
                    FamilySymbol tagSymbol = FirstSymbol(doc, tag);
                    if (tagSymbol.Category == null || tagSymbol.Category.Id.IntegerValue != (int)BuiltInCategory.OST_GenericModelTags)
                        throw new InvalidOperationException("Tag seed must use the Generic Model Tags category.");
                    doc.Regenerate();

                    FaceHost displayHost = GetInteriorFace(doc, displayWall);
                    FaceHost testHost = GetInteriorFace(doc, testWall);
                    List<FamilyInstance> displaySensors = new List<FamilyInstance>();
                    displaySensors.Add(PlaceSensor(doc, displayHost, sensors["SE-200"], 3300, 1400, "DISPLAY-200"));
                    displaySensors.Add(PlaceSensor(doc, displayHost, sensors["SE-300"], 7500, 1400, "DISPLAY-300"));
                    double[] boundaryHeights = { 1164.4, 1164.5, 1400.0, 1635.5, 1635.6 };
                    for (int i = 0; i < boundaryHeights.Length; ++i)
                        PlaceSensor(doc, testHost, sensors[i % 2 == 0 ? "SE-200" : "SE-300"],
                            1000 + i * 4000, boundaryHeights[i], "BOUNDARY-" + (i + 1));

                    // The opening belongs to the wall. The separately categorised family supplies a plain leaf and frame.
                    doc.Create.NewOpening(displayWall, Point(1550, 0, 0), Point(2450, 0, 2100));
                    XYZ doorPoint = ProjectToHostPlane(displayHost, Point(2000, 0, 0));
                    FamilyInstance doorInstance = doc.Create.NewFamilyInstance(doorPoint, doorSymbol, level, StructuralType.NonStructural);
                    SetComments(doorInstance, "Door proxy: 900 x 2100 mm nominal opening; no hardware or door performance data.");
                    XYZ grillePoint = ProjectToHostPlane(displayHost, Point(4200, 0, 1500));
                    FamilyInstance grilleInstance = doc.Create.NewFamilyInstance(grillePoint, grilleSymbol, level, StructuralType.NonStructural);
                    // The proxy projects from the same side of the wall as the sensor. Its volume intentionally enters the clearance zone.
                    if (displayHost.Normal.Y < 0)
                        ElementTransformUtils.RotateElement(doc, grilleInstance.Id,
                            Line.CreateUnbound(grillePoint, XYZ.BasisZ), Math.PI);
                    SetComments(grilleInstance, "Grille proxy: 600 x 300 x 80 mm. Intentionally intersects DISPLAY-200 keep-clear volume.");

                    ViewPlan plan = CreatePlan(doc, level);
                    ViewSection elevation = CreateElevation(doc, displayHost);
                    Create3D(doc);
                    CreateSchedule(doc);
                    foreach (FamilyInstance instance in displaySensors)
                    {
                        double height = instance.get_Parameter(Contract.MountingHeightGuid).AsDouble();
                        XYZ centre = instance.GetTransform().OfPoint(new XYZ(0, height, 0));
                        IndependentTag.Create(doc, tagSymbol.Id, elevation.Id, new Reference(instance), false,
                            TagOrientation.Horizontal, centre + Point(900, 0, 400));
                    }
                    doc.ProjectInformation.Name = "Wall sensor — mounting and clearance study";
                    doc.ProjectInformation.Number = "WS-01";
                    SetComments(doc.ProjectInformation,
                        "Revit 2022 sample. AFF datum is level FFL 00. Family origins are on that datum. See acceptance.csv for actual checks and outstanding manual tests.");
                    doc.Regenerate();
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit did not commit the sample scene transaction.");
                }

                NativeVerifier.Verify(doc, outputDirectory);
                doc.SaveAs(samplePath, NewSaveOptions());
                return samplePath;
            }
            finally
            {
                if (doc != null && doc.IsValidObject) doc.Close(false);
            }
        }

        private static void RequireFile(string path, string label)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("Missing " + label + ".", path);
        }

        private static Family Load(Document doc, string path)
        {
            Family family;
            if (!doc.LoadFamily(path, out family) || family == null)
                throw new InvalidOperationException("Could not load family: " + path);
            return family;
        }

        private static FamilySymbol FirstSymbol(Document doc, Family family)
        {
            ElementId id = family.GetFamilySymbolIds().FirstOrDefault();
            if (id == null) throw new InvalidOperationException("Family has no types: " + family.Name);
            FamilySymbol symbol = (FamilySymbol)doc.GetElement(id);
            if (!symbol.IsActive) symbol.Activate();
            return symbol;
        }

        private sealed class FaceHost
        {
            public ElementId WallId;
            public Reference Reference;
            public XYZ Origin;
            public XYZ Normal;
        }

        private static FaceHost GetInteriorFace(Document doc, Wall wall)
        {
            foreach (Reference reference in HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior))
            {
                PlanarFace face = wall.GetGeometryObjectFromReference(reference) as PlanarFace;
                if (face != null && Math.Abs(face.FaceNormal.Z) < 1e-8)
                    return new FaceHost { WallId = wall.Id, Reference = reference,
                        Origin = new XYZ(face.Origin.X, face.Origin.Y, face.Origin.Z),
                        Normal = new XYZ(face.FaceNormal.X, face.FaceNormal.Y, face.FaceNormal.Z).Normalize() };
            }
            throw new InvalidOperationException("No vertical planar interior wall face found.");
        }

        private static XYZ ProjectToHostPlane(FaceHost host, XYZ point)
        {
            // Use a value snapshot of the untrimmed plane. Door centres fall inside the opening,
            // and a face object retained across regeneration may no longer be valid.
            return point - host.Normal.Multiply((point - host.Origin).DotProduct(host.Normal));
        }

        private static Reference RefreshHostReference(Document doc, FaceHost host)
        {
            Wall wall = doc.GetElement(host.WallId) as Wall;
            if (wall == null) throw new InvalidOperationException("The sensor host wall no longer exists.");
            foreach (Reference reference in HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior))
            {
                PlanarFace face = wall.GetGeometryObjectFromReference(reference) as PlanarFace;
                if (face == null || face.FaceNormal.Normalize().DotProduct(host.Normal) < 0.999999) continue;
                double offset = Math.Abs((face.Origin - host.Origin).DotProduct(host.Normal));
                if (offset > 1e-8) continue;
                host.Reference = reference;
                return reference;
            }
            throw new InvalidOperationException("The interior host face changed; a matching current reference could not be resolved.");
        }

        private static FamilyInstance PlaceSensor(Document doc, FaceHost host, FamilySymbol symbol,
            double xMm, double heightMm, string mark)
        {
            XYZ origin = ProjectToHostPlane(host, Point(xMm, 0, 0));
            Reference currentReference = RefreshHostReference(doc, host);
            FamilyInstance instance = doc.Create.NewFamilyInstance(currentReference, origin, XYZ.BasisX, symbol);
            doc.Regenerate();
            XYZ localUp = instance.GetTransform().BasisY.Normalize();
            XYZ normal = host.Normal;
            double angle = Math.Atan2(normal.DotProduct(localUp.CrossProduct(XYZ.BasisZ)), localUp.DotProduct(XYZ.BasisZ));
            if (Math.Abs(angle) > 1e-9)
            {
                ElementTransformUtils.RotateElement(doc, instance.Id, Line.CreateUnbound(origin, normal), angle);
                doc.Regenerate();
            }
            if (instance.GetTransform().BasisY.DotProduct(XYZ.BasisZ) < 0.999999)
                throw new InvalidOperationException("Face placement did not align local Y with project vertical.");
            if (instance.GetTransform().BasisZ.DotProduct(normal) < 0.999999)
                throw new InvalidOperationException("Face placement would bury the sensor in the wall: local Z must point outward.");
            Parameter height = instance.get_Parameter(Contract.MountingHeightGuid);
            if (height == null || height.IsReadOnly) throw new InvalidOperationException("Mounting Height shared instance parameter is missing or locked.");
            height.Set(Contract.Feet(heightMm));
            Parameter clearance = instance.LookupParameter(Contract.ShowKeepClear);
            Parameter band = instance.LookupParameter(Contract.ShowMountingBand);
            if (clearance != null && !clearance.IsReadOnly) clearance.Set(1);
            if (band != null && !band.IsReadOnly) band.Set(1);
            instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(mark);
            SetComments(instance, "Origin = FFL 00. Set Mounting Height to move device; moving the entire instance changes the AFF datum.");
            return instance;
        }

        private static ViewFamilyType ViewType(Document doc, ViewFamily family)
        {
            ViewFamilyType type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == family);
            if (type == null) throw new InvalidOperationException("No view type available: " + family);
            return type;
        }

        private static ViewPlan CreatePlan(Document doc, Level level)
        {
            ViewPlan view = ViewPlan.Create(doc, ViewType(doc, ViewFamily.FloorPlan).Id, level.Id);
            view.Name = "01 — Plan and boundary fixtures";
            view.Scale = 50;
            view.DetailLevel = ViewDetailLevel.Fine;
            return view;
        }

        private static ViewSection CreateElevation(Document doc, FaceHost host)
        {
            Transform transform = Transform.Identity;
            transform.Origin = ProjectToHostPlane(host, Point(5000, 0, 1500))
                + host.Normal.Multiply(Contract.Feet(2500));
            transform.BasisY = XYZ.BasisZ;
            transform.BasisZ = -host.Normal;
            transform.BasisX = transform.BasisY.CrossProduct(transform.BasisZ).Normalize();
            BoundingBoxXYZ box = new BoundingBoxXYZ();
            box.Transform = transform;
            box.Min = Point(-5500, -1700, 0);
            box.Max = Point(5500, 1800, 3500);
            ViewSection view = ViewSection.CreateSection(doc, ViewType(doc, ViewFamily.Section).Id, box);
            view.Name = "02 — Mounting elevation";
            view.Scale = 25;
            view.DetailLevel = ViewDetailLevel.Fine;
            return view;
        }

        private static void Create3D(Document doc)
        {
            View3D view = View3D.CreateIsometric(doc, ViewType(doc, ViewFamily.ThreeDimensional).Id);
            view.Name = "03 — Clearance coordination";
            view.DetailLevel = ViewDetailLevel.Fine;
            view.DisplayStyle = DisplayStyle.Shading;
        }

        private static void CreateSchedule(Document doc)
        {
            ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            schedule.Name = "04 — Sensor mounting schedule";
            ScheduleDefinition definition = schedule.Definition;
            IList<SchedulableField> available = definition.GetSchedulableFields();
            AddField(definition, available, new ElementId(BuiltInParameter.ALL_MODEL_MARK), "Mark");
            AddField(definition, available, new ElementId(BuiltInParameter.ALL_MODEL_TYPE_MARK), "Type Mark");
            SharedParameterElement height = SharedParameterElement.Lookup(doc, Contract.MountingHeightGuid);
            SharedParameterElement status = SharedParameterElement.Lookup(doc, Contract.BandStatusGuid);
            if (height == null || status == null) throw new InvalidOperationException("Shared schedule fields were not loaded with the sensor family.");
            ScheduleField heightField = AddField(definition, available, height.Id, "Mounting Height (mm AFF C/L)");
            FormatOptions format = new FormatOptions(UnitTypeId.Millimeters);
            format.UseDefault = false;
            format.Accuracy = 0.1;
            heightField.SetFormatOptions(format);
            AddField(definition, available, status.Id, "Band Status");
            definition.IsItemized = true;
        }

        private static ScheduleField AddField(ScheduleDefinition definition, IList<SchedulableField> available,
            ElementId parameterId, string heading)
        {
            SchedulableField field = available.FirstOrDefault(x => x.ParameterId == parameterId);
            if (field == null) throw new InvalidOperationException("Parameter is not schedulable: " + heading);
            ScheduleField added = definition.AddField(field);
            added.ColumnHeading = heading;
            return added;
        }

        private static string BuildProxy(Application app, string template, string directory, bool door)
        {
            string name = door ? DoorFamilyName : GrilleFamilyName;
            string components = Path.Combine(directory, "source-components");
            Directory.CreateDirectory(components);
            string path = Path.Combine(components, name + ".rfa");
            if (File.Exists(path)) throw new IOException("Refusing to overwrite " + path);
            Document family = app.NewFamilyDocument(template);
            try
            {
                using (Transaction transaction = new Transaction(family, "Build " + name))
                {
                    transaction.Start();
                    family.OwnerFamily.FamilyCategory = Category.GetCategory(family,
                        door ? BuiltInCategory.OST_Doors : BuiltInCategory.OST_MechanicalEquipment);
                    family.FamilyManager.NewType("Geometric proxy");
                    if (door)
                    {
                        Box(family, -430, -20, 0, 430, 20, 2070);
                        Box(family, -475, -40, 0, -450, 40, 2150);
                        Box(family, 450, -40, 0, 475, 40, 2150);
                        Box(family, -475, -40, 2100, 475, 40, 2150);
                    }
                    else
                    {
                        Box(family, -300, 0, -150, 300, 80, 150);
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit did not commit the " + name + " proxy transaction.");
                }
                family.SaveAs(path, NewSaveOptions());
                return path;
            }
            finally { if (family.IsValidObject) family.Close(false); }
        }

        private static void Box(Document family, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, Point(0, 0, z0));
            SketchPlane sketch = SketchPlane.Create(family, plane);
            XYZ[] points = { Point(x0, y0, z0), Point(x1, y0, z0), Point(x1, y1, z0), Point(x0, y1, z0) };
            CurveArray loop = new CurveArray();
            for (int i = 0; i < 4; ++i) loop.Append(Line.CreateBound(points[i], points[(i + 1) % 4]));
            CurveArrArray loops = new CurveArrArray();
            loops.Append(loop);
            family.FamilyCreate.NewExtrusion(true, loops, sketch, Contract.Feet(z1 - z0));
        }

        private static SaveAsOptions NewSaveOptions()
        {
            return new SaveAsOptions { Compact = true, OverwriteExistingFile = false, MaximumBackups = 1 };
        }

        private static XYZ Point(double x, double y, double z)
        {
            return new XYZ(Contract.Feet(x), Contract.Feet(y), Contract.Feet(z));
        }

        private static void SetComments(Element element, string text)
        {
            Parameter parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (parameter != null && !parameter.IsReadOnly) parameter.Set(text);
        }
    }
}
