using System;
using System.Data.SqlTypes;
using System.Globalization;
using Microsoft.SqlServer.Server;
using Microsoft.SqlServer.Types;

/// <summary>
/// Iran National Grid (IRNG) - Based on UTM Coordinate System
/// 
/// According to Official Standard: نشریه شماره ۸-۱۱۹ سازمان نقشه‌برداری کشور
/// 
/// Key Facts:
/// - IRNG is a GRID NAMING SYSTEM built on top of UTM coordinates
/// - It is NOT a separate map projection
/// - Uses WGS84 ellipsoid
/// - Iran spans UTM zones 38, 39, 40, 41 (and partially 42)
/// - 100km grid squares are identified by two letters (e.g., HN, GP, FL)
/// - Format: XY EEEEE NNNNN (e.g., HN 12345 12345)
/// 
/// UTM Zone boundaries for Iran:
/// - Zone 38: 42°E - 48°E
/// - Zone 39: 48°E - 54°E (Central Meridian 51°E)
/// - Zone 40: 54°E - 60°E (Central Meridian 57°E)
/// - Zone 41: 60°E - 66°E (Central Meridian 63°E)
/// 
/// Iran Geographic Bounds:
/// - Latitude: 25°N to 40°N
/// - Longitude: 44°E to 64°E
/// </summary>
public static class IranNationalGrid
{
    #region Constants

    /// <summary>WGS84 SRID</summary>
    public const int WGS84_SRID = 4326;

    // WGS84 Ellipsoid Parameters
    private const double SEMI_MAJOR_AXIS = 6378137.0;           // a (meters)
    private const double FLATTENING = 1.0 / 298.257223563;      // f (WGS84)
    private const double ECCENTRICITY_SQ = 0.00669437999014;    // e² = 2f - f²

    // UTM Parameters
    private const double UTM_SCALE_FACTOR = 0.9996;             // k0
    private const double UTM_FALSE_EASTING = 500000.0;          // meters
    private const double UTM_FALSE_NORTHING_NORTH = 0.0;        // Northern hemisphere

    // Iran Geographic Bounds
    private const double IRAN_MIN_LAT = 25.0;
    private const double IRAN_MAX_LAT = 40.0;
    private const double IRAN_MIN_LON = 44.0;
    private const double IRAN_MAX_LON = 64.0;

    // IRNG Letter System (excluding I and O to avoid confusion with 1 and 0)
    // Letters used: A B C D E F G H J K L M N P Q R S T U V W X Y Z
    private static readonly char[] IRNG_LETTERS = {
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K',
        'L', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V',
        'W', 'X', 'Y', 'Z'
    };

    // IRNG Grid Letter Table based on official document (نشریه ۸-۱۱۹)
    // First letter is based on column (easting) within UTM zone
    // Second letter is based on row (northing)
    // The pattern repeats every 2,000,000 meters in northing

    #endregion

    #region SQL Server Functions - Geographic to UTM

