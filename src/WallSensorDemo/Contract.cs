using System;

namespace WallSensorDemo
{
    public static class Contract
    {
        public const string FamilyName = "WS_WallSensor";
        public const string MountingHeight = "Mounting Height";
        public const string DeviceWidth = "Device Width";
        public const string DeviceHeight = "Device Height";
        public const string DeviceDepth = "Device Depth";
        public const string BandMinimum = "Band Minimum";
        public const string BandMaximum = "Band Maximum";
        public const string BandStatus = "Band Status";
        public const string ShowKeepClear = "Show Keep Clear";
        public const string ShowMountingBand = "Show Mounting Band";
        public const string IfcExportAs = "IfcExportAs";
        public const string IfcExportType = "IfcExportType";
        public const string DeviceSubcategory = "WS_Device";
        public const string ClearanceSubcategory = "WS_KeepClear";
        public const string BandSubcategory = "WS_MountingBand";
        public const string SymbolSubcategory = "WS_PlanSymbol";
        public const string StatusFormula = "if(or((Mounting Height - Device Height / 2) < Band Minimum, (Mounting Height + Device Height / 2) > Band Maximum), \"OUT OF BAND\", \"OK\")";
        public static readonly Guid MountingHeightGuid = new Guid("683c7d22-6dd7-4f63-b753-f38b50c043f7");
        public static readonly Guid BandStatusGuid = new Guid("ab717f7f-bc7e-4b66-a31d-ff138d3c3125");
        public const double WidthMm = 155.0;
        public const double HeightMm = 129.0;
        public const double DepthMm = 34.0;
        public const double DefaultHeightMm = 1400.0;
        public const double BandBottomMm = 1100.0;
        public const double BandTopMm = 1700.0;
        public const double ClearanceMm = 1000.0;
        public static double Feet(double millimetres) { return millimetres / 304.8; }
        public static double Millimetres(double feet) { return feet * 304.8; }
        public static string ExpectedStatus(double heightMm)
        {
            return (heightMm - HeightMm / 2.0 < BandBottomMm ||
                    heightMm + HeightMm / 2.0 > BandTopMm) ? "OUT OF BAND" : "OK";
        }
    }
}
