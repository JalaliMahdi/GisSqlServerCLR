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
            SqlGeometry geometry = SqlGeometry.STGeomFromText(
                new System.Data.SqlTypes.SqlChars("POINT (6048669.75554771 3751819.65061306)"), 3857);

            SqlGeometry geometryPolygon = SqlGeometry.STGeomFromText(
                new System.Data.SqlTypes.SqlChars("POLYGON ((-64.8 32.3, -65.5 18.3, -80.3 25.2, -64.8 32.3))"), 4326);

            SqlGeometry geometryCollection = SqlGeometry.STGeomFromText(
                new System.Data.SqlTypes.SqlChars("GEOMETRYCOLLECTION(POINT(12.4924 41.8902),LINESTRING(12.4924 41.8902, 12.4960 41.9028),POLYGON((  12.4924 41.8902,  12.4960 41.9028,  12.4800 41.8950,  12.4924 41.8902))\r\n)"), 4326);

            var a2 = SpatialReprojection.TransformGeometry(geometryPolygon, 3857);
            var a23 = SpatialReprojection.TransformGeometry(geometryCollection, 3857);

            var a = SpatialReprojection.TransformGeometry(geometry,  4326);

            var transformed = SpatialReprojection.TransformWktGeometry("POINT (54.305529999999997 31.925776599999999)", 4326, 3857);
            Console.WriteLine($"Tranformed : {transformed.Value}");
        }
    }
}