    /// <summary>
    /// Convert geographic coordinates (WGS84) to UTM
    /// Returns: "Zone Easting Northing" (e.g., "39N 528418.123 3951576.456")
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString GeographicToUTM(SqlDouble longitude, SqlDouble latitude)
    {
        if (longitude.IsNull || latitude.IsNull)
            return SqlString.Null;

        double lon = longitude.Value;
        double lat = latitude.Value;

        int zone = GetUTMZone(lon);
        char hemisphere = lat >= 0 ? 'N' : 'S';

        GeographicToUTMInternal(lon, lat, zone, out double easting, out double northing);

        return new SqlString($"{zone}{hemisphere} {easting.ToString("F3", CultureInfo.InvariantCulture)} {northing.ToString("F3", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Convert UTM coordinates to geographic (WGS84)
    /// Returns: "Longitude,Latitude"
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString UTMToGeographic(SqlInt32 zone, SqlString hemisphere, SqlDouble easting, SqlDouble northing)
    {
        if (zone.IsNull || hemisphere.IsNull || easting.IsNull || northing.IsNull)
            return SqlString.Null;

        int z = zone.Value;
        bool isNorthern = hemisphere.Value.ToUpper().StartsWith("N");
        double e = easting.Value;
        double n = northing.Value;

        UTMToGeographicInternal(z, isNorthern, e, n, out double lon, out double lat);

        return new SqlString($"{lon.ToString("F10", CultureInfo.InvariantCulture)},{lat.ToString("F10", CultureInfo.InvariantCulture)}");
    }

    #endregion

    #region SQL Server Functions - IRNG

    /// <summary>
    /// Convert geographic coordinates (WGS84) to IRNG code
    /// Returns IRNG format: "XY EEEEE NNNNN" (e.g., "HN 28418 51576")
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString GeographicToIRNG(SqlDouble longitude, SqlDouble latitude)
    {
        if (longitude.IsNull || latitude.IsNull)
            return SqlString.Null;

        double lon = longitude.Value;
        double lat = latitude.Value;

        // Get UTM zone and coordinates
        int zone = GetUTMZone(lon);
        GeographicToUTMInternal(lon, lat, zone, out double easting, out double northing);

        // Get IRNG code
        string irngCode = UTMToIRNGCode(zone, easting, northing);

        return new SqlString(irngCode);
    }

    /// <summary>
    /// Convert geographic coordinates to IRNG with specified precision
    /// Precision: number of digits for easting and northing (1-5)
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString GeographicToIRNGWithPrecision(SqlDouble longitude, SqlDouble latitude, SqlInt32 precision)
    {
        if (longitude.IsNull || latitude.IsNull || precision.IsNull)
            return SqlString.Null;

        double lon = longitude.Value;
        double lat = latitude.Value;
        int prec = Math.Max(1, Math.Min(5, precision.Value));

        int zone = GetUTMZone(lon);
        GeographicToUTMInternal(lon, lat, zone, out double easting, out double northing);

        string irngCode = UTMToIRNGCodeWithPrecision(zone, easting, northing, prec);

        return new SqlString(irngCode);
    }

    /// <summary>
    /// Convert UTM coordinates to IRNG code
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString UTMToIRNG(SqlInt32 zone, SqlDouble easting, SqlDouble northing)
    {
        if (zone.IsNull || easting.IsNull || northing.IsNull)
            return SqlString.Null;

        int z = zone.Value;
        double e = easting.Value;
        double n = northing.Value;

        string irngCode = UTMToIRNGCode(z, e, n);

        return new SqlString(irngCode);
    }

    /// <summary>
    /// Parse IRNG code and return UTM coordinates
    /// Input: "HN 28418 51576" or "HN28418 51576" or "HN 2841851576"
    /// Returns: "Zone Easting Northing"
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString IRNGToUTM(SqlString irngCode)
    {
        if (irngCode.IsNull)
            return SqlString.Null;

        string code = irngCode.Value.Trim().ToUpper();

        if (!ParseIRNGCode(code, out int zone, out double easting, out double northing))
            return SqlString.Null;

        return new SqlString($"{zone}N {easting.ToString("F0", CultureInfo.InvariantCulture)} {northing.ToString("F0", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Parse IRNG code and return geographic coordinates (WGS84)
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString IRNGToGeographic(SqlString irngCode)
    {
        if (irngCode.IsNull)
            return SqlString.Null;

        string code = irngCode.Value.Trim().ToUpper();

        if (!ParseIRNGCode(code, out int zone, out double easting, out double northing))
            return SqlString.Null;

        UTMToGeographicInternal(zone, true, easting, northing, out double lon, out double lat);

        return new SqlString($"{lon.ToString("F10", CultureInfo.InvariantCulture)},{lat.ToString("F10", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Get the 100km grid square code (two letters) for given coordinates
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString GetGridSquare(SqlDouble longitude, SqlDouble latitude)
    {
        if (longitude.IsNull || latitude.IsNull)
            return SqlString.Null;

        double lon = longitude.Value;
        double lat = latitude.Value;

        int zone = GetUTMZone(lon);
        GeographicToUTMInternal(lon, lat, zone, out double easting, out double northing);

        string gridSquare = GetGridSquareLetters(zone, easting, northing);

        return new SqlString(gridSquare);
    }

    /// <summary>
    /// Check if coordinates are within Iran bounds
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlBoolean IsWithinIranBounds(SqlDouble longitude, SqlDouble latitude)
    {
        if (longitude.IsNull || latitude.IsNull)
            return SqlBoolean.Null;

        double lon = longitude.Value;
        double lat = latitude.Value;

        bool isValid = lat >= IRAN_MIN_LAT && lat <= IRAN_MAX_LAT &&
                      lon >= IRAN_MIN_LON && lon <= IRAN_MAX_LON;

        return new SqlBoolean(isValid);
    }

    /// <summary>
    /// Get UTM zone for a longitude
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlInt32 GetUTMZoneForLongitude(SqlDouble longitude)
    {
        if (longitude.IsNull)
            return SqlInt32.Null;

        return new SqlInt32(GetUTMZone(longitude.Value));
    }

    /// <summary>
    /// Get the central meridian for a UTM zone
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlDouble GetCentralMeridian(SqlInt32 zone)
    {
        if (zone.IsNull)
            return SqlDouble.Null;

        double cm = (zone.Value - 1) * 6 - 180 + 3;
        return new SqlDouble(cm);
    }

    #endregion

    #region SQL Server Functions - Geometry Transformation

    /// <summary>
    /// Transform WGS84 geometry to UTM for the appropriate zone
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlGeometry TransformToUTM(SqlGeometry wgs84Geometry)
    {
        if (wgs84Geometry == null || wgs84Geometry.IsNull)
            return SqlGeometry.Null;

        if (wgs84Geometry.STSrid.Value != WGS84_SRID)
            throw new ArgumentException($"Input geometry must have SRID {WGS84_SRID} (WGS84)");

        // Get centroid to determine UTM zone
        var centroid = wgs84Geometry.STCentroid();
        if (centroid.IsNull)
            return SqlGeometry.Null;

        double lon = centroid.STX.Value;
        int zone = GetUTMZone(lon);
        int utmSrid = 32600 + zone; // Northern hemisphere UTM SRID

        return SpatialReprojection.TransformGeometry(wgs84Geometry, utmSrid);
    }

    /// <summary>
    /// Transform UTM geometry back to WGS84
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlGeometry TransformToWGS84(SqlGeometry utmGeometry)
    {
        if (utmGeometry == null || utmGeometry.IsNull)
            return SqlGeometry.Null;

        return SpatialReprojection.TransformGeometry(utmGeometry, WGS84_SRID);
    }

    #endregion

    #region Core UTM Transformation Methods

    /// <summary>
    /// Get UTM zone number for a given longitude
    /// </summary>
    internal static int GetUTMZone(double longitude)
    {
        return (int)Math.Floor((longitude + 180) / 6) + 1;
    }

    /// <summary>
    /// Convert geographic coordinates to UTM (Forward Projection)
    /// Uses Transverse Mercator projection
    /// </summary>
    internal static void GeographicToUTMInternal(double longitude, double latitude, int zone, out double easting, out double northing)
    {
        double lat = DegreesToRadians(latitude);
        double lon = DegreesToRadians(longitude);

        // Central meridian for the zone
        double lon0 = DegreesToRadians((zone - 1) * 6 - 180 + 3);

        // Calculate auxiliary values
        double e2 = ECCENTRICITY_SQ;
        double e4 = e2 * e2;
        double e6 = e4 * e2;
        double ep2 = e2 / (1 - e2); // e'^2

        double N = SEMI_MAJOR_AXIS / Math.Sqrt(1 - e2 * Math.Sin(lat) * Math.Sin(lat));
        double T = Math.Tan(lat) * Math.Tan(lat);
        double C = ep2 * Math.Cos(lat) * Math.Cos(lat);
        double A = Math.Cos(lat) * (lon - lon0);

        // Meridional arc
        double M = SEMI_MAJOR_AXIS * (
            (1 - e2 / 4 - 3 * e4 / 64 - 5 * e6 / 256) * lat
            - (3 * e2 / 8 + 3 * e4 / 32 + 45 * e6 / 1024) * Math.Sin(2 * lat)
            + (15 * e4 / 256 + 45 * e6 / 1024) * Math.Sin(4 * lat)
            - (35 * e6 / 3072) * Math.Sin(6 * lat)
        );

        // Calculate easting and northing
        double A2 = A * A;
        double A3 = A2 * A;
        double A4 = A3 * A;
        double A5 = A4 * A;
        double A6 = A5 * A;
        double T2 = T * T;

        easting = UTM_SCALE_FACTOR * N * (
            A
            + (1 - T + C) * A3 / 6
            + (5 - 18 * T + T2 + 72 * C - 58 * ep2) * A5 / 120
        ) + UTM_FALSE_EASTING;

        northing = UTM_SCALE_FACTOR * (
            M
            + N * Math.Tan(lat) * (
                A2 / 2
                + (5 - T + 9 * C + 4 * C * C) * A4 / 24
                + (61 - 58 * T + T2 + 600 * C - 330 * ep2) * A6 / 720
            )
        ) + UTM_FALSE_NORTHING_NORTH;
    }

    /// <summary>
    /// Convert UTM coordinates to geographic (Inverse Projection)
    /// </summary>
    internal static void UTMToGeographicInternal(int zone, bool isNorthernHemisphere, double easting, double northing, out double longitude, out double latitude)
    {
        // Central meridian for the zone
        double lon0 = DegreesToRadians((zone - 1) * 6 - 180 + 3);

        // Remove false easting and northing
        double x = easting - UTM_FALSE_EASTING;
        double y = isNorthernHemisphere ? northing : northing - 10000000.0;

        // Calculate auxiliary values
        double e2 = ECCENTRICITY_SQ;
        double e4 = e2 * e2;
        double e6 = e4 * e2;
        double ep2 = e2 / (1 - e2);
        double e1 = (1 - Math.Sqrt(1 - e2)) / (1 + Math.Sqrt(1 - e2));

        double M = y / UTM_SCALE_FACTOR;
        double mu = M / (SEMI_MAJOR_AXIS * (1 - e2 / 4 - 3 * e4 / 64 - 5 * e6 / 256));

        // Footprint latitude
        double phi1 = mu
            + (3 * e1 / 2 - 27 * e1 * e1 * e1 / 32) * Math.Sin(2 * mu)
            + (21 * e1 * e1 / 16 - 55 * e1 * e1 * e1 * e1 / 32) * Math.Sin(4 * mu)
            + (151 * e1 * e1 * e1 / 96) * Math.Sin(6 * mu)
            + (1097 * e1 * e1 * e1 * e1 / 512) * Math.Sin(8 * mu);

        double sinPhi1 = Math.Sin(phi1);
        double cosPhi1 = Math.Cos(phi1);
        double tanPhi1 = Math.Tan(phi1);

        double N1 = SEMI_MAJOR_AXIS / Math.Sqrt(1 - e2 * sinPhi1 * sinPhi1);
        double T1 = tanPhi1 * tanPhi1;
        double C1 = ep2 * cosPhi1 * cosPhi1;
        double R1 = SEMI_MAJOR_AXIS * (1 - e2) / Math.Pow(1 - e2 * sinPhi1 * sinPhi1, 1.5);
        double D = x / (N1 * UTM_SCALE_FACTOR);

        double D2 = D * D;
        double D3 = D2 * D;
        double D4 = D3 * D;
        double D5 = D4 * D;
        double D6 = D5 * D;
        double T12 = T1 * T1;

        double lat = phi1
            - (N1 * tanPhi1 / R1) * (
                D2 / 2
                - (5 + 3 * T1 + 10 * C1 - 4 * C1 * C1 - 9 * ep2) * D4 / 24
                + (61 + 90 * T1 + 298 * C1 + 45 * T12 - 252 * ep2 - 3 * C1 * C1) * D6 / 720
            );

        double lon = lon0 + (
            D
            - (1 + 2 * T1 + C1) * D3 / 6
            + (5 - 2 * C1 + 28 * T1 - 3 * C1 * C1 + 8 * ep2 + 24 * T12) * D5 / 120
        ) / cosPhi1;

        latitude = RadiansToDegrees(lat);
        longitude = RadiansToDegrees(lon);
    }

    #endregion

    #region IRNG Grid Letter Methods

    /// <summary>
    /// Column letter offsets for each UTM zone covering Iran
    /// Based on official IRNG standard (نشریه ۸-۱۱۹, شکل ۳-۳)
    /// 
    /// These offsets are derived from sample data in the official document:
    /// - Zone 38: B, C, D... (western Iran - Azerbaijan, Kermanshah)
    /// - Zone 39: E, F, G, H... (central Iran - Tehran, Isfahan, Gilan)
    /// - Zone 40: N, P, Q... (eastern-central Iran)
    /// - Zone 41: S, T, U, V... (eastern Iran - Khorasan)
    /// 
    /// Formula: letterIndex = (col100k + offset) % 24
    /// </summary>
    private static readonly int[] ZONE_COLUMN_OFFSETS = new int[64];
    
    static IranNationalGrid()
    {
        // Initialize column offsets for Iran's UTM zones based on official document
        // Zone 38: Column 5 → B (index 1), so offset = 1 - 5 = -4
        // Zone 39: Column 3 → F (index 5), verified with Rasht sample, so offset = 5 - 3 = +2
        // Zone 40: Column 1 → N (index 12), so offset = 12 - 1 = +11
        // Zone 41: Column 1 → S (index 16), so offset = 16 - 1 = +15
        
        ZONE_COLUMN_OFFSETS[38] = -4;
        ZONE_COLUMN_OFFSETS[39] = 2;
        ZONE_COLUMN_OFFSETS[40] = 11;
        ZONE_COLUMN_OFFSETS[41] = 15;
        
        // Also support adjacent zones that might be used
        ZONE_COLUMN_OFFSETS[37] = -10;  // Estimated for continuity
        ZONE_COLUMN_OFFSETS[42] = 19;   // Estimated for continuity
    }

    /// <summary>
    /// Get the two-letter grid square code based on UTM coordinates
    /// According to official IRNG standard (نشریه ۸-۱۱۹)
    /// 
    /// Verified against official samples:
    /// - رشت (Rasht): FQ 7426 at ~49.6°E, 37.28°N → Zone 39, Column 3 = F, Row 41 = Q ✓
    /// - لاهیجان (Lahijan): GQ 1118 at ~50.0°E, 37.2°N → Zone 39, Column 4 = G ✓
    /// - کرمانشاه (Kermanshah): CL 3278 at ~47.1°E, 34.3°N → Zone 38, Column 6 = C ✓
    /// </summary>
    internal static string GetGridSquareLetters(int zone, double easting, double northing)
    {
        // Get column number (1-8 based on 100km divisions)
        int col100k = (int)Math.Floor(easting / 100000);
        
        // Get first letter (column letter) based on zone-specific offset
        int colLetterIndex = GetColumnLetterIndex(zone, col100k);
        char firstLetter = IRNG_LETTERS[colLetterIndex];

        // Get row number
        int row100k = (int)Math.Floor(northing / 100000);
        
        // Get second letter (row letter)
        int rowLetterIndex = GetRowLetterIndex(zone, row100k);
        char secondLetter = IRNG_LETTERS[rowLetterIndex];

        return $"{firstLetter}{secondLetter}";
    }

    /// <summary>
    /// Get column letter index based on UTM zone and column number
    /// According to official IRNG standard (نشریه ۸-۱۱۹, شکل ۳-۳)
    /// 
    /// Unlike MGRS which uses 3 repeating sets of 8 letters,
    /// IRNG uses zone-specific offsets that create a continuous
    /// letter progression across Iran from west to east.
    /// </summary>
    private static int GetColumnLetterIndex(int zone, int col100k)
    {
        // Get zone-specific offset
        int offset = 0;
        if (zone >= 0 && zone < ZONE_COLUMN_OFFSETS.Length)
        {
            offset = ZONE_COLUMN_OFFSETS[zone];
        }
        
        // Calculate letter index with proper wrapping for negative values
        int index = col100k + offset;
        
        // Handle negative indices by wrapping
        while (index < 0) index += IRNG_LETTERS.Length;
        
        return index % IRNG_LETTERS.Length;
    }

    /// <summary>
    /// Get row letter index based on zone and northing
    /// According to official IRNG standard (نشریه ۸-۱۱۹)
    /// 
    /// The row letter pattern is zone-dependent with offset = (zone - 26)
    /// This was verified against official samples:
    /// - Zone 39, Row 41 → Q (index 14): (41 + 13) % 20 = 14 ✓
    /// - Zone 38, Row 38 → L (index 10): (38 + 12) % 20 = 10 ✓
    /// 
    /// Formula: letterIndex = (row100k + zone - 26) % 20
    /// </summary>
    private static int GetRowLetterIndex(int zone, int row100k)
    {
        // Row offset is zone-dependent: offset = zone - 26
        // This creates proper letter assignments for Iran's northing range
        int rowOffset = zone - 26;
        
        // Calculate row letter index (cycles every 20 letters = 2,000,000 meters)
        int index = (row100k + rowOffset) % 20;
        
        // Handle negative indices
        while (index < 0) index += 20;
        
        return index;
    }

    /// <summary>
    /// Convert UTM coordinates to IRNG code with full precision (5 digits)
    /// </summary>
    internal static string UTMToIRNGCode(int zone, double easting, double northing)
    {
        return UTMToIRNGCodeWithPrecision(zone, easting, northing, 5);
    }

    /// <summary>
    /// Convert UTM coordinates to IRNG code with specified precision
    /// </summary>
    internal static string UTMToIRNGCodeWithPrecision(int zone, double easting, double northing, int precision)
    {
        string gridSquare = GetGridSquareLetters(zone, easting, northing);

        // Get the numeric part within the 100km square
        int eastingInSquare = (int)(easting % 100000);
        int northingInSquare = (int)(northing % 100000);

        // Format based on precision
        // precision 1 = 10km, 2 = 1km, 3 = 100m, 4 = 10m, 5 = 1m
        int divisor = (int)Math.Pow(10, 5 - precision);
        int eastDigits = eastingInSquare / divisor;
        int northDigits = northingInSquare / divisor;

        string format = new string('0', precision);

        return $"{gridSquare} {eastDigits.ToString(format)} {northDigits.ToString(format)}";
    }

    /// <summary>
    /// Parse IRNG code and extract UTM coordinates
    /// </summary>
    internal static bool ParseIRNGCode(string code, out int zone, out double easting, out double northing)
    {
        zone = 0;
        easting = 0;
        northing = 0;

        if (string.IsNullOrWhiteSpace(code) || code.Length < 4)
            return false;

        // Remove spaces and extract parts
        code = code.Replace(" ", "");

        if (code.Length < 4)
            return false;

        // First two characters are the grid square letters
        char firstLetter = code[0];
        char secondLetter = code[1];

        if (!char.IsLetter(firstLetter) || !char.IsLetter(secondLetter))
            return false;

        // Remaining characters are the numeric coordinates
        string numericPart = code.Substring(2);

        if (numericPart.Length < 2 || numericPart.Length % 2 != 0)
            return false;

        int halfLen = numericPart.Length / 2;
        string eastPart = numericPart.Substring(0, halfLen);
        string northPart = numericPart.Substring(halfLen);

        if (!int.TryParse(eastPart, out int eastNum) || !int.TryParse(northPart, out int northNum))
            return false;

        // Scale up based on precision
        int multiplier = (int)Math.Pow(10, 5 - halfLen);
        int eastingInSquare = eastNum * multiplier;
        int northingInSquare = northNum * multiplier;

        // Determine zone and base coordinates from grid letters
        // This is an approximation - for Iran, most coordinates are in zones 39-40
        if (!GetZoneAndBaseFromLetters(firstLetter, secondLetter, out zone, out int baseEasting, out int baseNorthing))
            return false;

        easting = baseEasting * 100000 + eastingInSquare;
        northing = baseNorthing * 100000 + northingInSquare;

        return true;
    }

    /// <summary>
    /// Determine UTM zone and base 100km coordinates from grid letters
    /// Based on official IRNG patterns for Iran's zones (38-41)
    /// According to نشریه ۸-۱۱۹
    /// 
    /// Column letter patterns from official document (شکل ۳-۵):
    /// - Zone 38: A, B, C, D (columns 5-8, western Iran)
    /// - Zone 39: D, E, F, G, H, J, K, L (columns 1-8, central Iran)
    /// - Zone 40: L, M, N, P, Q, R, S, T (columns 1-8)
    /// - Zone 41: S, T, U, V, W, X, Y, Z (columns 1-8, eastern Iran)
    /// </summary>
    private static bool GetZoneAndBaseFromLetters(char firstLetter, char secondLetter, out int zone, out int baseEasting, out int baseNorthing)
    {
        zone = 39; // Default to zone 39 (central Iran)
        baseEasting = 5; // Default to column 5 (center of zone)
        baseNorthing = 35; // Default to row 35 (middle of Iran)

        int firstIndex = Array.IndexOf(IRNG_LETTERS, firstLetter);
        int secondIndex = Array.IndexOf(IRNG_LETTERS, secondLetter);

        if (firstIndex < 0 || secondIndex < 0)
            return false;

        // Determine zone and column from first letter (column letter)
        // Based on IRNG patterns from official document:
        // Zone 38 offset = -4: columns 5-8 → letters A-D (indices 0-3)
        // Zone 39 offset = +2: columns 1-8 → letters D-L (indices 3-10)
        // Zone 40 offset = +11: columns 1-8 → letters L-T (indices 10-17)
        // Zone 41 offset = +15: columns 1-8 → letters S-Z (indices 16-23)
        
        // Work backwards: letterIndex = col + offset, so col = letterIndex - offset
        
        if (firstIndex >= 16) // S-Z (indices 16-23)
        {
            // Zone 41: offset = 15, col = firstIndex - 15
            zone = 41;
            baseEasting = firstIndex - 15;
            if (baseEasting < 1) baseEasting = 1;
            if (baseEasting > 8) baseEasting = 8;
        }
        else if (firstIndex >= 10 && firstIndex < 16) // L-R (indices 10-15)
        {
            // Could be Zone 39 (L at col 8) or Zone 40 (L at col 1)
            // Use row letter to help distinguish
            // For now, prefer Zone 40 for these letters
            zone = 40;
            baseEasting = firstIndex - 11;
            if (baseEasting < 1)
            {
                // If column would be 0 or negative, might be Zone 39
                zone = 39;
                baseEasting = firstIndex - 2;
            }
            if (baseEasting < 1) baseEasting = 1;
            if (baseEasting > 8) baseEasting = 8;
        }
        else if (firstIndex >= 3 && firstIndex < 10) // D-K (indices 3-9)
        {
            // Zone 39: offset = 2, col = firstIndex - 2
            zone = 39;
            baseEasting = firstIndex - 2;
            if (baseEasting < 1) baseEasting = 1;
            if (baseEasting > 8) baseEasting = 8;
        }
        else // A-C (indices 0-2)
        {
            // Zone 38: offset = -4, col = firstIndex + 4
            zone = 38;
            baseEasting = firstIndex + 4;
            // In Zone 38, only columns 4-8 are typically used in Iran
            if (baseEasting < 1) baseEasting = 1;
            if (baseEasting > 8) baseEasting = 8;
        }

        // Calculate base northing from second letter (row letter)
        // Formula: letterIndex = (row100k + zone - 26) % 20
        // Reverse: row100k = (letterIndex - (zone - 26) + 20) % 20
        // But we need the actual row in Iran's range (27-45)
        
        int rowOffset = zone - 26;
        int targetRowMod = (secondIndex - rowOffset + 40) % 20; // +40 to ensure positive

        // For Iran, northing/100000 ranges from about 27 to 45
        // Find the value in this range that matches the pattern
        baseNorthing = 0;
        for (int row = 27; row <= 46; row++)
        {
            if (row % 20 == targetRowMod)
            {
                baseNorthing = row;
                break;
            }
        }

        // If not found in first pass, try extended search
        if (baseNorthing == 0)
        {
            // Calculate directly
            baseNorthing = targetRowMod;
            while (baseNorthing < 27) baseNorthing += 20;
            if (baseNorthing > 46) baseNorthing = 35; // Fallback to middle
        }

        return true;
    }

    #endregion

    #region IRNG Direct Coordinate Transformation Methods

    /// <summary>
    /// Convert geographic coordinates (WGS84) to IRNG UTM coordinates
    /// For use in SpatialReprojection with SRID 102030
    /// </summary>
    /// <param name="longitude">Longitude in degrees (WGS84)</param>
    /// <param name="latitude">Latitude in degrees (WGS84)</param>
    /// <param name="easting">Output easting in meters (UTM)</param>
    /// <param name="northing">Output northing in meters (UTM)</param>
    internal static void GeographicToIRNGInternal(double longitude, double latitude, out double easting, out double northing)
    {
        // Determine appropriate UTM zone for the longitude
        int zone = GetUTMZone(longitude);

        // Convert to UTM coordinates
        GeographicToUTMInternal(longitude, latitude, zone, out easting, out northing);
    }

    /// <summary>
    /// Convert IRNG UTM coordinates to geographic coordinates (WGS84)
    /// For use in SpatialReprojection with SRID 102030
    /// </summary>
    /// <param name="easting">Easting in meters (UTM)</param>
    /// <param name="northing">Northing in meters (UTM)</param>
    /// <param name="longitude">Output longitude in degrees (WGS84)</param>
    /// <param name="latitude">Output latitude in degrees (WGS84)</param>
    internal static void IRNGToGeographicInternal(double easting, double northing, out double longitude, out double latitude)
    {
        // For Iran, we need to determine the UTM zone from the easting/northing
        // Most of Iran is in zones 38-41. We'll use zone 39 as default (central Iran)
        // A more accurate approach would require additional context or zone information

        // Estimate zone from easting - this is an approximation
        // Zone 39 has central meridian at 51°E, which is common for Iran
        int zone = EstimateZoneFromCoordinates(easting, northing);

        // Convert from UTM to geographic (Iran is always in northern hemisphere)
        UTMToGeographicInternal(zone, true, easting, northing, out longitude, out latitude);
    }

    /// <summary>
    /// Estimate UTM zone from easting/northing coordinates for Iran
    /// This attempts to determine the correct zone by checking coordinate validity
    /// </summary>
    private static int EstimateZoneFromCoordinates(double easting, double northing)
    {
        // For Iran, coordinates typically fall in zones 38-41
        // We try each zone and see which one produces valid Iran coordinates
        // 
        // Iran bounds:
        // Latitude: 25°N to 40°N
        // Longitude: 44°E to 64°E

        int[] iranZones = { 39, 40, 38, 41 }; // Order by likelihood for Iran

        foreach (int zone in iranZones)
        {
            UTMToGeographicInternal(zone, true, easting, northing, out double lon, out double lat);

            // Check if result is within Iran bounds
            if (lat >= IRAN_MIN_LAT && lat <= IRAN_MAX_LAT &&
                lon >= IRAN_MIN_LON && lon <= IRAN_MAX_LON)
            {
                // Also verify the zone matches the longitude
                int expectedZone = GetUTMZone(lon);
                if (expectedZone == zone)
                {
                    return zone;
                }
            }
        }

        // Default to zone 39 if no valid zone found
        return 39;
    }

    #endregion

    #region Utility Methods

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    #endregion

    #region Information Functions

    /// <summary>
    /// Get information about IRNG system
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString GetIRNGInfo()
    {
        return new SqlString(
            "Iran National Grid (IRNG) - نشریه ۸-۱۱۹\n" +
            "Based on: UTM (Universal Transverse Mercator)\n" +
            "Datum: WGS84\n" +
            "Iran UTM Zones: 38, 39, 40, 41\n" +
            "Grid Format: XY EEEEE NNNNN (e.g., HN 28418 51576)\n" +
            "100km Square: Two letters (e.g., HN, GP, FL)\n" +
            "Coordinates: 5-digit easting and northing within square"
        );
    }

    /// <summary>
    /// Get Proj4 string for UTM zone
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString GetUTMProj4String(SqlInt32 zone)
    {
        if (zone.IsNull)
            return SqlString.Null;

        int z = zone.Value;
        double cm = (z - 1) * 6 - 180 + 3;

        return new SqlString(
            $"+proj=utm +zone={z} +datum=WGS84 +units=m +no_defs"
        );
    }

    /// <summary>
    /// Get WKT for UTM zone
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString GetUTMWKT(SqlInt32 zone)
    {
        if (zone.IsNull)
            return SqlString.Null;

        int z = zone.Value;
        double cm = (z - 1) * 6 - 180 + 3;

        return new SqlString(
            $@"PROJCS[""WGS 84 / UTM zone {z}N"",
    GEOGCS[""WGS 84"",
        DATUM[""WGS_1984"",
            SPHEROID[""WGS 84"",6378137,298.257223563]],
        PRIMEM[""Greenwich"",0],
        UNIT[""degree"",0.0174532925199433]],
    PROJECTION[""Transverse_Mercator""],
    PARAMETER[""latitude_of_origin"",0],
    PARAMETER[""central_meridian"",{cm}],
    PARAMETER[""scale_factor"",0.9996],
    PARAMETER[""false_easting"",500000],
    PARAMETER[""false_northing"",0],
    UNIT[""metre"",1]]"
        );
    }

    #endregion
}
