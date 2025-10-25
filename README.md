# SQL Server CLR Integration - GIS Spatial Functions

SQL Server CLR assembly for coordinate system transformations using DotSpatial.

## Features

- **STTransform**: Transform geometry between coordinate systems
- **STTransformWkt**: Transform WKT strings between coordinate systems  
- **STExtent**: Calculate bounding box (aggregate function)

**Supported EPSG Codes**: 4326 (WGS84), 3857 (Web Mercator), 32638-32642 (UTM Zones 38N-42N)

## Quick Start

### Prerequisites
- SQL Server 2012+ (recommended 2016+)
- `sysadmin` role or assembly permissions
- .NET Framework 4.8.1

### Installation

**Automated Installation:**
```sql
-- 1. Enable CLR
EXEC sp_configure 'show advanced options', 1; RECONFIGURE;
EXEC sp_configure 'clr enabled', 1; RECONFIGURE;

-- 2. Set database as trustworthy
ALTER DATABASE [YourDatabase] SET TRUSTWORTHY ON;
GO

-- 3. Create assembly (update path)
USE [YourDatabase];
GO

CREATE ASSEMBLY GisSqlServerCLR
FROM N'D:\GisSqlServerCLR.dll'
WITH PERMISSION_SET = UNSAFE;
GO

-- 4. Create functions
CREATE FUNCTION dbo.STTransform(@Geometry geometry, @dst int)
RETURNS geometry
AS EXTERNAL NAME [GisSqlServerCLR].[SpatialReprojection].TransformGeometry;
GO

CREATE FUNCTION dbo.STTransformWkt(@wkt NVARCHAR(MAX), @src int, @dst int)
RETURNS NVARCHAR(MAX)
AS EXTERNAL NAME [GisSqlServerCLR].[SpatialReprojection].TransformWktGeometry;
GO

CREATE AGGREGATE dbo.STExtent(@geometry GEOMETRY)
RETURNS NVARCHAR(MAX)
EXTERNAL NAME [GisSqlServerCLR].[SpatialExtent];
GO

-- 5. Test installation
SELECT dbo.STTransform(
    geometry::STGeomFromText('POINT (51.3890 35.6892)', 4326), 
    3857
).STAsText();
```

### Update Assembly

```sql
-- Drop functions
DROP FUNCTION IF EXISTS dbo.STTransform;
DROP FUNCTION IF EXISTS dbo.STTransformWkt;
DROP AGGREGATE IF EXISTS dbo.STExtent;
GO

-- Drop and recreate assembly
DROP ASSEMBLY IF EXISTS GisSqlServerCLR;
GO

CREATE ASSEMBLY GisSqlServerCLR
FROM N'D:\GisSqlServerCLR.dll'
WITH PERMISSION_SET = UNSAFE;
GO

-- Recreate functions (use code from installation section)
```

### Uninstall

```sql
-- Remove all components
DROP FUNCTION IF EXISTS dbo.STTransform;
DROP FUNCTION IF EXISTS dbo.STTransformWkt;
DROP AGGREGATE IF EXISTS dbo.STExtent;
DROP ASSEMBLY IF EXISTS GisSqlServerCLR;
GO

-- Optional: Disable CLR
-- EXEC sp_configure 'clr enabled', 0; RECONFIGURE;
```

## Usage Examples

```sql
-- Transform point WGS84 ? Web Mercator
SELECT dbo.STTransform(
    geometry::STGeomFromText('POINT (51.3890 35.6892)', 4326), 
    3857
).STAsText();

-- Transform WKT
SELECT dbo.STTransformWkt('POLYGON ((51 35, 52 35, 52 36, 51 36, 51 35))', 4326, 32639);

-- Calculate extent
SELECT dbo.STExtent(Location) FROM Locations;

-- Calculate area in meters
DECLARE @area GEOMETRY = geometry::STGeomFromText('POLYGON ((51 35, 51.01 35, 51.01 35.01, 51 35.01, 51 35))', 4326);
SELECT dbo.STTransform(@area, 32639).STArea() AS SquareMeters;

-- Batch transform
UPDATE Locations SET WebMercator = dbo.STTransform(Location, 3857) WHERE Location IS NOT NULL;
```

## Diagnostics

```sql
-- Check CLR status
SELECT name, value, value_in_use FROM sys.configurations WHERE name = 'clr enabled';

-- Check database trustworthy
SELECT name, is_trustworthy_on FROM sys.databases WHERE name = DB_NAME();

-- Check assembly
SELECT * FROM sys.assemblies WHERE name = 'GisSqlServerCLR';

-- Check functions
SELECT name, type_desc FROM sys.objects 
WHERE name IN ('STTransform', 'STTransformWkt', 'STExtent');

-- Run test
DECLARE @test GEOMETRY = geometry::STGeomFromText('POINT (51.3890 35.6892)', 4326);
DECLARE @result GEOMETRY = dbo.STTransform(@test, 3857);
SELECT 
    CASE WHEN @result IS NOT NULL THEN '? Working' ELSE '? Failed' END AS Status,
    @result.STAsText() AS Result;
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| CLR not enabled | `EXEC sp_configure 'clr enabled', 1; RECONFIGURE;` |
| Assembly not trusted | `ALTER DATABASE [YourDB] SET TRUSTWORTHY ON;` |
| Assembly not found | Check DLL path and SQL Server read permissions |
| Unsupported EPSG | Modify `GetProjectionFromEpsg` in `SpatialReprojection.cs` |
| Invalid geometry | Verify with `@geometry.STIsValid()` |

## Security Notes

- Uses `PERMISSION_SET = UNSAFE` (required for DotSpatial)
- Set `TRUSTWORTHY ON` only on trusted databases
