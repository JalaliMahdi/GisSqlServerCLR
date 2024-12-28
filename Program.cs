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


            var a = SpatialReprojection.TransformGeometry(geometry,  4326);

            var transformed = SpatialReprojection.TransformWktGeometry("POINT (54.305529999999997 31.925776599999999)", 4326, 3857);
            Console.WriteLine($"Tranformed : {transformed.Value}");
        }
    }
}
