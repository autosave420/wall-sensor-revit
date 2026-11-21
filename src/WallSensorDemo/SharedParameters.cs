using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace WallSensorDemo
{
    public static class SharedParameters
    {
        // These GUIDs are the contract between the sensor, tag and schedule.
        // Changing the name alone does not create an equivalent tag parameter.
        public static void WriteFile(string path)
        {
            string contents = "# Revit shared parameter definitions for Wall Sensor Demo.\r\n"
                + "*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n"
                + "*GROUP\tID\tNAME\r\nGROUP\t1\tWall Sensor Demo\r\n"
                + "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n"
                + "PARAM\t" + Contract.MountingHeightGuid + "\t" + Contract.MountingHeight
                + "\tLENGTH\t\t1\t1\tDevice centre above the finished-floor insertion datum.\t1\t0\r\n"
                + "PARAM\t" + Contract.BandStatusGuid + "\t" + Contract.BandStatus
                + "\tTEXT\t\t1\t1\tCalculated check of the complete device against the mounting band.\t1\t0\r\n";

            if (File.Exists(path))
            {
                if (!String.Equals(File.ReadAllText(path), contents, StringComparison.Ordinal))
                    throw new IOException("Refusing to replace a different shared parameter file: " + path);
                return;
            }
            string parent = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(parent);
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(contents);
        }

        internal static IDictionary<string, FamilyParameter> AddRequiredParameters(
            Application application, Document familyDocument, string path)
        {
            WriteFile(path);
            string previous = application.SharedParametersFilename;
            try
            {
                application.SharedParametersFilename = Path.GetFullPath(path);
                DefinitionFile file = application.OpenSharedParameterFile();
                if (file == null) throw new InvalidOperationException("Revit could not read the shared parameter file.");
                DefinitionGroup group = file.Groups.get_Item("Wall Sensor Demo");
                if (group == null) throw new InvalidOperationException("The shared parameter group is missing.");

                Dictionary<string, FamilyParameter> result = new Dictionary<string, FamilyParameter>();
                Add(familyDocument.FamilyManager, group, result, Contract.MountingHeight,
                    Contract.MountingHeightGuid, BuiltInParameterGroup.PG_GEOMETRY);
                Add(familyDocument.FamilyManager, group, result, Contract.BandStatus,
                    Contract.BandStatusGuid, BuiltInParameterGroup.PG_IDENTITY_DATA);
                return result;
            }
            finally
            {
                application.SharedParametersFilename = previous;
            }
        }

        private static void Add(FamilyManager manager, DefinitionGroup group,
            IDictionary<string, FamilyParameter> result, string name, Guid expected,
            BuiltInParameterGroup parameterGroup)
        {
            ExternalDefinition definition = group.Definitions.get_Item(name) as ExternalDefinition;
            if (definition == null || definition.GUID != expected)
                throw new InvalidOperationException("Shared parameter GUID does not match: " + name);
            result.Add(name, manager.AddParameter(definition, parameterGroup, true));
        }
    }
}
