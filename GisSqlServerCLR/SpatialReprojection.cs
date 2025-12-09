using System;
using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using Microsoft.SqlServer.Server;
using Microsoft.SqlServer.Types;

/// <summary>
/// SQL Server CLR functions for spatial coordinate transformations
/// </summary>
public class SpatialReprojection
{
    // G17 format ensures round-trip accuracy for double (17 significant digits)
  private const string COORDINATE_FORMAT = "G17";

    #region Public SQL Functions

    /// <summary>
    /// Transforms WKT geometry string from source to destination projection
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlString TransformWktGeometry(string geometry, int srcProj, int dstProj)
    {
        if (string.IsNullOrWhiteSpace(geometry))
        {
            return SqlString.Null;
        }

        try
        {
            string trimmed = geometry.Trim();

            // Handle BOX format (non-standard WKT)
            if (trimmed.StartsWith("BOX", StringComparison.OrdinalIgnoreCase))
            {
                return TransformBoxGeometry(trimmed, srcProj, dstProj);
            }

            SqlGeometry geom = SqlGeometry.STGeomFromText(new SqlChars(geometry), srcProj);
            SqlGeometry transformed = TransformGeometry(geom, dstProj);

            return transformed.IsNull ? SqlString.Null : transformed.STAsText().ToSqlString();
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Error transforming WKT geometry: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Transforms SqlGeometry from its SRID to destination projection
    /// </summary>
    [SqlFunction(IsDeterministic = true, IsPrecise = true)]
    public static SqlGeometry TransformGeometry(SqlGeometry geometry, int dstProj)
    {
        if (geometry == null || geometry.IsNull)
        {
            return SqlGeometry.Null;
        }

        try
        {
            int srcProj = geometry.STSrid.Value;

            if (srcProj == dstProj)
            {
                return geometry;
            }

            return ProcessGeometry(geometry, srcProj, dstProj);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Error transforming geometry: {ex.Message}", ex);
        }
    }

    #endregion

    #region BOX Format Handling

    private static SqlString TransformBoxGeometry(string boxWkt, int srcProj, int dstProj)
    {
        // Parse BOX(minX minY, maxX maxY) format
        int startParen = boxWkt.IndexOf('(');
        int endParen = boxWkt.LastIndexOf(')');

        if (startParen == -1 || endParen == -1 || endParen <= startParen)
        {
            throw new ArgumentException("Invalid BOX format. Expected: BOX(x1 y1, x2 y2)");
        }

        string coordinates = boxWkt.Substring(startParen + 1, endParen - startParen - 1);
        string[] points = coordinates.Split(',');

        if (points.Length != 2)
        {
            throw new ArgumentException("BOX format must contain exactly two coordinate pairs separated by comma");
        }

        // Parse first point
        string[] coord1 = points[0].Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (coord1.Length < 2)
        {
            throw new ArgumentException("Invalid coordinates in BOX format");
        }

        double x1 = double.Parse(coord1[0], CultureInfo.InvariantCulture);
        double y1 = double.Parse(coord1[1], CultureInfo.InvariantCulture);

        // Parse second point
        string[] coord2 = points[1].Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (coord2.Length < 2)
        {
            throw new ArgumentException("Invalid coordinates in BOX format");
        }

        double x2 = double.Parse(coord2[0], CultureInfo.InvariantCulture);
        double y2 = double.Parse(coord2[1], CultureInfo.InvariantCulture);

        // Transform both corners
        TransformCoordinate(ref x1, ref y1, srcProj, dstProj);
        TransformCoordinate(ref x2, ref y2, srcProj, dstProj);

        // Ensure min/max order after transformation
        double minX = Math.Min(x1, x2);
        double maxX = Math.Max(x1, x2);
        double minY = Math.Min(y1, y2);
        double maxY = Math.Max(y1, y2);

        // Format result as BOX
        string result = $"BOX({FormatCoordinate(minX, minY)}, {FormatCoordinate(maxX, maxY)})";
        return new SqlString(result);
    }

    #endregion

    #region Geometry Processing

    private static SqlGeometry ProcessGeometry(SqlGeometry geometry, int sourceProj, int destinationProj)
    {
        string geometryType = geometry.STGeometryType().Value;

        switch (geometryType.ToUpper())
        {
            case "POINT":
                return TransformPoint(geometry, sourceProj, destinationProj);

            case "LINESTRING":
                return TransformLineString(geometry, sourceProj, destinationProj);

            case "POLYGON":
                return TransformPolygon(geometry, sourceProj, destinationProj);

            case "MULTIPOINT":
                return TransformMultiPoint(geometry, sourceProj, destinationProj);

            case "MULTILINESTRING":
                return TransformMultiLineString(geometry, sourceProj, destinationProj);

            case "MULTIPOLYGON":
                return TransformMultiPolygon(geometry, sourceProj, destinationProj);

            case "GEOMETRYCOLLECTION":
                return TransformGeometryCollection(geometry, sourceProj, destinationProj);

            default:
                throw new NotSupportedException($"Geometry type '{geometryType}' is not supported");
        }
    }

    private static SqlGeometry TransformPoint(SqlGeometry point, int sourceProj, int destinationProj)
    {
        double x = point.STX.Value;
        double y = point.STY.Value;

        TransformCoordinate(ref x, ref y, sourceProj, destinationProj);

        string wkt = FormatPoint(x, y);
        return SqlGeometry.STGeomFromText(new SqlChars(wkt), destinationProj);
    }

    private static SqlGeometry TransformLineString(SqlGeometry lineString, int sourceProj, int destinationProj)
    {
        int numPoints = lineString.STNumPoints().Value;
        StringBuilder wktBuilder = new StringBuilder("LINESTRING (");

        for (int i = 1; i <= numPoints; i++)
        {
            SqlGeometry point = lineString.STPointN(i);
            double x = point.STX.Value;
            double y = point.STY.Value;

            TransformCoordinate(ref x, ref y, sourceProj, destinationProj);

            if (i > 1)
            {
                wktBuilder.Append(", ");
            }
            wktBuilder.Append(FormatCoordinate(x, y));
        }

        wktBuilder.Append(")");
        return SqlGeometry.STGeomFromText(new SqlChars(wktBuilder.ToString()), destinationProj);
    }

    private static SqlGeometry TransformPolygon(SqlGeometry polygon, int sourceProj, int destinationProj)
    {
        StringBuilder wktBuilder = new StringBuilder("POLYGON (");

        SqlGeometry exteriorRing = polygon.STExteriorRing();
        wktBuilder.Append(TransformRing(exteriorRing, sourceProj, destinationProj));

        int numInteriorRings = polygon.STNumInteriorRing().Value;
        for (int i = 1; i <= numInteriorRings; i++)
        {
            wktBuilder.Append(", ");
            SqlGeometry interiorRing = polygon.STInteriorRingN(i);
            wktBuilder.Append(TransformRing(interiorRing, sourceProj, destinationProj));
        }

        wktBuilder.Append(")");
        return SqlGeometry.STGeomFromText(new SqlChars(wktBuilder.ToString()), destinationProj);
    }

    private static SqlGeometry TransformMultiPoint(SqlGeometry multiPoint, int sourceProj, int destinationProj)
    {
        int numGeometries = multiPoint.STNumGeometries().Value;
        StringBuilder wktBuilder = new StringBuilder("MULTIPOINT (");

        for (int i = 1; i <= numGeometries; i++)
        {
            SqlGeometry point = multiPoint.STGeometryN(i);
            double x = point.STX.Value;
            double y = point.STY.Value;

            TransformCoordinate(ref x, ref y, sourceProj, destinationProj);

            if (i > 1)
            {
                wktBuilder.Append(", ");
            }
            wktBuilder.Append($"({FormatCoordinate(x, y)})");
        }

        wktBuilder.Append(")");
        return SqlGeometry.STGeomFromText(new SqlChars(wktBuilder.ToString()), destinationProj);
    }

    private static SqlGeometry TransformMultiLineString(SqlGeometry multiLineString, int sourceProj, int destinationProj)
    {
        int numGeometries = multiLineString.STNumGeometries().Value;
        StringBuilder wktBuilder = new StringBuilder("MULTILINESTRING (");

        for (int i = 1; i <= numGeometries; i++)
        {
            if (i > 1)
            {
                wktBuilder.Append(", ");
            }

            SqlGeometry lineString = multiLineString.STGeometryN(i);
            int numPoints = lineString.STNumPoints().Value;
            wktBuilder.Append("(");

            for (int j = 1; j <= numPoints; j++)
            {
                SqlGeometry point = lineString.STPointN(j);
                double x = point.STX.Value;
                double y = point.STY.Value;

                TransformCoordinate(ref x, ref y, sourceProj, destinationProj);

                if (j > 1)
                {
                    wktBuilder.Append(", ");
                }
                wktBuilder.Append(FormatCoordinate(x, y));
            }

            wktBuilder.Append(")");
        }

        wktBuilder.Append(")");
        return SqlGeometry.STGeomFromText(new SqlChars(wktBuilder.ToString()), destinationProj);
    }

    private static SqlGeometry TransformMultiPolygon(SqlGeometry multiPolygon, int sourceProj, int destinationProj)
    {
        int numGeometries = multiPolygon.STNumGeometries().Value;
        StringBuilder wktBuilder = new StringBuilder("MULTIPOLYGON (");

        for (int i = 1; i <= numGeometries; i++)
        {
            if (i > 1)
            {
                wktBuilder.Append(", ");
            }

            SqlGeometry polygon = multiPolygon.STGeometryN(i);
            wktBuilder.Append("(");

            SqlGeometry exteriorRing = polygon.STExteriorRing();
            wktBuilder.Append(TransformRing(exteriorRing, sourceProj, destinationProj));

            int numInteriorRings = polygon.STNumInteriorRing().Value;
            for (int j = 1; j <= numInteriorRings; j++)
            {
                wktBuilder.Append(", ");
                SqlGeometry interiorRing = polygon.STInteriorRingN(j);
                wktBuilder.Append(TransformRing(interiorRing, sourceProj, destinationProj));
            }

            wktBuilder.Append(")");
        }

        wktBuilder.Append(")");
        return SqlGeometry.STGeomFromText(new SqlChars(wktBuilder.ToString()), destinationProj);
    }

    private static SqlGeometry TransformGeometryCollection(SqlGeometry geometryCollection, int sourceProj, int destinationProj)
    {
        int numGeometries = geometryCollection.STNumGeometries().Value;
        StringBuilder wktBuilder = new StringBuilder("GEOMETRYCOLLECTION (");

        for (int i = 1; i <= numGeometries; i++)
        {
            if (i > 1)
            {
                wktBuilder.Append(", ");
            }

            SqlGeometry geometry = geometryCollection.STGeometryN(i);
            SqlGeometry transformedGeom = ProcessGeometry(geometry, sourceProj, destinationProj);

            string wkt = transformedGeom.STAsText().ToSqlString().Value;
            wktBuilder.Append(wkt);
        }

        wktBuilder.Append(")");
        return SqlGeometry.STGeomFromText(new SqlChars(wktBuilder.ToString()), destinationProj);
    }

    private static string TransformRing(SqlGeometry ring, int sourceProj, int destinationProj)
    {
        int numPoints = ring.STNumPoints().Value;
        StringBuilder ringBuilder = new StringBuilder("(");

        for (int i = 1; i <= numPoints; i++)
        {
            SqlGeometry point = ring.STPointN(i);
            double x = point.STX.Value;
            double y = point.STY.Value;

            TransformCoordinate(ref x, ref y, sourceProj, destinationProj);

            if (i > 1)
            {
                ringBuilder.Append(", ");
            }
            ringBuilder.Append(FormatCoordinate(x, y));
        }

        ringBuilder.Append(")");
        return ringBuilder.ToString();
    }

    #endregion

    #region Coordinate Transformation

    private static void TransformCoordinate(ref double x, ref double y, int sourceProj, int destinationProj)
    {
        // Handle IRNG (Iran National Grid) transformations directly
        const int IRNG_SRID = 102030;
        const int WGS84_SRID = 4326;

        if (sourceProj == IRNG_SRID || destinationProj == IRNG_SRID)
        {
            if (sourceProj == WGS84_SRID && destinationProj == IRNG_SRID)
            {
                // WGS84 to IRNG
                IranNationalGrid.GeographicToIRNGInternal(x, y, out double easting, out double northing);
                x = easting;
                y = northing;
                return;
            }
            else if (sourceProj == IRNG_SRID && destinationProj == WGS84_SRID)
            {
                // IRNG to WGS84
                IranNationalGrid.IRNGToGeographicInternal(x, y, out double lon, out double lat);
                x = lon;
                y = lat;
                return;
            }
            else if (sourceProj == IRNG_SRID)
            {
                // IRNG to other: first convert to WGS84
                IranNationalGrid.IRNGToGeographicInternal(x, y, out double lon, out double lat);
                x = lon;
                y = lat;
                // Then convert WGS84 to destination
                TransformCoordinate(ref x, ref y, WGS84_SRID, destinationProj);
                return;
            }
            else if (destinationProj == IRNG_SRID)
            {
                // Other to IRNG: first convert to WGS84
                TransformCoordinate(ref x, ref y, sourceProj, WGS84_SRID);
                // Then convert WGS84 to IRNG
                IranNationalGrid.GeographicToIRNGInternal(x, y, out double easting, out double northing);
                x = easting;
                y = northing;
                return;
            }
        }

        double[] xy = { x, y };
        double[] z = { 0 };

        var sourceProjection = GetProjectionFromEpsg(sourceProj);
        var destinationProjection = GetProjectionFromEpsg(destinationProj);

        DotSpatial.Projections.Reproject.ReprojectPoints(xy, z, sourceProjection, destinationProjection, 0, 1);

        x = xy[0];
        y = xy[1];
    }

    #endregion

    #region Formatting Helpers

    private static string FormatPoint(double x, double y)
    {
        return $"POINT ({FormatCoordinate(x, y)})";
    }

    private static string FormatCoordinate(double x, double y)
    {
        // Use G17 directly without rounding to preserve maximum precision
        // G17 ensures round-trip accuracy for double values (17 significant digits)
   return $"{x.ToString(COORDINATE_FORMAT, CultureInfo.InvariantCulture)} {y.ToString(COORDINATE_FORMAT, CultureInfo.InvariantCulture)}";
    }

    #endregion

    #region Projection Helpers

    private static DotSpatial.Projections.ProjectionInfo GetProjectionFromEpsg(int epsgCode)
    {
        // Try to load from DotSpatial database first
        try
        {
            var projection = DotSpatial.Projections.ProjectionInfo.FromAuthorityCode("EPSG", epsgCode);
            if (projection != null)
            {
                return projection;
            }
        }
        catch
        {
            // Continue to hardcoded mappings
        }

        // Common projections
        if (epsgCode == 4326)
            return DotSpatial.Projections.KnownCoordinateSystems.Geographic.World.WGS1984;

        if (epsgCode == 3857)
            return DotSpatial.Projections.KnownCoordinateSystems.Projected.World.WebMercator;

        // UTM zones
        if (epsgCode >= 32601 && epsgCode <= 32660)
            return GetUtmNorthProjection(epsgCode);

        if (epsgCode >= 32701 && epsgCode <= 32760)
            return GetUtmSouthProjection(epsgCode);

        throw new ArgumentException($"EPSG:{epsgCode} not supported. Add it manually or ensure projection database is available.");
    }

    private static DotSpatial.Projections.ProjectionInfo GetUtmNorthProjection(int epsgCode)
    {
        var utmWgs84 = DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984;

        switch (epsgCode)
        {
            case 32601: return utmWgs84.WGS1984UTMZone1N;
            case 32602: return utmWgs84.WGS1984UTMZone2N;
            case 32603: return utmWgs84.WGS1984UTMZone3N;
            case 32604: return utmWgs84.WGS1984UTMZone4N;
            case 32605: return utmWgs84.WGS1984UTMZone5N;
            case 32606: return utmWgs84.WGS1984UTMZone6N;
            case 32607: return utmWgs84.WGS1984UTMZone7N;
            case 32608: return utmWgs84.WGS1984UTMZone8N;
            case 32609: return utmWgs84.WGS1984UTMZone9N;
            case 32610: return utmWgs84.WGS1984UTMZone10N;
            case 32611: return utmWgs84.WGS1984UTMZone11N;
            case 32612: return utmWgs84.WGS1984UTMZone12N;
            case 32613: return utmWgs84.WGS1984UTMZone13N;
            case 32614: return utmWgs84.WGS1984UTMZone14N;
            case 32615: return utmWgs84.WGS1984UTMZone15N;
            case 32616: return utmWgs84.WGS1984UTMZone16N;
            case 32617: return utmWgs84.WGS1984UTMZone17N;
            case 32618: return utmWgs84.WGS1984UTMZone18N;
            case 32619: return utmWgs84.WGS1984UTMZone19N;
            case 32620: return utmWgs84.WGS1984UTMZone20N;
            case 32621: return utmWgs84.WGS1984UTMZone21N;
            case 32622: return utmWgs84.WGS1984UTMZone22N;
            case 32623: return utmWgs84.WGS1984UTMZone23N;
            case 32624: return utmWgs84.WGS1984UTMZone24N;
            case 32625: return utmWgs84.WGS1984UTMZone25N;
            case 32626: return utmWgs84.WGS1984UTMZone26N;
            case 32627: return utmWgs84.WGS1984UTMZone27N;
            case 32628: return utmWgs84.WGS1984UTMZone28N;
            case 32629: return utmWgs84.WGS1984UTMZone29N;
            case 32630: return utmWgs84.WGS1984UTMZone30N;
            case 32631: return utmWgs84.WGS1984UTMZone31N;
            case 32632: return utmWgs84.WGS1984UTMZone32N;
            case 32633: return utmWgs84.WGS1984UTMZone33N;
            case 32634: return utmWgs84.WGS1984UTMZone34N;
            case 32635: return utmWgs84.WGS1984UTMZone35N;
            case 32636: return utmWgs84.WGS1984UTMZone36N;
            case 32637: return utmWgs84.WGS1984UTMZone37N;
            case 32638: return utmWgs84.WGS1984UTMZone38N;
            case 32639: return utmWgs84.WGS1984UTMZone39N;
            case 32640: return utmWgs84.WGS1984UTMZone40N;
            case 32641: return utmWgs84.WGS1984UTMZone41N;
            case 32642: return utmWgs84.WGS1984UTMZone42N;
            case 32643: return utmWgs84.WGS1984UTMZone43N;
            case 32644: return utmWgs84.WGS1984UTMZone44N;
            case 32645: return utmWgs84.WGS1984UTMZone45N;
            case 32646: return utmWgs84.WGS1984UTMZone46N;
            case 32647: return utmWgs84.WGS1984UTMZone47N;
            case 32648: return utmWgs84.WGS1984UTMZone48N;
            case 32649: return utmWgs84.WGS1984UTMZone49N;
            case 32650: return utmWgs84.WGS1984UTMZone50N;
            case 32651: return utmWgs84.WGS1984UTMZone51N;
            case 32652: return utmWgs84.WGS1984UTMZone52N;
            case 32653: return utmWgs84.WGS1984UTMZone53N;
            case 32654: return utmWgs84.WGS1984UTMZone54N;
            case 32655: return utmWgs84.WGS1984UTMZone55N;
            case 32656: return utmWgs84.WGS1984UTMZone56N;
            case 32657: return utmWgs84.WGS1984UTMZone57N;
            case 32658: return utmWgs84.WGS1984UTMZone58N;
            case 32659: return utmWgs84.WGS1984UTMZone59N;
            case 32660: return utmWgs84.WGS1984UTMZone60N;
            default:
                throw new ArgumentException($"EPSG:{epsgCode} not supported.");
        }
    }

    private static DotSpatial.Projections.ProjectionInfo GetUtmSouthProjection(int epsgCode)
    {
        var utmWgs84 = DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984;

        switch (epsgCode)
        {
            case 32701: return utmWgs84.WGS1984UTMZone1S;
            case 32702: return utmWgs84.WGS1984UTMZone2S;
            case 32703: return utmWgs84.WGS1984UTMZone3S;
            case 32704: return utmWgs84.WGS1984UTMZone4S;
            case 32705: return utmWgs84.WGS1984UTMZone5S;
            case 32706: return utmWgs84.WGS1984UTMZone6S;
            case 32707: return utmWgs84.WGS1984UTMZone7S;
            case 32708: return utmWgs84.WGS1984UTMZone8S;
            case 32709: return utmWgs84.WGS1984UTMZone9S;
            case 32710: return utmWgs84.WGS1984UTMZone10S;
            case 32711: return utmWgs84.WGS1984UTMZone11S;
            case 32712: return utmWgs84.WGS1984UTMZone12S;
            case 32713: return utmWgs84.WGS1984UTMZone13S;
            case 32714: return utmWgs84.WGS1984UTMZone14S;
            case 32715: return utmWgs84.WGS1984UTMZone15S;
            case 32716: return utmWgs84.WGS1984UTMZone16S;
            case 32717: return utmWgs84.WGS1984UTMZone17S;
            case 32718: return utmWgs84.WGS1984UTMZone18S;
            case 32719: return utmWgs84.WGS1984UTMZone19S;
            case 32720: return utmWgs84.WGS1984UTMZone20S;
            case 32721: return utmWgs84.WGS1984UTMZone21S;
            case 32722: return utmWgs84.WGS1984UTMZone22S;
            case 32723: return utmWgs84.WGS1984UTMZone23S;
            case 32724: return utmWgs84.WGS1984UTMZone24S;
            case 32725: return utmWgs84.WGS1984UTMZone25S;
            case 32726: return utmWgs84.WGS1984UTMZone26S;
            case 32727: return utmWgs84.WGS1984UTMZone27S;
            case 32728: return utmWgs84.WGS1984UTMZone28S;
            case 32729: return utmWgs84.WGS1984UTMZone29S;
            case 32730: return utmWgs84.WGS1984UTMZone30S;
            case 32731: return utmWgs84.WGS1984UTMZone31S;
            case 32732: return utmWgs84.WGS1984UTMZone32S;
            case 32733: return utmWgs84.WGS1984UTMZone33S;
            case 32734: return utmWgs84.WGS1984UTMZone34S;
            case 32735: return utmWgs84.WGS1984UTMZone35S;
            case 32736: return utmWgs84.WGS1984UTMZone36S;
            case 32737: return utmWgs84.WGS1984UTMZone37S;
            case 32738: return utmWgs84.WGS1984UTMZone38S;
            case 32739: return utmWgs84.WGS1984UTMZone39S;
            case 32740: return utmWgs84.WGS1984UTMZone40S;
            case 32741: return utmWgs84.WGS1984UTMZone41S;
            case 32742: return utmWgs84.WGS1984UTMZone42S;
            case 32743: return utmWgs84.WGS1984UTMZone43S;
            case 32744: return utmWgs84.WGS1984UTMZone44S;
            case 32745: return utmWgs84.WGS1984UTMZone45S;
            case 32746: return utmWgs84.WGS1984UTMZone46S;
            case 32747: return utmWgs84.WGS1984UTMZone47S;
            case 32748: return utmWgs84.WGS1984UTMZone48S;
            case 32749: return utmWgs84.WGS1984UTMZone49S;
            case 32750: return utmWgs84.WGS1984UTMZone50S;
            case 32751: return utmWgs84.WGS1984UTMZone51S;
            case 32752: return utmWgs84.WGS1984UTMZone52S;
            case 32753: return utmWgs84.WGS1984UTMZone53S;
            case 32754: return utmWgs84.WGS1984UTMZone54S;
            case 32755: return utmWgs84.WGS1984UTMZone55S;
            case 32756: return utmWgs84.WGS1984UTMZone56S;
            case 32757: return utmWgs84.WGS1984UTMZone57S;
            case 32758: return utmWgs84.WGS1984UTMZone58S;
            case 32759: return utmWgs84.WGS1984UTMZone59S;
            case 32760: return utmWgs84.WGS1984UTMZone60S;
            default:
                throw new ArgumentException($"EPSG:{epsgCode} not supported.");
        }
    }

    #endregion
}
