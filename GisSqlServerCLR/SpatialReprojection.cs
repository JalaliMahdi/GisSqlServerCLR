using System;
using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using Microsoft.SqlServer.Server;
using Microsoft.SqlServer.Types;

public class SpatialReprojection
{
    [SqlFunction(
        IsDeterministic = true,
        IsPrecise = true)]
    public static SqlString TransformWktGeometry(string geometry, int srcProj, int dstProj)
    {
        if (string.IsNullOrWhiteSpace(geometry))
        {
            return SqlString.Null;
        }

        try
        {
            SqlGeometry geom = SqlGeometry.STGeomFromText(new SqlChars(geometry), srcProj);

            SqlGeometry transformed = TransformGeometry(geom, dstProj);

            if (transformed.IsNull)
            {
                return SqlString.Null;
            }

            return transformed.STAsText().ToSqlString();
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Error transforming WKT geometry: {ex.Message}", ex);
        }
    }

    [SqlFunction(
        IsDeterministic = true,
        IsPrecise = true)]
    public static SqlGeometry TransformGeometry(SqlGeometry geometry, int dstProj)
    {
        if (geometry == null || geometry.IsNull)
        {
            return SqlGeometry.Null;
        }

        try
        {
            SqlInt32 sqlSrid = geometry.STSrid;
            int srcProj = sqlSrid.Value;

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

    private static void TransformCoordinate(ref double x, ref double y, int sourceProj, int destinationProj)
    {
        double[] xy = { x, y };
        double[] z = { 0 };

        var sourceProjection = GetProjectionFromEpsg(sourceProj);
        var destinationProjection = GetProjectionFromEpsg(destinationProj);

        DotSpatial.Projections.Reproject.ReprojectPoints(xy, z, sourceProjection, destinationProjection, 0, 1);

        x = xy[0];
        y = xy[1];
    }

    private static string FormatPoint(double x, double y)
    {
        return $"POINT ({FormatCoordinate(x, y)})";
    }

    private static string FormatCoordinate(double x, double y)
    {
        double roundedX = Math.Round(x, 10);
        double roundedY = Math.Round(y, 10);

        return $"{roundedX.ToString("G17", CultureInfo.InvariantCulture)} {roundedY.ToString("G17", CultureInfo.InvariantCulture)}";
    }

    private static DotSpatial.Projections.ProjectionInfo GetProjectionFromEpsg(int epsgCode)
    {
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
        }

        switch (epsgCode)
        {
            case 4326:
                return DotSpatial.Projections.KnownCoordinateSystems.Geographic.World.WGS1984;

            case 3857:
                return DotSpatial.Projections.KnownCoordinateSystems.Projected.World.WebMercator;

            case 32601: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone1N;
            case 32602: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone2N;
            case 32603: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone3N;
            case 32604: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone4N;
            case 32605: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone5N;
            case 32606: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone6N;
            case 32607: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone7N;
            case 32608: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone8N;
            case 32609: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone9N;
            case 32610: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone10N;
            case 32611: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone11N;
            case 32612: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone12N;
            case 32613: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone13N;
            case 32614: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone14N;
            case 32615: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone15N;
            case 32616: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone16N;
            case 32617: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone17N;
            case 32618: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone18N;
            case 32619: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone19N;
            case 32620: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone20N;
            case 32621: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone21N;
            case 32622: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone22N;
            case 32623: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone23N;
            case 32624: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone24N;
            case 32625: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone25N;
            case 32626: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone26N;
            case 32627: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone27N;
            case 32628: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone28N;
            case 32629: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone29N;
            case 32630: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone30N;
            case 32631: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone31N;
            case 32632: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone32N;
            case 32633: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone33N;
            case 32634: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone34N;
            case 32635: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone35N;
            case 32636: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone36N;
            case 32637: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone37N;
            case 32638: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone38N;
            case 32639: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone39N;
            case 32640: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone40N;
            case 32641: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone41N;
            case 32642: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone42N;
            case 32643: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone43N;
            case 32644: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone44N;
            case 32645: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone45N;
            case 32646: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone46N;
            case 32647: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone47N;
            case 32648: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone48N;
            case 32649: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone49N;
            case 32650: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone50N;
            case 32651: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone51N;
            case 32652: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone52N;
            case 32653: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone53N;
            case 32654: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone54N;
            case 32655: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone55N;
            case 32656: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone56N;
            case 32657: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone57N;
            case 32658: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone58N;
            case 32659: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone59N;
            case 32660: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone60N;

            case 32701: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone1S;
            case 32702: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone2S;
            case 32703: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone3S;
            case 32704: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone4S;
            case 32705: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone5S;
            case 32706: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone6S;
            case 32707: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone7S;
            case 32708: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone8S;
            case 32709: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone9S;
            case 32710: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone10S;
            case 32711: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone11S;
            case 32712: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone12S;
            case 32713: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone13S;
            case 32714: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone14S;
            case 32715: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone15S;
            case 32716: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone16S;
            case 32717: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone17S;
            case 32718: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone18S;
            case 32719: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone19S;
            case 32720: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone20S;
            case 32721: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone21S;
            case 32722: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone22S;
            case 32723: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone23S;
            case 32724: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone24S;
            case 32725: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone25S;
            case 32726: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone26S;
            case 32727: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone27S;
            case 32728: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone28S;
            case 32729: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone29S;
            case 32730: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone30S;
            case 32731: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone31S;
            case 32732: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone32S;
            case 32733: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone33S;
            case 32734: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone34S;
            case 32735: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone35S;
            case 32736: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone36S;
            case 32737: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone37S;
            case 32738: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone38S;
            case 32739: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone39S;
            case 32740: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone40S;
            case 32741: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone41S;
            case 32742: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone42S;
            case 32743: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone43S;
            case 32744: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone44S;
            case 32745: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone45S;
            case 32746: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone46S;
            case 32747: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone47S;
            case 32748: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone48S;
            case 32749: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone49S;
            case 32750: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone50S;
            case 32751: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone51S;
            case 32752: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone52S;
            case 32753: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone53S;
            case 32754: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone54S;
            case 32755: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone55S;
            case 32756: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone56S;
            case 32757: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone57S;
            case 32758: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone58S;
            case 32759: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone59S;
            case 32760: return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone60S;

            default:
                throw new ArgumentException($"EPSG:{epsgCode} not supported. Add it manually or ensure projection database is available.");
        }
    }
}