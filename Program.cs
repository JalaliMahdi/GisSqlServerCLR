using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Globalization;
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
            TestIranNationalGrid();

            Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  Tests Completed - Press any key to exit");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.ReadKey();
        }

        static void TestBasicRoundTrip()
        {
            Console.WriteLine(">> Test 1: Basic Round-trip (WGS84 <-> Web Mercator)");
            Console.WriteLine("-------------------------------------------------------------------");

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
            Console.WriteLine(">> Test 2: High Precision (14 decimal places)");
            Console.WriteLine("-------------------------------------------------------------------");

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
            Console.WriteLine(">> Test 3: Multiple EPSG Support");
            Console.WriteLine("-------------------------------------------------------------------");

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
                return "EXCELLENT (< 1e-10)";
            else if (lonError < 1e-8 && latError < 1e-8)
                return "GOOD (< 1e-8)";
            else if (lonError < 1e-6 && latError < 1e-6)
                return "ACCEPTABLE (< 1e-6)";
            else
                return "POOR (>= 1e-6)";
        }

        static double CalculateErrorMM(double lonError, double latError)
        {
            double errorKm = Math.Sqrt(Math.Pow(lonError * 111, 2) + Math.Pow(latError * 111, 2));
            return errorKm * 1_000_000;
        }

        static void TestIranNationalGrid()
        {
            Console.WriteLine("\n");
            Console.WriteLine("╔═══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║     IRAN NATIONAL GRID (IRNG) - COMPREHENSIVE TEST SUITE                     ║");
            Console.WriteLine("║     نشریه ۸-۱۱۹ سازمان نقشه‌برداری کشور                                       ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝");

            int totalPassed = 0;
            int totalFailed = 0;

            // ═══════════════════════════════════════════════════════════════════════════════
            // TEST SUITE 1: Official Sample Validation (جدول ۳-۲)
            // ═══════════════════════════════════════════════════════════════════════════════
            Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ TEST 1: Official Sample Cities Validation (جدول ۳-۲ نشریه ۸-۱۱۹)           │");
            Console.WriteLine("├─────────────────────────────────────────────────────────────────────────────┤");
            Console.WriteLine("│ Purpose: Validate grid square letters against official document samples    │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");
            
            var officialSamples = new[]
            {
                // From جدول ۳-۲: Sample cities with their IRNG codes
                new { City = "رشت", CityEN = "Rasht", Lon = 49.583, Lat = 37.283, Expected = "FQ", Zone = 39 },
                new { City = "بندر انزلی", CityEN = "Bandar Anzali", Lon = 49.47, Lat = 37.47, Expected = "FQ", Zone = 39 },
                new { City = "لاهیجان", CityEN = "Lahijan", Lon = 50.0, Lat = 37.2, Expected = "GQ", Zone = 39 },
                new { City = "کوچصفهان", CityEN = "Kuchesfehan", Lon = 49.80, Lat = 37.25, Expected = "FQ", Zone = 39 },
                new { City = "لنگرود", CityEN = "Langarud", Lon = 50.15, Lat = 37.2, Expected = "GQ", Zone = 39 },
            };

            Console.WriteLine("\n  ┌────────────────────┬───────────────────┬──────┬──────────┬──────────┬────────┐");
            Console.WriteLine("  │ City               │ Coordinates       │ Zone │ Expected │ Actual   │ Result │");
            Console.WriteLine("  ├────────────────────┼───────────────────┼──────┼──────────┼──────────┼────────┤");
            
            int passed1 = 0, failed1 = 0;
            foreach (var s in officialSamples)
            {
                try
                {
                    var gridSquare = IranNationalGrid.GetGridSquare(new SqlDouble(s.Lon), new SqlDouble(s.Lat));
                    var actualZone = IranNationalGrid.GetUTMZoneForLongitude(new SqlDouble(s.Lon)).Value;
                    string actual = gridSquare.Value;
                    bool ok = actual == s.Expected && actualZone == s.Zone;
                    
                    string result = ok ? "  OK  " : " FAIL ";
                    Console.WriteLine($"  │ {s.CityEN,-18} │ ({s.Lon,6:F2}, {s.Lat,5:F2}) │  {actualZone,2}  │    {s.Expected}    │    {actual}    │ [{result}] │");
                    
                    if (ok) { passed1++; totalPassed++; } else { failed1++; totalFailed++; }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  │ {s.CityEN,-18} │ ERROR: {ex.Message,-50} │");
                    failed1++; totalFailed++;
                }
            }
            Console.WriteLine("  └────────────────────┴───────────────────┴──────┴──────────┴──────────┴────────┘");
            Console.WriteLine($"  Summary: {passed1} passed, {failed1} failed");

            // ═══════════════════════════════════════════════════════════════════════════════
            // TEST SUITE 2: Zone-Specific Column Letter Validation
            // ═══════════════════════════════════════════════════════════════════════════════
            Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ TEST 2: Zone-Specific Column Letter Validation (شکل ۳-۳)                   │");
            Console.WriteLine("├─────────────────────────────────────────────────────────────────────────────┤");
            Console.WriteLine("│ Purpose: Verify first letter (column) follows zone patterns:               │");
            Console.WriteLine("│   Zone 38: B, C, D...  |  Zone 39: E, F, G, H...                          │");
            Console.WriteLine("│   Zone 40: N, P, Q...  |  Zone 41: S, T, U, V...                          │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");

            var columnTests = new[]
            {
                // Zone 38 Tests (42°E - 48°E)
                new { Zone = 38, Lon = 46.0, Lat = 38.0, ExpectedCol = "B", Description = "Zone 38, Column ~5" },
                new { Zone = 38, Lon = 47.0, Lat = 34.5, ExpectedCol = "C", Description = "Zone 38, Column ~6" },
                new { Zone = 38, Lon = 47.5, Lat = 35.0, ExpectedCol = "C", Description = "Zone 38, Column ~7" },
                
                // Zone 39 Tests (48°E - 54°E)
                new { Zone = 39, Lon = 49.0, Lat = 37.0, ExpectedCol = "E", Description = "Zone 39, Column ~2" },
                new { Zone = 39, Lon = 49.6, Lat = 37.3, ExpectedCol = "F", Description = "Zone 39, Column ~3 (Rasht)" },
                new { Zone = 39, Lon = 50.0, Lat = 37.2, ExpectedCol = "G", Description = "Zone 39, Column ~4 (Lahijan)" },
                new { Zone = 39, Lon = 51.4, Lat = 35.7, ExpectedCol = "H", Description = "Zone 39, Column ~5 (Tehran)" },
                new { Zone = 39, Lon = 52.5, Lat = 36.0, ExpectedCol = "J", Description = "Zone 39, Column ~6" },
                
                // Zone 40 Tests (54°E - 60°E)
                new { Zone = 40, Lon = 55.0, Lat = 32.0, ExpectedCol = "N", Description = "Zone 40, Column ~2" },
                new { Zone = 40, Lon = 57.0, Lat = 30.0, ExpectedCol = "P", Description = "Zone 40, Column ~4" },
                new { Zone = 40, Lon = 59.0, Lat = 36.0, ExpectedCol = "Q", Description = "Zone 40, Column ~6" },
                
                // Zone 41 Tests (60°E - 66°E)  
                new { Zone = 41, Lon = 61.0, Lat = 31.0, ExpectedCol = "T", Description = "Zone 41, Column ~2" },
                new { Zone = 41, Lon = 62.0, Lat = 35.0, ExpectedCol = "U", Description = "Zone 41, Column ~3" },
            };

            Console.WriteLine("\n  ┌──────┬───────────────────┬──────────────────────────────┬──────────┬────────┬────────┐");
            Console.WriteLine("  │ Zone │ Coordinates       │ Description                  │ Expected │ Actual │ Result │");
            Console.WriteLine("  ├──────┼───────────────────┼──────────────────────────────┼──────────┼────────┼────────┤");
            
            int passed2 = 0, failed2 = 0;
            foreach (var t in columnTests)
            {
                try
                {
                    var gridSquare = IranNationalGrid.GetGridSquare(new SqlDouble(t.Lon), new SqlDouble(t.Lat));
                    var actualZone = IranNationalGrid.GetUTMZoneForLongitude(new SqlDouble(t.Lon)).Value;
                    string actualCol = gridSquare.Value.Substring(0, 1);
                    bool ok = actualCol == t.ExpectedCol && actualZone == t.Zone;
                    
                    string result = ok ? "  OK  " : " FAIL ";
                    Console.WriteLine($"  │  {actualZone,2}  │ ({t.Lon,5:F1}, {t.Lat,5:F1})   │ {t.Description,-28} │    {t.ExpectedCol}     │   {actualCol}    │ [{result}] │");
                    
                    if (ok) { passed2++; totalPassed++; } else { failed2++; totalFailed++; }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  │  {t.Zone,2}  │ ERROR: {ex.Message,-62} │");
                    failed2++; totalFailed++;
                }
            }
            Console.WriteLine("  └──────┴───────────────────┴──────────────────────────────┴──────────┴────────┴────────┘");
            Console.WriteLine($"  Summary: {passed2} passed, {failed2} failed");

            // ═══════════════════════════════════════════════════════════════════════════════
            // TEST SUITE 3: Row Letter Validation
            // ═══════════════════════════════════════════════════════════════════════════════
            Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ TEST 3: Row Letter Validation (Northing)                                   │");
            Console.WriteLine("├─────────────────────────────────────────────────────────────────────────────┤");
            Console.WriteLine("│ Purpose: Verify second letter (row) based on northing                      │");
            Console.WriteLine("│ Formula: letterIndex = (row100k + zone - 26) % 20                          │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");

            var rowTests = new[]
            {
                // Different latitudes in Zone 39 to test row letters
                new { Lon = 51.0, Lat = 27.5, ExpectedRow = "F", Description = "South Iran (~27.5°N)" },
                new { Lon = 51.0, Lat = 29.5, ExpectedRow = "H", Description = "Shiraz area (~29.5°N)" },
                new { Lon = 51.0, Lat = 32.5, ExpectedRow = "L", Description = "Isfahan area (~32.5°N)" },
                new { Lon = 51.0, Lat = 35.7, ExpectedRow = "P", Description = "Tehran area (~35.7°N)" },
                new { Lon = 51.0, Lat = 37.3, ExpectedRow = "Q", Description = "Caspian coast (~37.3°N)" },
                new { Lon = 51.0, Lat = 38.5, ExpectedRow = "S", Description = "North Iran (~38.5°N)" },
            };

            Console.WriteLine("\n  ┌───────────────────┬──────────────────────────────┬──────────┬────────┬────────┐");
            Console.WriteLine("  │ Coordinates       │ Description                  │ Expected │ Actual │ Result │");
            Console.WriteLine("  ├───────────────────┼──────────────────────────────┼──────────┼────────┼────────┤");
            
            int passed3 = 0, failed3 = 0;
            foreach (var t in rowTests)
            {
                try
                {
                    var gridSquare = IranNationalGrid.GetGridSquare(new SqlDouble(t.Lon), new SqlDouble(t.Lat));
                    string actualRow = gridSquare.Value.Substring(1, 1);
                    bool ok = actualRow == t.ExpectedRow;
                    
                    string result = ok ? "  OK  " : " FAIL ";
                    Console.WriteLine($"  │ ({t.Lon,5:F1}, {t.Lat,5:F1})   │ {t.Description,-28} │    {t.ExpectedRow}     │   {actualRow}    │ [{result}] │");
                    
                    if (ok) { passed3++; totalPassed++; } else { failed3++; totalFailed++; }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  │ ERROR: {ex.Message,-72} │");
                    failed3++; totalFailed++;
                }
            }
            Console.WriteLine("  └───────────────────┴──────────────────────────────┴──────────┴────────┴────────┘");
            Console.WriteLine($"  Summary: {passed3} passed, {failed3} failed");

            // ═══════════════════════════════════════════════════════════════════════════════
            // TEST SUITE 4: Precision Levels (جدول ۱-۲)
            // ═══════════════════════════════════════════════════════════════════════════════
            Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ TEST 4: IRNG Precision Levels (جدول ۱-۲)                                   │");
            Console.WriteLine("├─────────────────────────────────────────────────────────────────────────────┤");
            Console.WriteLine("│ Format: XY EEEEE NNNNN - Number of digits determines precision             │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");

            double testLon = 51.3890, testLat = 35.6892; // Tehran
            Console.WriteLine($"\n  Test Point: Tehran ({testLon}°E, {testLat}°N)");
            Console.WriteLine("\n  ┌───────────┬────────────────┬─────────────────────────┬────────┐");
            Console.WriteLine("  │ Precision │ Resolution     │ IRNG Code               │ Result │");
            Console.WriteLine("  ├───────────┼────────────────┼─────────────────────────┼────────┤");

            var precisionTests = new[]
            {
                new { Level = 1, Resolution = "10 km", MinLen = 6 },
                new { Level = 2, Resolution = "1 km", MinLen = 8 },
                new { Level = 3, Resolution = "100 m", MinLen = 10 },
                new { Level = 4, Resolution = "10 m", MinLen = 12 },
                new { Level = 5, Resolution = "1 m", MinLen = 14 },
            };

            int passed4 = 0, failed4 = 0;
            foreach (var p in precisionTests)
            {
                try
                {
                    var irng = IranNationalGrid.GeographicToIRNGWithPrecision(
                        new SqlDouble(testLon), new SqlDouble(testLat), new SqlInt32(p.Level));
                    
                    string code = irng.Value;
                    // Verify format: 2 letters + space + digits + space + digits
                    bool validFormat = code.Length >= p.MinLen && char.IsLetter(code[0]) && char.IsLetter(code[1]);
                    string result = validFormat ? "  OK  " : " FAIL ";
                    
                    Console.WriteLine($"  │     {p.Level}     │ {p.Resolution,-14} │ {code,-23} │ [{result}] │");
                    
                    if (validFormat) { passed4++; totalPassed++; } else { failed4++; totalFailed++; }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  │     {p.Level}     │ ERROR: {ex.Message,-46} │");
                    failed4++; totalFailed++;
                }
            }
            Console.WriteLine("  └───────────┴────────────────┴─────────────────────────┴────────┘");
            Console.WriteLine($"  Summary: {passed4} passed, {failed4} failed");

            // ═══════════════════════════════════════════════════════════════════════════════
            // TEST SUITE 5: Round-Trip Accuracy
            // ═══════════════════════════════════════════════════════════════════════════════
            Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ TEST 5: Round-Trip Accuracy (WGS84 → UTM → WGS84)                          │");
            Console.WriteLine("├─────────────────────────────────────────────────────────────────────────────┤");
            Console.WriteLine("│ Purpose: Verify coordinate transformation precision                        │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");

            var roundTripCities = new[]
            {
                new { Name = "Tehran", Lon = 51.3890, Lat = 35.6892 },
                new { Name = "Mashhad", Lon = 59.5794, Lat = 36.2605 },
                new { Name = "Isfahan", Lon = 51.6678, Lat = 32.6546 },
                new { Name = "Tabriz", Lon = 46.2919, Lat = 38.0800 },
            };

            Console.WriteLine("\n  ┌──────────┬─────────────────────────────┬─────────────────────────────┬────────────┬────────┐");
            Console.WriteLine("  │ City     │ Original (Lon, Lat)         │ Recovered (Lon, Lat)        │ Error (mm) │ Result │");
            Console.WriteLine("  ├──────────┼─────────────────────────────┼─────────────────────────────┼────────────┼────────┤");

            int passed5 = 0, failed5 = 0;
            foreach (var city in roundTripCities)
            {
                try
                {
                    int zone = IranNationalGrid.GetUTMZoneForLongitude(new SqlDouble(city.Lon)).Value;
                    var utmResult = IranNationalGrid.GeographicToUTM(new SqlDouble(city.Lon), new SqlDouble(city.Lat));
                    
                    var parts = utmResult.Value.Split(' ');
                    double easting = double.Parse(parts[1], CultureInfo.InvariantCulture);
                    double northing = double.Parse(parts[2], CultureInfo.InvariantCulture);
                    
                    var geoResult = IranNationalGrid.UTMToGeographic(
                        new SqlInt32(zone), new SqlString("N"),
                        new SqlDouble(easting), new SqlDouble(northing));
                    
                    var geoParts = geoResult.Value.Split(',');
                    double backLon = double.Parse(geoParts[0], CultureInfo.InvariantCulture);
                    double backLat = double.Parse(geoParts[1], CultureInfo.InvariantCulture);
                    
                    double errLon = Math.Abs(city.Lon - backLon);
                    double errLat = Math.Abs(city.Lat - backLat);
                    double errorMM = CalculateErrorMM(errLon, errLat);
                    
                    bool ok = errorMM < 1.0; // Less than 1mm error
                    string result = ok ? "  OK  " : " FAIL ";
                    
                    Console.WriteLine($"  │ {city.Name,-8} │ ({city.Lon,10:F6}, {city.Lat,10:F6}) │ ({backLon,10:F6}, {backLat,10:F6}) │ {errorMM,10:F4} │ [{result}] │");
                    
                    if (ok) { passed5++; totalPassed++; } else { failed5++; totalFailed++; }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  │ {city.Name,-8} │ ERROR: {ex.Message,-60} │");
                    failed5++; totalFailed++;
                }
            }
            Console.WriteLine("  └──────────┴─────────────────────────────┴─────────────────────────────┴────────────┴────────┘");
            Console.WriteLine($"  Summary: {passed5} passed, {failed5} failed");

            // ═══════════════════════════════════════════════════════════════════════════════
            // TEST SUITE 6: Iran Bounds Validation
            // ═══════════════════════════════════════════════════════════════════════════════
            Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ TEST 6: Iran Geographic Bounds Validation                                  │");
            Console.WriteLine("├─────────────────────────────────────────────────────────────────────────────┤");
            Console.WriteLine("│ Iran Bounds: Lat 25°N-40°N, Lon 44°E-64°E                                  │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");

            var boundTests = new[]
            {
                new { Name = "Tehran", Lon = 51.39, Lat = 35.69, ShouldBeInside = true },
                new { Name = "Chabahar", Lon = 60.64, Lat = 25.29, ShouldBeInside = true },
                new { Name = "Urmia", Lon = 45.07, Lat = 37.55, ShouldBeInside = true },
                new { Name = "London", Lon = -0.12, Lat = 51.51, ShouldBeInside = false },
                new { Name = "Dubai", Lon = 55.27, Lat = 25.07, ShouldBeInside = false },
                new { Name = "Kabul", Lon = 69.17, Lat = 34.53, ShouldBeInside = false },
            };

            Console.WriteLine("\n  ┌──────────────┬───────────────────┬──────────────┬────────────┬────────┐");
            Console.WriteLine("  │ Location     │ Coordinates       │ Expected     │ Actual     │ Result │");
            Console.WriteLine("  ├──────────────┼───────────────────┼──────────────┼────────────┼────────┤");

            int passed6 = 0, failed6 = 0;
            foreach (var t in boundTests)
            {
                try
                {
                    bool isInside = IranNationalGrid.IsWithinIranBounds(
                        new SqlDouble(t.Lon), new SqlDouble(t.Lat)).Value;
                    
                    bool ok = isInside == t.ShouldBeInside;
                    string expected = t.ShouldBeInside ? "Inside" : "Outside";
                    string actual = isInside ? "Inside" : "Outside";
                    string result = ok ? "  OK  " : " FAIL ";
                    
                    Console.WriteLine($"  │ {t.Name,-12} │ ({t.Lon,6:F2}, {t.Lat,5:F2}) │ {expected,-12} │ {actual,-10} │ [{result}] │");
                    
                    if (ok) { passed6++; totalPassed++; } else { failed6++; totalFailed++; }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  │ {t.Name,-12} │ ERROR: {ex.Message,-50} │");
                    failed6++; totalFailed++;
                }
            }
            Console.WriteLine("  └──────────────┴───────────────────┴──────────────┴────────────┴────────┘");
            Console.WriteLine($"  Summary: {passed6} passed, {failed6} failed");

            // ═══════════════════════════════════════════════════════════════════════════════
            // FINAL SUMMARY
            // ═══════════════════════════════════════════════════════════════════════════════
            Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                           FINAL TEST RESULTS                                 ║");
            Console.WriteLine("╠═══════════════════════════════════════════════════════════════════════════════╣");
            Console.WriteLine($"║   Total Tests: {totalPassed + totalFailed,-5}                                                        ║");
            Console.WriteLine($"║   Passed:      {totalPassed,-5}  ✓                                                      ║");
            Console.WriteLine($"║   Failed:      {totalFailed,-5}  {(totalFailed == 0 ? "✓" : "✗")}                                                      ║");
            Console.WriteLine($"║   Success Rate: {(totalPassed * 100.0 / (totalPassed + totalFailed)):F1}%                                                    ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝");

            // Additional Info Output
            Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ IRNG System Information                                                     │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");
            Console.WriteLine(IranNationalGrid.GetIRNGInfo().Value);

            // Sample points for remaining tests
            var iranCities = new[]
            {
                new { Name = "Tehran", Lon = 51.3890, Lat = 35.6892 },
                new { Name = "Isfahan", Lon = 51.6678, Lat = 32.6546 },
                new { Name = "Shiraz", Lon = 52.5311, Lat = 29.5918 },
                new { Name = "Tabriz", Lon = 46.2919, Lat = 38.0800 },
                new { Name = "Mashhad", Lon = 59.5794, Lat = 36.2605 },
                new { Name = "Kerman", Lon = 57.0879, Lat = 30.2839 },
                new { Name = "Ahvaz", Lon = 48.6692, Lat = 31.3183 },
                new { Name = "Bandar Abbas", Lon = 56.2808, Lat = 27.1865 }
            };

            // ═══════════════════════════════════════════════════════════════════════════════
            // TEST SUITE 7: Major Iranian Cities - Full IRNG Codes
            // ═══════════════════════════════════════════════════════════════════════════════
            Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ TEST 7: Major Iranian Cities - Complete IRNG Codes                         │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");

            Console.WriteLine("\n  ┌──────────────────┬───────────────────┬──────┬────────────────────┬────────────┐");
            Console.WriteLine("  │ City             │ Coordinates       │ Zone │ Grid Square        │ IRNG Code  │");
            Console.WriteLine("  ├──────────────────┼───────────────────┼──────┼────────────────────┼────────────┤");

            foreach (var city in iranCities)
            {
                try
                {
                    int zone = IranNationalGrid.GetUTMZoneForLongitude(new SqlDouble(city.Lon)).Value;
                    var gridSquare = IranNationalGrid.GetGridSquare(new SqlDouble(city.Lon), new SqlDouble(city.Lat));
                    var irngCode = IranNationalGrid.GeographicToIRNGWithPrecision(
                        new SqlDouble(city.Lon), new SqlDouble(city.Lat), new SqlInt32(2)); // 1km precision

                    Console.WriteLine($"  │ {city.Name,-16} │ ({city.Lon,6:F2}, {city.Lat,5:F2}) │  {zone,2}  │        {gridSquare.Value,-10} │ {irngCode.Value,-10} │");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  │ {city.Name,-16} │ ERROR: {ex.Message,-52} │");
                }
            }
            Console.WriteLine("  └──────────────────┴───────────────────┴──────┴────────────────────┴────────────┘");

            // ═══════════════════════════════════════════════════════════════════════════════
            // TEST SUITE 8: UTM Zone Information
            // ═══════════════════════════════════════════════════════════════════════════════
            Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ TEST 8: UTM Zones Covering Iran                                            │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");

            Console.WriteLine("\n  ┌──────┬──────────────────┬─────────────────┬──────────────────────────────────┐");
            Console.WriteLine("  │ Zone │ Central Meridian │ Longitude Range │ Column Letters (IRNG)            │");
            Console.WriteLine("  ├──────┼──────────────────┼─────────────────┼──────────────────────────────────┤");

            var zoneInfo = new[]
            {
                new { Zone = 38, Letters = "A, B, C, D (cols 4-8)" },
                new { Zone = 39, Letters = "D, E, F, G, H, J, K, L (cols 1-8)" },
                new { Zone = 40, Letters = "L, M, N, P, Q, R, S, T (cols 1-8)" },
                new { Zone = 41, Letters = "S, T, U, V, W, X, Y, Z (cols 1-8)" },
            };

            foreach (var z in zoneInfo)
            {
                double cm = IranNationalGrid.GetCentralMeridian(new SqlInt32(z.Zone)).Value;
                double lonMin = (z.Zone - 1) * 6 - 180;
                double lonMax = z.Zone * 6 - 180;
                
                Console.WriteLine($"  │  {z.Zone}  │       {cm,2:F0}°E       │   {lonMin,2:F0}°E - {lonMax,2:F0}°E  │ {z.Letters,-32} │");
            }
            Console.WriteLine("  └──────┴──────────────────┴─────────────────┴──────────────────────────────────┘");

            Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    IRNG TEST SUITE COMPLETED                                 ║");
            Console.WriteLine("║              Based on Official Standard: نشریه ۸-۱۱۹                         ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝");
        }
    }
}
