using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Forms = System.Windows.Forms;

namespace WallSensorDemo
{
    [Transaction(TransactionMode.Manual)]
    public sealed class BuildDemoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var app = commandData.Application.Application;
            if (app.VersionNumber != "2022")
            {
                message = "This generator targets Revit 2022. Revit cannot save an older native format.";
                return Result.Failed;
            }
            string output = null;
            try
            {
                TaskDialog.Show("Wall sensor demo",
                    "Select an unhosted Metric Generic Model template, a Metric Generic Model face based template, " +
                    "and the Generic Model tag seed described in docs/tag-seed.md.\n\n" +
                    "The sensor insertion datum is the finished floor. Use a vertical planar wall. " +
                    "A new output folder will be created. Native interference checks, tag appearance and IFC export require the manual acceptance steps.");
                string genericTemplate = PickFile("Select unhosted Metric Generic Model.rft", "Revit family template|*.rft", app.FamilyTemplatePath);
                if (genericTemplate == null) return Result.Cancelled;
                string faceTemplate = PickFile("Select Metric Generic Model face based.rft", "Revit family template|*.rft", app.FamilyTemplatePath);
                if (faceTemplate == null) return Result.Cancelled;
                string tagSeed = PickFile("Select the prepared WS_SensorTag.rfa", "Revit family|*.rfa", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                if (tagSeed == null) return Result.Cancelled;
                ValidateTagSeed(app, tagSeed);
                string parent = PickDirectory("Choose a parent folder for a new sensor demo output folder");
                if (parent == null) return Result.Cancelled;
                output = Path.Combine(parent, "WallSensor-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(output);
                string sensor = FamilyBuilder.Build(app, genericTemplate, faceTemplate, output);
                string tagCopy = Path.Combine(output, "WS_SensorTag.rfa");
                File.Copy(tagSeed, tagCopy, false);
                string sample = SampleBuilder.Build(app, sensor, tagCopy, genericTemplate, output);
                long sensorBytes = new FileInfo(sensor).Length;
                string sizeStatus = sensorBytes < 1000000 ? "PASS" : "FAIL";
                File.WriteAllText(Path.Combine(output, "native-file-check.txt"),
                    "Host: Revit " + app.VersionNumber + " / " + app.VersionBuild + Environment.NewLine +
                    "Sensor bytes: " + sensorBytes + Environment.NewLine +
                    "Sensor < 1,000,000 bytes: " + sizeStatus + Environment.NewLine +
                    "Saved sample: " + Path.GetFileName(sample) + Environment.NewLine +
                    "Tag seed supplied by operator; inspect both unit labels in Revit." + Environment.NewLine +
                    "Saving is not acceptance. Review acceptance.csv and complete docs/acceptance.md." + Environment.NewLine);
                if (sensorBytes >= 1000000)
                    throw new InvalidOperationException("Sensor exceeds the strict 1,000,000-byte budget. Output was retained for diagnosis.");
                TaskDialog.Show("Wall sensor files saved", output + "\n\nReview acceptance.csv. Complete the native interference, tag, reopening and IFC tests before using these files as a finished deliverable.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                if (output != null && Directory.Exists(output))
                    File.WriteAllText(Path.Combine(output, "generation-error.log"), ex.ToString());
                message = ex.Message + (output == null ? "" : "\nDiagnostic folder: " + output);
                return Result.Failed;
            }
        }

        private static void ValidateTagSeed(Autodesk.Revit.ApplicationServices.Application app, string path)
        {
            Document seed = null;
            try
            {
                seed = app.OpenDocumentFile(path);
                if (!seed.IsFamilyDocument || seed.OwnerFamily.FamilyCategory.Id.IntegerValue != (int)BuiltInCategory.OST_GenericModelTags)
                    throw new InvalidOperationException("Select a Generic Model tag family prepared in Revit 2022. See docs/tag-seed.md.");
            }
            finally { if (seed != null) seed.Close(false); }
        }

        internal static string PickDirectory(string description)
        {
            using (var picker = new Forms.FolderBrowserDialog())
            {
                picker.Description = description;
                picker.ShowNewFolderButton = true;
                return picker.ShowDialog() == Forms.DialogResult.OK ? picker.SelectedPath : null;
            }
        }

        private static string PickFile(string title, string filter, string directory)
        {
            using (var picker = new Forms.OpenFileDialog())
            {
                picker.Title = title;
                picker.Filter = filter;
                picker.CheckFileExists = true;
                if (Directory.Exists(directory)) picker.InitialDirectory = directory;
                return picker.ShowDialog() == Forms.DialogResult.OK ? picker.FileName : null;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CheckDemoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument active = commandData.Application.ActiveUIDocument;
            if (active == null || active.Document.IsFamilyDocument || commandData.Application.Application.VersionNumber != "2022")
            {
                message = "Open the generated sample project in Revit 2022.";
                return Result.Failed;
            }
            string output = BuildDemoCommand.PickDirectory("Choose a folder for the native verification report");
            if (output == null) return Result.Cancelled;
            try
            {
                string report = Path.Combine(output, "Check-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(report);
                NativeVerifier.Verify(active.Document, report);
                TaskDialog.Show("Verification report", report + "\n\nCheck all FAIL and PENDING rows. An API report does not replace Revit Interference Check or IFC export validation.");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
