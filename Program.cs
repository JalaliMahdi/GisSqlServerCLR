using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.Types;

namespace GisSqlCLR
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  GIS SQL CLR - Coordinate Transformation Tests");
            Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

            TestBasicRoundTrip();
            TestHighPrecision();
            TestMultipleEPSG();

            Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  Tests Completed - Press any key to exit");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.ReadKey();
        }

        static void TestBasicRoundTrip()
        {
            Console.WriteLine("▶ Test 1: Basic Round-trip (WGS84 ↔ Web Mercator)");
            Console.WriteLine("───────────────────────────────────────────────────────────────");

            double lon = -74.0060;
            double lat = 40.7128;

            Console.WriteLine($"  Input (WGS84):  Lon={lon}, Lat={lat}");

            SqlGeometry wgs84 = SqlGeometry.STGeomFromText(new SqlChars($"POINT ({lon} {lat})"), 4326);
            SqlGeometry mercator = SpatialReprojection.TransformGeometry(wgs84, 3857);
            
            Console.WriteLine($"  Web Mercator:   X={mercator.STX.Value:F2}, Y={mercator.STY.Value:F2}");

            SqlGeometry result = SpatialReprojection.TransformGeometry(mercator, 4326);
            
            Console.WriteLine($"  Output (WGS84): Lon={result.STX.Value}, Lat={result.STY.Value}");
            Console.WriteLine();
            
            double diffLon = Math.Abs(lon - result.STX.Value);
            double diffLat = Math.Abs(lat - result.STY.Value);
            
            Console.WriteLine($"  Difference:");
            Console.WriteLine($"    Longitude: {diffLon:E} degrees");
            Console.WriteLine($"    Latitude:  {diffLat:E} degrees");
            Console.WriteLine($"    Distance:  ~{CalculateErrorMM(diffLon, diffLat):F4} mm\n");
        }

        static void TestHighPrecision()
        {
            Console.WriteLine("▶ Test 2: High Precision (14 decimal places)");
            Console.WriteLine("───────────────────────────────────────────────────────────────");

            var tests = new[]
            {
                new { Name = "New York", Lon = -74.00601234567890, Lat = 40.71282345678901 },
                new { Name = "Los Angeles", Lon = -118.24371234567890, Lat = 34.05223456789012 },
                new { Name = "Chicago", Lon = -87.62983456789012, Lat = 41.87813456789012 }
            };

            foreach (var test in tests)
            {
                Console.WriteLine($"  {test.Name}:");
                Console.WriteLine($"    Input:  {test.Lon:F14}, {test.Lat:F14}");

                SqlGeometry point = SqlGeometry.STGeomFromText(
                    new SqlChars($"POINT ({test.Lon:F14} {test.Lat:F14})"), 4326);
                
                SqlGeometry mercator = SpatialReprojection.TransformGeometry(point, 3857);
                SqlGeometry back = SpatialReprojection.TransformGeometry(mercator, 4326);

                Console.WriteLine($"    Output: {back.STX.Value:F14}, {back.STY.Value:F14}");
                
                double errLon = Math.Abs(test.Lon - back.STX.Value);
                double errLat = Math.Abs(test.Lat - back.STY.Value);
                
                Console.WriteLine($"    Error:  {errLon:E} (lon), {errLat:E} (lat)");
                Console.WriteLine($"    Status: {GetStatus(errLon, errLat)}\n");
            }
        }

        static void TestMultipleEPSG()
        {
            Console.WriteLine("▶ Test 3: Multiple EPSG Support");
            Console.WriteLine("───────────────────────────────────────────────────────────────");

            double lon = -74.0060;
            double lat = 40.7128;

            var projections = new[]
            {
                new { Code = 3857, Name = "Web Mercator" },
                new { Code = 32618, Name = "UTM Zone 18N (New York)" },
                new { Code = 32617, Name = "UTM Zone 17N" },
                new { Code = 32619, Name = "UTM Zone 19N" }
            };

            SqlGeometry original = SqlGeometry.STGeomFromText(
                new SqlChars($"POINT ({lon} {lat})"), 4326);

            Console.WriteLine($"  Original (WGS84): ({lon}, {lat})\n");

            foreach (var proj in projections)
            {
                try
                {
                    SqlGeometry transformed = SpatialReprojection.TransformGeometry(original, proj.Code);
                    SqlGeometry back = SpatialReprojection.TransformGeometry(transformed, 4326);

                    double errLon = Math.Abs(lon - back.STX.Value);
                    double errLat = Math.Abs(lat - back.STY.Value);

                    Console.WriteLine($"  EPSG:{proj.Code} - {proj.Name}");
                    Console.WriteLine($"    Transformed: ({transformed.STX.Value:F2}, {transformed.STY.Value:F2})");
                    Console.WriteLine($"    Back to WGS84: ({back.STX.Value:F10}, {back.STY.Value:F10})");
                    Console.WriteLine($"    Error: {errLon:E} (lon), {errLat:E} (lat)");
                    Console.WriteLine($"    Status: {GetStatus(errLon, errLat)}\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  EPSG:{proj.Code} - {proj.Name}");
                    Console.WriteLine($"    ✗ ERROR: {ex.Message}\n");
                }
            }
        }

        static string GetStatus(double lonError, double latError)
        {
            if (lonError < 1e-10 && latError < 1e-10)
                return "✓ EXCELLENT (< 1e-10)";
            else if (lonError < 1e-8 && latError < 1e-8)
                return "✓ GOOD (< 1e-8)";
            else if (lonError < 1e-6 && latError < 1e-6)
                return "⚠ ACCEPTABLE (< 1e-6)";
            else
                return "✗ POOR (≥ 1e-6)";
        }

        static double CalculateErrorMM(double lonError, double latError)
        {
            double errorKm = Math.Sqrt(Math.Pow(lonError * 111, 2) + Math.Pow(latError * 111, 2));
            return errorKm * 1_000_000;
        }
    }
}
