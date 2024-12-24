# SQL Server CLR Integration

This repository provides SQL scripts and instructions for enabling CLR (Common Language Runtime) integration in SQL Server. It also includes a custom SQL function for transforming geometries between different coordinate systems using a CLR assembly.

## Prerequisites

Before proceeding, ensure the following requirements are met:

- **SQL Server Version**: SQL Server 2012 or later
- **Database Access**: Sufficient permissions to execute SQL commands and modify database settings
- **CLR Assembly**: The corresponding DLL file for the assembly (`GisSqlServerCLR.dll`)

## Installation Guide

Follow the steps below to enable CLR integration and set up the geometry transformation function.

### 1. Enable Advanced Options in SQL Server

To enable CLR integration, execute the following SQL commands:

```sql
-- Enable advanced options
sp_configure 'show advanced options', 1;  
GO  
RECONFIGURE;  
GO  

-- Enable CLR integration
sp_configure 'clr enabled', 1;  
GO  
RECONFIGURE;  
```

### 2. Set the Database as Trustworthy

For the CLR assembly to work, you need to set the target database as trustworthy. Replace `[GPS_Tracking]` with the name of your database:

```sql
ALTER DATABASE [GPS_Tracking] SET TRUSTWORTHY ON;
GO
```

### 3. Create the CLR Assembly

Deploy the CLR assembly to your SQL Server instance. Update the path to the DLL file (`D:\GisSqlServerCLR.dll`) as needed:

```sql
CREATE ASSEMBLY GisSqlServerCLR
FROM N'D:\GisSqlServerCLR.dll'
WITH PERMISSION_SET = UNSAFE;
GO
```

### 4. Create the Geometry Transformation Function

Finally, create the custom SQL function for transforming geometries. This function takes a geometry object and a destination spatial reference ID (SRID) as input and returns the transformed geometry:

```sql
CREATE FUNCTION dbo.STTransform(@Geometry geometry, @dst int)
RETURNS geometry
AS
EXTERNAL NAME [GisSqlServerCLR].[GisSqlServerCLR.SpatialReprojection].TransformGeometry;
GO
```

## Usage

Once the setup is complete, you can use the `dbo.STTransform` function in your SQL queries to transform geometries. For example:

```sql
SELECT dbo.STTransform(geometry::STGeomFromText('POINT (30 10)', 4326), 3857);
```

This query transforms a geometry from SRID 4326 (WGS 84) to SRID 3857 (Web Mercator).

## Notes

- Ensure that the DLL file (`GisSqlServerCLR.dll`) is accessible by the SQL Server instance.
- The database must remain trustworthy for the CLR assembly to function properly.
- Use `PERMISSION_SET = UNSAFE` cautiously, as it grants elevated permissions to the assembly.

## License

This project is released with no restrictions. You are free to use, modify, and distribute it as you see fit.
