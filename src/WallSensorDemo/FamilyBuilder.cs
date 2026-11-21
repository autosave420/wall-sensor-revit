using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace WallSensorDemo
{
    /// <summary>
    /// Builds native families only when called from a Revit 2022 API context.
    /// The face-based family's insertion datum is the finished-floor position.
    /// Local X is width, local Y is height, local Z points away from the wall.
    /// </summary>
    public static class FamilyBuilder
    {
        public static string Build(Application app, string genericTemplate, string faceTemplate,
            string outputDirectory)
        {
            if (app == null) throw new ArgumentNullException("app");
            if (app.VersionNumber != "2022")
                throw new InvalidOperationException("Run this builder in Revit 2022. Revit cannot save to an older release.");
            RequireTemplate(genericTemplate);
            RequireTemplate(faceTemplate);
            Directory.CreateDirectory(outputDirectory);
            string components = Path.Combine(outputDirectory, "source-components");
            Directory.CreateDirectory(components);
            string housingPath = Path.Combine(components, "WS_Housing.rfa");
            string sensorPath = Path.Combine(outputDirectory, Contract.FamilyName + ".rfa");
            RequireNewFile(housingPath);
            RequireNewFile(sensorPath);
            string definitionsPath = Path.Combine(outputDirectory, "WS_SharedParameters.txt");
            SharedParameters.WriteFile(definitionsPath);

            Document housing = null;
            Document sensor = null;
            try
            {
                housing = app.NewFamilyDocument(genericTemplate);
                if (housing == null) throw new InvalidOperationException("The generic family template could not be opened.");
                if (housing.OwnerFamily.FamilyPlacementType != FamilyPlacementType.OneLevelBased)
                    throw new InvalidOperationException("The housing template must be an unhosted Generic Model template, not a face-, wall- or work-plane-based template.");
                BuildHousing(housing);
                SaveNewFamily(housing, housingPath);

                sensor = app.NewFamilyDocument(faceTemplate);
                if (sensor == null || sensor.OwnerFamily.FamilyPlacementType != FamilyPlacementType.WorkPlaneBased)
                    throw new InvalidOperationException("Select a Revit 2022 face-based Generic Model template.");
                // LoadFamily requires a target document with no open transaction.
                Family nested = housing.LoadFamily(sensor);
                if (nested == null) throw new InvalidOperationException("The housing could not be loaded into the sensor family.");
                BuildSensor(app, sensor, nested, definitionsPath);
                SaveNewFamily(sensor, sensorPath);
                return sensorPath;
            }
            finally
            {
                try
                {
                    if (sensor != null && sensor.IsValidObject) sensor.Close(false);
                }
                finally
                {
                    if (housing != null && housing.IsValidObject) housing.Close(false);
                }
            }
        }

        private static void BuildHousing(Document document)
        {
            using (Transaction transaction = new Transaction(document, "Build sensor housing"))
            {
                transaction.Start();
                SetGenericCategory(document);
                SetIntegerIfWritable(document.OwnerFamily, BuiltInParameter.FAMILY_WORK_PLANE_BASED, 0);
                SetIntegerIfWritable(document.OwnerFamily, BuiltInParameter.FAMILY_SHARED, 0);
                SetIntegerIfWritable(document.OwnerFamily, BuiltInParameter.FAMILY_ALWAYS_VERTICAL, 0);
                FamilyManager manager = document.FamilyManager;
                manager.NewType("Housing");
                FamilyParameter showClear = manager.AddParameter(Contract.ShowKeepClear,
                    GroupTypeId.Visibility, SpecTypeId.Boolean.YesNo, true);
                manager.Set(showClear, 1);

                View plan = FindXYView(document);
                EnsureCentreReference(document, plan);
                Category bodyCategory = Subcategory(document, Contract.DeviceSubcategory, new Color(65, 70, 72));
                Category clearCategory = Subcategory(document, Contract.ClearanceSubcategory, new Color(205, 140, 30));
                Category symbolCategory = Subcategory(document, Contract.SymbolSubcategory, new Color(35, 60, 72));
                ElementId bodyMaterial = Material(document, "WS_Housing finish", new Color(223, 226, 224), 0);
                ElementId clearMaterial = Material(document, "WS_Keep-clear volume", new Color(225, 174, 60), 90);

                double w = Contract.Feet(Contract.WidthMm);
                double h = Contract.Feet(Contract.HeightMm);
                double d = Contract.Feet(Contract.DepthMm);
                SketchPlane facePlane = SketchPlane.Create(document,
                    Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
                Extrusion(document, RoundedRectangle(w, h, Contract.Feet(8)),
                    facePlane, d, bodyCategory, bodyMaterial);
                double c = Contract.Feet(Contract.ClearanceMm);
                Extrusion clearance = Extrusion(document, Rectangle(-w / 2 - c, -h / 2 - c,
                    w / 2 + c, h / 2 + c, 0), facePlane, d + c, clearCategory, clearMaterial);
                Parameter visible = clearance.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);
                if (visible == null) throw new InvalidOperationException("The clearance extrusion exposes no visibility parameter.");
                manager.AssociateElementParameterToFamilyParameter(visible, showClear);
                AddPlanSymbol(document, symbolCategory);
                document.Regenerate();
                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit did not commit the housing transaction.");
            }
        }

        private static void BuildSensor(Application app, Document document, Family housing,
            string definitionsPath)
        {
            using (Transaction transaction = new Transaction(document, "Build face-based sensor"))
            {
                transaction.Start();
                SetGenericCategory(document);
                SetIntegerIfWritable(document.OwnerFamily, BuiltInParameter.FAMILY_ALWAYS_VERTICAL, 0);
                FamilyManager manager = document.FamilyManager;
                FamilyType first = manager.NewType("SE-200");
                IDictionary<string, FamilyParameter> shared =
                    SharedParameters.AddRequiredParameters(app, document, definitionsPath);
                FamilyParameter height = shared[Contract.MountingHeight];
                manager.Set(height, Contract.Feet(Contract.DefaultHeightMm));
                ConstantLength(manager, Contract.DeviceWidth, Contract.WidthMm);
                ConstantLength(manager, Contract.DeviceHeight, Contract.HeightMm);
                ConstantLength(manager, Contract.DeviceDepth, Contract.DepthMm);
                ConstantLength(manager, Contract.BandMinimum, Contract.BandBottomMm);
                ConstantLength(manager, Contract.BandMaximum, Contract.BandTopMm);
                manager.SetFormula(shared[Contract.BandStatus], Contract.StatusFormula);
                FamilyParameter clear = manager.AddParameter(Contract.ShowKeepClear,
                    GroupTypeId.Visibility, SpecTypeId.Boolean.YesNo, true);
                FamilyParameter bandVisible = manager.AddParameter(Contract.ShowMountingBand,
                    GroupTypeId.Visibility, SpecTypeId.Boolean.YesNo, true);
                manager.Set(clear, 1);
                manager.Set(bandVisible, 1);
                FamilyParameter ifcClass = manager.AddParameter(Contract.IfcExportAs,
                    GroupTypeId.Ifc, SpecTypeId.String.Text, false);
                FamilyParameter ifcType = manager.AddParameter(Contract.IfcExportType,
                    GroupTypeId.Ifc, SpecTypeId.String.Text, false);

                // Mirror the nested subcategories in the host for predictable project VG controls.
                Subcategory(document, Contract.DeviceSubcategory, new Color(65, 70, 72));
                Subcategory(document, Contract.ClearanceSubcategory, new Color(205, 140, 30));
                Subcategory(document, Contract.SymbolSubcategory, new Color(35, 60, 72));
                View plan = FindXYView(document);
                ReferencePlane floorDatum = EnsureCentreReference(document, plan);
                floorDatum.Name = "WS_FinishedFloor";
                floorDatum.Pinned = true;

                FamilySymbol symbol = housing.GetFamilySymbolIds()
                    .Select(id => document.GetElement(id) as FamilySymbol).FirstOrDefault();
                if (symbol == null) throw new InvalidOperationException("No housing symbol was found.");
                if (!symbol.IsActive) symbol.Activate();
                document.Regenerate();
                FamilyInstance instance = document.FamilyCreate.NewFamilyInstance(
                    new XYZ(0, Contract.Feet(Contract.DefaultHeightMm), 0), symbol, StructuralType.NonStructural);
                Parameter nestedClear = instance.LookupParameter(Contract.ShowKeepClear);
                if (nestedClear == null) throw new InvalidOperationException("The housing visibility parameter is missing.");
                manager.AssociateElementParameterToFamilyParameter(nestedClear, clear);
                document.Regenerate();
                IList<Reference> centres = instance.GetReferences(FamilyInstanceReferenceType.CenterFrontBack);
                if (centres == null || centres.Count != 1)
                    throw new InvalidOperationException("The housing must have exactly one Centre (Front/Back) reference.");
                ReferenceArray references = new ReferenceArray();
                references.Append(floorDatum.GetReference());
                references.Append(centres[0]);
                double dimensionX = Contract.Feet(-300);
                Dimension dimension = document.FamilyCreate.NewDimension(plan,
                    Line.CreateBound(new XYZ(dimensionX, 0, 0),
                        new XYZ(dimensionX, Contract.Feet(Contract.DefaultHeightMm), 0)), references);
                dimension.FamilyLabel = height;

                Category bandCategory = Subcategory(document, Contract.BandSubcategory, new Color(45, 135, 110));
                ElementId bandMaterial = Material(document, "WS_Installation band", new Color(80, 171, 141), 80);
                double bandFaceOffset = Contract.Feet(0.5);
                SketchPlane bandPlane = SketchPlane.Create(document,
                    Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, bandFaceOffset)));
                Extrusion band = Extrusion(document,
                    Rectangle(Contract.Feet(-175), Contract.Feet(Contract.BandBottomMm),
                        Contract.Feet(175), Contract.Feet(Contract.BandTopMm), bandFaceOffset),
                    bandPlane, Contract.Feet(0.5), bandCategory, bandMaterial);
                manager.AssociateElementParameterToFamilyParameter(
                    band.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM), bandVisible);

                FamilyParameter mark = manager.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
                if (mark == null) throw new InvalidOperationException("The template does not contain the built-in Type Mark parameter.");
                SetTypeValues(manager, first, "SE-200", mark, height, clear, bandVisible, ifcClass, ifcType);
                FamilyType second = manager.NewType("SE-300");
                SetTypeValues(manager, second, "SE-300", mark, height, clear, bandVisible, ifcClass, ifcType);
                manager.CurrentType = first;
                if (manager.Types.Size != 2)
                    throw new InvalidOperationException("Use a blank face-based template: the sensor must contain exactly two types.");
                CheckFamilyFlex(document, instance, height, shared[Contract.BandStatus]);
                document.Regenerate();
                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit did not commit the sensor transaction.");
            }
        }

        private static void CheckFamilyFlex(Document document, FamilyInstance housing,
            FamilyParameter height, FamilyParameter status)
        {
            FamilyManager manager = document.FamilyManager;
            double low = Contract.BandBottomMm + Contract.HeightMm / 2;
            double high = Contract.BandTopMm - Contract.HeightMm / 2;
            double[] cases = { Contract.DefaultHeightMm, low, low - 0.1, high, high + 0.1 };
            try
            {
                foreach (double value in cases)
                {
                    manager.Set(height, Contract.Feet(value));
                    document.Regenerate();
                    double actual = Contract.Millimetres(housing.GetTransform().Origin.Y);
                    if (Math.Abs(actual - value) > 0.01)
                        throw new InvalidOperationException("The mounting-height dimension did not move the housing to "
                            + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " mm.");
                    string actualStatus = manager.CurrentType.AsString(status);
                    if (actualStatus != Contract.ExpectedStatus(value))
                        throw new InvalidOperationException("The family band-status formula failed at "
                            + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " mm.");
                }
            }
            finally
            {
                manager.Set(height, Contract.Feet(Contract.DefaultHeightMm));
                document.Regenerate();
            }
        }

        private static void SetTypeValues(FamilyManager manager, FamilyType type, string markText,
            FamilyParameter mark, FamilyParameter height, FamilyParameter clear, FamilyParameter band,
            FamilyParameter ifcClass, FamilyParameter ifcType)
        {
            manager.CurrentType = type;
            manager.Set(mark, markText);
            manager.Set(height, Contract.Feet(Contract.DefaultHeightMm));
            manager.Set(clear, 1);
            manager.Set(band, 1);
            manager.Set(ifcClass, "IfcSensor");
            manager.Set(ifcType, "USERDEFINED");
        }

        private static void ConstantLength(FamilyManager manager, string name, double millimetres)
        {
            FamilyParameter parameter = manager.AddParameter(name, GroupTypeId.Geometry,
                SpecTypeId.Length, false);
            manager.SetFormula(parameter,
                millimetres.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture) + " mm");
        }

        private static ReferencePlane EnsureCentreReference(Document document, View view)
        {
            List<ReferencePlane> planes = new FilteredElementCollector(document)
                .OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().ToList();
            foreach (ReferencePlane plane in planes)
            {
                Parameter definesOrigin = plane.get_Parameter(BuiltInParameter.DATUM_PLANE_DEFINES_ORIGIN);
                if (definesOrigin != null && definesOrigin.AsInteger() == 1
                    && Math.Abs(plane.GetPlane().Normal.DotProduct(plane.GetPlane().Origin)) > 1e-7)
                    throw new InvalidOperationException("Template origin plane '" + plane.Name
                        + "' is offset from model zero. Use an unmodified Revit 2022 template with its origin at zero.");
            }

            ReferencePlane x = ZeroReference(document, view, planes, XYZ.BasisX,
                FamilyInstanceReferenceType.CenterLeftRight);
            ReferencePlane y = ZeroReference(document, view, planes, XYZ.BasisY,
                FamilyInstanceReferenceType.CenterFrontBack);
            foreach (ReferencePlane plane in planes)
            {
                Parameter definesOrigin = plane.get_Parameter(BuiltInParameter.DATUM_PLANE_DEFINES_ORIGIN);
                if (plane.Id != x.Id && plane.Id != y.Id && definesOrigin != null
                    && definesOrigin.AsInteger() == 1)
                {
                    if (definesOrigin.IsReadOnly)
                        throw new InvalidOperationException("An additional template origin plane cannot be cleared: " + plane.Name);
                    definesOrigin.Set(0);
                }
            }
            DefineOrigin(x);
            DefineOrigin(y);
            document.Regenerate();
            return y;
        }

        private static ReferencePlane ZeroReference(Document document, View view, IList<ReferencePlane> planes,
            XYZ normal, FamilyInstanceReferenceType reference)
        {
            ReferencePlane plane = planes.FirstOrDefault(p =>
                Math.Abs(p.GetPlane().Normal.DotProduct(normal)) > 0.9999
                && Math.Abs(p.GetPlane().Origin.DotProduct(normal)) < 1e-7);
            if (plane == null)
            {
                XYZ along = normal.IsAlmostEqualTo(XYZ.BasisX) ? XYZ.BasisY : XYZ.BasisX;
                plane = document.FamilyCreate.NewReferencePlane(along.Multiply(-2),
                    along.Multiply(2), XYZ.BasisZ, view);
            }
            foreach (ReferencePlane other in planes)
            {
                Parameter role = other.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME);
                if (other.Id != plane.Id && role != null && role.AsInteger() == (int)reference)
                    throw new InvalidOperationException("The template has a conflicting " + reference
                        + " reference. Use an unmodified template with one centre reference per axis.");
            }
            Parameter referenceType = plane.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME);
            if (referenceType == null || referenceType.IsReadOnly)
                throw new InvalidOperationException("The template's centre reference cannot be configured.");
            referenceType.Set((int)reference);
            return plane;
        }

        private static void DefineOrigin(ReferencePlane plane)
        {
            Parameter parameter = plane.get_Parameter(BuiltInParameter.DATUM_PLANE_DEFINES_ORIGIN);
            if (parameter == null || (parameter.IsReadOnly && parameter.AsInteger() != 1))
                throw new InvalidOperationException("The centre reference cannot define the insertion origin: " + plane.Name);
            if (!parameter.IsReadOnly) parameter.Set(1);
            if (parameter.AsInteger() != 1)
                throw new InvalidOperationException("Revit did not accept the centre reference as the insertion origin.");
            plane.Pinned = true;
        }

        private static View FindXYView(Document document)
        {
            View view = new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && Math.Abs(v.ViewDirection.DotProduct(XYZ.BasisZ)) > 0.9999);
            if (view == null) throw new InvalidOperationException("The family template needs a plan view normal to local Z.");
            return view;
        }

        private static void AddPlanSymbol(Document document, Category category)
        {
            // This plane becomes horizontal in the project when local Y is wall-up.
            SketchPlane plane = SketchPlane.Create(document,
                Plane.CreateByNormalAndOrigin(XYZ.BasisY, XYZ.Zero));
            double r = Contract.Feet(35), z = Contract.Feet(70);
            List<Curve> curves = new List<Curve>();
            curves.Add(Arc.Create(new XYZ(-r, 0, z), new XYZ(r, 0, z), new XYZ(0, 0, z + r)));
            curves.Add(Arc.Create(new XYZ(r, 0, z), new XYZ(-r, 0, z), new XYZ(0, 0, z - r)));
            curves.Add(Line.CreateBound(new XYZ(0, 0, 0), new XYZ(0, 0, z - r)));
            double stroke = Contract.Feet(15);
            curves.Add(Line.CreateBound(new XYZ(-stroke, 0, z), new XYZ(stroke, 0, z)));
            curves.Add(Line.CreateBound(new XYZ(0, 0, z - stroke), new XYZ(0, 0, z + stroke)));
            foreach (Curve curve in curves)
            {
                SymbolicCurve symbolic = document.FamilyCreate.NewSymbolicCurve(curve, plane);
                symbolic.LineStyle = category.GetGraphicsStyle(GraphicsStyleType.Projection);
                FamilyElementVisibility visibility = new FamilyElementVisibility(FamilyElementVisibilityType.ViewSpecific);
                visibility.IsShownInCoarse = true;
                visibility.IsShownInMedium = true;
                visibility.IsShownInFine = true;
                visibility.IsShownOnlyWhenCut = false;
                symbolic.SetVisibility(visibility);
            }
        }

        private static Category Subcategory(Document document, string name, Color color)
        {
            Category parent = document.OwnerFamily.FamilyCategory;
            Category category = parent.SubCategories.Cast<Category>().FirstOrDefault(c => c.Name == name)
                ?? document.Settings.Categories.NewSubcategory(parent, name);
            category.LineColor = color;
            category.SetLineWeight(1, GraphicsStyleType.Projection);
            return category;
        }

        private static ElementId Material(Document document, string name, Color color, int transparency)
        {
            ElementId id = Autodesk.Revit.DB.Material.Create(document, name);
            Autodesk.Revit.DB.Material material = document.GetElement(id) as Autodesk.Revit.DB.Material;
            material.Color = color;
            material.Transparency = transparency;
            material.Shininess = 20;
            return id;
        }

        private static Extrusion Extrusion(Document document, CurveArray outline, SketchPlane plane,
            double depth, Category category, ElementId material)
        {
            CurveArrArray loops = new CurveArrArray();
            loops.Append(outline);
            Extrusion extrusion = document.FamilyCreate.NewExtrusion(true, loops, plane, depth);
            extrusion.Subcategory = category;
            Parameter materialParameter = extrusion.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (materialParameter != null && !materialParameter.IsReadOnly) materialParameter.Set(material);
            return extrusion;
        }

        private static CurveArray Rectangle(double left, double bottom, double right, double top, double z)
        {
            XYZ a = new XYZ(left, bottom, z), b = new XYZ(right, bottom, z);
            XYZ c = new XYZ(right, top, z), d = new XYZ(left, top, z);
            CurveArray curves = new CurveArray();
            curves.Append(Line.CreateBound(a, b)); curves.Append(Line.CreateBound(b, c));
            curves.Append(Line.CreateBound(c, d)); curves.Append(Line.CreateBound(d, a));
            return curves;
        }

        private static CurveArray RoundedRectangle(double width, double height, double radius)
        {
            double l = -width / 2, r = width / 2, b = -height / 2, t = height / 2;
            double diagonal = radius / Math.Sqrt(2);
            CurveArray curves = new CurveArray();
            XYZ a = new XYZ(l + radius, b, 0), c = new XYZ(r - radius, b, 0);
            XYZ d = new XYZ(r, b + radius, 0), e = new XYZ(r, t - radius, 0);
            XYZ f = new XYZ(r - radius, t, 0), g = new XYZ(l + radius, t, 0);
            XYZ h = new XYZ(l, t - radius, 0), i = new XYZ(l, b + radius, 0);
            curves.Append(Line.CreateBound(a, c));
            curves.Append(Arc.Create(c, d, new XYZ(r - radius + diagonal, b + radius - diagonal, 0)));
            curves.Append(Line.CreateBound(d, e));
            curves.Append(Arc.Create(e, f, new XYZ(r - radius + diagonal, t - radius + diagonal, 0)));
            curves.Append(Line.CreateBound(f, g));
            curves.Append(Arc.Create(g, h, new XYZ(l + radius - diagonal, t - radius + diagonal, 0)));
            curves.Append(Line.CreateBound(h, i));
            curves.Append(Arc.Create(i, a, new XYZ(l + radius - diagonal, b + radius - diagonal, 0)));
            return curves;
        }

        private static void SetGenericCategory(Document document)
        {
            document.OwnerFamily.FamilyCategory = document.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
        }

        private static void SetIntegerIfWritable(Element element, BuiltInParameter key, int value)
        {
            Parameter parameter = element.get_Parameter(key);
            if (parameter != null && !parameter.IsReadOnly) parameter.Set(value);
        }

        private static void RequireTemplate(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)
                || !String.Equals(Path.GetExtension(path), ".rft", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("Select an installed Revit 2022 family template (.rft).", path);
        }

        private static void RequireNewFile(string path)
        {
            if (File.Exists(path)) throw new IOException("Refusing to overwrite an existing family: " + path);
        }

        private static void SaveNewFamily(Document document, string path)
        {
            RequireNewFile(path);
            using (SaveAsOptions options = new SaveAsOptions())
            {
                options.OverwriteExistingFile = false;
                options.Compact = true;
                options.MaximumBackups = 1;
                document.SaveAs(path, options);
            }
        }
    }
}
