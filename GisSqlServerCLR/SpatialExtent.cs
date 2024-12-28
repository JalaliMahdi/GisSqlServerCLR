using System;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using Microsoft.SqlServer.Types;

[Serializable]
[SqlUserDefinedAggregate(
    Format.UserDefined,
    IsInvariantToNulls = true,
    IsInvariantToDuplicates = false,
    IsInvariantToOrder = true,
    MaxByteSize = 8000)]
public class SpatialExtent : IBinarySerialize
{
    private double _minX;
    private double _minY;
    private double _maxX;
    private double _maxY;
    private bool _hasValues;


    public void Init()
    {
        _minX = double.MaxValue;
        _minY = double.MaxValue;
        _maxX = double.MinValue;
        _maxY = double.MinValue;
        _hasValues = false;
    }

    public void Accumulate(SqlGeometry geometry)
    {
        if (geometry == null || geometry.IsNull)
            return;

        _minX = Math.Min(_minX, geometry.STEnvelope().STPointN(1).STX.Value);
        _minY = Math.Min(_minY, geometry.STEnvelope().STPointN(1).STY.Value);
        _maxX = Math.Max(_maxX, geometry.STEnvelope().STPointN(3).STX.Value);
        _maxY = Math.Max(_maxY, geometry.STEnvelope().STPointN(3).STY.Value);

        _hasValues = true;
    }

    public void Merge(SpatialExtent group)
    {
        if (group._hasValues)
        {
            _minX = Math.Min(_minX, group._minX);
            _minY = Math.Min(_minY, group._minY);
            _maxX = Math.Max(_maxX, group._maxX);
            _maxY = Math.Max(_maxY, group._maxY);
            _hasValues = true;
        }
    }

    public SqlString Terminate()
    {

        return new SqlString($"BOX({_minX} {_minY}, {_maxX} {_maxY})");
    }

    public void Read(System.IO.BinaryReader r)
    {
        _minX = r.ReadDouble();
        _minY = r.ReadDouble();
        _maxX = r.ReadDouble();
        _maxY = r.ReadDouble();
        _hasValues = r.ReadBoolean();
    }

    public void Write(System.IO.BinaryWriter w)
    {
        w.Write(_minX);
        w.Write(_minY);
        w.Write(_maxX);
        w.Write(_maxY);
        w.Write(_hasValues);
    }
}