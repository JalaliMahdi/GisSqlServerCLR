using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using Microsoft.SqlServer.Types;

namespace GisSqlServerCLR
{
    public class SpatialReprojection
    {

        [SqlFunction]
        public static string TransformWktGeometry(string geometry, int srcProj, int dstProj)
        {
            string processedGeometry = ProcessWktGeometry(geometry, srcProj, dstProj);
            return processedGeometry;
        }

        [SqlFunction]
        public static SqlGeometry TransformGeometry(SqlGeometry geometry, int dstProj)
        {
            SqlGeometry processedGeometry = ProcessGeometry(geometry, dstProj);
            return processedGeometry;
        }

        /////////////////////////////////////////////
        
        private static SqlGeometry ProcessGeometry(SqlGeometry geometry, int destinationProj4)
        {
            SqlInt32 sqkSrid = geometry.STSrid;
            SqlChars sqlWkt = geometry.STAsText();

            int srid = sqkSrid.Value;
            string wkt = sqlWkt.ToSqlString().Value;

            string processedGeometry = ProcessWktGeometry(wkt, srid, destinationProj4);

            SqlGeometry geom = SqlGeometry.STGeomFromText(new SqlChars(processedGeometry), destinationProj4);

            return geom;
        }

        private static string ProcessWktGeometry(string geometry, int sourceProj4, int destinationProj4)
        {
            int startIndex = geometry.IndexOf('(');
            string geometryContent = geometry.Substring(startIndex).Trim();

            string[] coordinateGroups = geometryContent.Split(',');

            foreach (var group in coordinateGroups)
            {
                string cleanedGroup = CleanCoordinateGroup(group);
                string[] coordinates = cleanedGroup.Split(' ');

                for (int i = 0; i < coordinates.Length - 1; i += 2)
                {
                    string originalPair = $"{coordinates[i]} {coordinates[i + 1]}";

                    string transformedPair = TransformCoordinatePair(
                        double.Parse(coordinates[i]),
                        double.Parse(coordinates[i + 1]),
                        sourceProj4,
                        destinationProj4
                    );

                    geometry = geometry.Replace(originalPair, transformedPair);
                }
            }

            return geometry;
        }

        private static string CleanCoordinateGroup(string group)
        {
            return group.Replace('(', ' ').Replace(')', ' ').Trim();
        }

        private static string TransformCoordinatePair(double x, double y, int sourceProj4, int destinationProj4)
        {
            double[] coordinates = { x, y };
            double[] z = { 0 };

            var sourceProjection = GetProjectionFromEpsg(sourceProj4);
            var destinationProjection = GetProjectionFromEpsg(destinationProj4);

            DotSpatial.Projections.Reproject.ReprojectPoints(coordinates, z, sourceProjection, destinationProjection, 0, 1);

            return $"{coordinates[0]} {coordinates[1]}";
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
}