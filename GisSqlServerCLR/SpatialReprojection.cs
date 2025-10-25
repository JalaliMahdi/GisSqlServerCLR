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

    /////////////////////////////////////////////

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

            // Exterior Ring
            SqlGeometry exteriorRing = polygon.STExteriorRing();
            wktBuilder.Append(TransformRing(exteriorRing, sourceProj, destinationProj));

            // Interior Rings
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
        double[] coordinates = { x, y };
        double[] z = { 0 };

        var sourceProjection = GetProjectionFromEpsg(sourceProj);
        var destinationProjection = GetProjectionFromEpsg(destinationProj);

        DotSpatial.Projections.Reproject.ReprojectPoints(coordinates, z, sourceProjection, destinationProjection, 0, 1);

        x = coordinates[0];
        y = coordinates[1];
    }

    private static string FormatPoint(double x, double y)
    {
        return $"POINT ({FormatCoordinate(x, y)})";
    }

    private static string FormatCoordinate(double x, double y)
    {
        return $"{x.ToString("G17", CultureInfo.InvariantCulture)} {y.ToString("G17", CultureInfo.InvariantCulture)}";
    }

    private static DotSpatial.Projections.ProjectionInfo GetProjectionFromEpsg(int epsgCode)
    {
        switch (epsgCode)
        {
            // EPSG:4326 - WGS84 (Geographic)
            case 4326:
                return DotSpatial.Projections.KnownCoordinateSystems.Geographic.World.WGS1984;

            // EPSG:3857 - Web Mercator
            case 3857:
                return DotSpatial.Projections.KnownCoordinateSystems.Projected.World.WebMercator;

            // UTM Zones (Northern Hemisphere)
            case 32638: // UTM Zone 38N
                return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone38N;

            case 32639: // UTM Zone 39N
                return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone39N;

            case 32640: // UTM Zone 40N
                return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone40N;

            case 32641: // UTM Zone 41N
                return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone41N;

            case 32642: // UTM Zone 42N
                return DotSpatial.Projections.KnownCoordinateSystems.Projected.UtmWgs1984.WGS1984UTMZone42N;

            default:
                throw new ArgumentException($"Unsupported EPSG code: {epsgCode}");
        }
    }
}