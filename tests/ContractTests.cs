using System;
using System.Globalization;
using WallSensorDemo;

internal static class ContractTests
{
    private static int passed;
    private static int failed;

    private static void Equal(string name, string expected, string actual)
    {
        if (String.Equals(expected, actual, StringComparison.Ordinal))
            passed++;
        else
        {
            failed++;
            Console.Error.WriteLine("FAIL {0}: expected {1}, got {2}", name, expected, actual);
        }
    }

    private static void Close(string name, double expected, double actual, double tolerance)
    {
        if (!Double.IsNaN(actual) && !Double.IsInfinity(actual) && Math.Abs(expected - actual) <= tolerance)
            passed++;
        else
        {
            failed++;
            Console.Error.WriteLine("FAIL {0}: expected {1:R}, got {2:R}", name, expected, actual);
        }
    }

    public static int Main()
    {
        // Expected outcomes are hand-derived from a 129 mm body and a 1100-1700 mm band.
        // Cases just outside each limit catch centre-only checks and exclusive comparisons.
        double[] heights = {
            -100.0, 0.0, 1100.0, 1164.4, 1164.4999, 1164.5,
            1164.5001, 1200.0, 1400.0, 1600.0, 1635.4999,
            1635.5, 1635.5001, 1635.6, 1700.0, 2500.0
        };
        string[] expected = {
            "OUT OF BAND", "OUT OF BAND", "OUT OF BAND", "OUT OF BAND", "OUT OF BAND", "OK",
            "OK", "OK", "OK", "OK", "OK", "OK", "OUT OF BAND", "OUT OF BAND", "OUT OF BAND", "OUT OF BAND"
        };
        for (int i = 0; i < heights.Length; i++)
            Equal("height " + heights[i].ToString("0.####", CultureInfo.InvariantCulture), expected[i], Contract.ExpectedStatus(heights[i]));

        // Freeze the published dimensional assumption. Changing orientation changes the acceptance limits.
        Close("width mm", 155.0, Contract.WidthMm, 0.0);
        Close("height mm", 129.0, Contract.HeightMm, 0.0);
        Close("depth mm", 34.0, Contract.DepthMm, 0.0);
        Close("default centre mm", 1400.0, Contract.DefaultHeightMm, 0.0);
        Close("fixed lower datum mm", 1100.0, Contract.BandBottomMm, 0.0);
        Close("fixed upper datum mm", 1700.0, Contract.BandTopMm, 0.0);
        Close("clearance mm", 1000.0, Contract.ClearanceMm, 0.0);

        Close("one foot in internal units", 1.0, Contract.Feet(304.8), 1e-12);
        Close("one inch in internal units", 1.0 / 12.0, Contract.Feet(25.4), 1e-12);
        Close("one foot to mm", 304.8, Contract.Millimetres(1.0), 1e-10);
        Close("zero", 0.0, Contract.Feet(0.0), 0.0);
        Close("negative datum", -1.0, Contract.Feet(-304.8), 1e-12);
        Close("1400 mm in feet", 4.593175853018373, Contract.Feet(1400.0), 1e-12);
        Close("1400 mm in inches", 55.11811023622047, Contract.Feet(1400.0) * 12.0, 1e-10);
        Close("rounded inch display", 55.0, Math.Round(Contract.Feet(1400.0) * 12.0), 0.0);
        Close("55 inches is 1397 mm", 1397.0, Contract.Millimetres(55.0 / 12.0), 1e-10);
        Close("height round trip", 1635.5, Contract.Millimetres(Contract.Feet(1635.5)), 1e-9);

        // A tag seed and the model family must share these exact external identities.
        Equal("height shared GUID", "683c7d22-6dd7-4f63-b753-f38b50c043f7", Contract.MountingHeightGuid.ToString("D"));
        Equal("status shared GUID", "ab717f7f-bc7e-4b66-a31d-ff138d3c3125", Contract.BandStatusGuid.ToString("D"));

        Console.WriteLine("{0} passed; {1} failed.", passed, failed);
        Console.WriteLine("Scope: arithmetic and shared-parameter contract only. Revit geometry, formula parsing, tags, interference, IFC and native file size are not tested here.");
        return failed == 0 ? 0 : 1;
    }
}
