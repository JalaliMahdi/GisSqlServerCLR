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
        {
            return;
        }

        if (geometry.STIsEmpty().IsTrue)
        {
            return;
        }


        if (geometry.STIsValid().IsFalse)
        {
            return;
        }

        var envelope = geometry.STEnvelope();
        
        if (envelope.IsNull || envelope.STIsEmpty().IsTrue)
        {
            return;
        }

        var numPoints = envelope.STNumPoints();
        if (numPoints.IsNull || numPoints.Value < 3)
        {
            return;
        }

        var point1 = envelope.STPointN(1);
        var point3 = envelope.STPointN(3);
        
        if (point1 != null && !point1.IsNull && point3 != null && !point3.IsNull)
        {
            var x1 = point1.STX;
            var y1 = point1.STY;
            var x3 = point3.STX;
            var y3 = point3.STY;

            if (x1.IsNull || y1.IsNull || x3.IsNull || y3.IsNull)
            {
                return;
            }

            double minX = x1.Value;
            double minY = y1.Value;
            double maxX = x3.Value;
            double maxY = y3.Value;
            
            if (!_hasValues)
            {
                _minX = minX;
                _minY = minY;
                _maxX = maxX;
                _maxY = maxY;
                _hasValues = true;
            }
            else
            {
                _minX = Math.Min(_minX, minX);
                _minY = Math.Min(_minY, minY);
                _maxX = Math.Max(_maxX, maxX);
                _maxY = Math.Max(_maxY, maxY);
            }
        }
    }

    public void Merge(SpatialExtent group)
    {
        if (group._hasValues)
        {
            if (!_hasValues)
            {
                _minX = group._minX;
                _minY = group._minY;
                _maxX = group._maxX;
                _maxY = group._maxY;
                _hasValues = true;
            }
            else
            {
                _minX = Math.Min(_minX, group._minX);
                _minY = Math.Min(_minY, group._minY);
                _maxX = Math.Max(_maxX, group._maxX);
                _maxY = Math.Max(_maxY, group._maxY);
            }
        }
    }

    public SqlString Terminate()
    {
        if (!_hasValues)
        {
            return SqlString.Null;
        }
        
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