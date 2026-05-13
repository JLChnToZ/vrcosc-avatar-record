using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit, Size = 8)]
public readonly struct Primitive32 : IFormattable, IConvertible, IEquatable<Primitive32>, IComparable<Primitive32> {
    [FieldOffset(0)] private readonly int intValue;
    [FieldOffset(0)] private readonly float floatValue;
    [FieldOffset(4)] private readonly TypeCode type;

    public static Primitive32 Null => default;

    public bool IsValid => type != default;

    public static bool IsTypeCodeSupported(TypeCode typeCode) =>
        typeCode >= TypeCode.Boolean && typeCode <= TypeCode.Single;

    public static bool TryCreate(object? rawValue, out Primitive32 value) {
        switch (Convert.GetTypeCode(rawValue)) {
            case TypeCode.Boolean:
                value = new Primitive32(Convert.ToBoolean(rawValue));
                return true;
            case TypeCode.Byte:
                value = new Primitive32(Convert.ToByte(rawValue));
                return true;
            case TypeCode.SByte:
                value = new Primitive32(Convert.ToSByte(rawValue));
                return true;
            case TypeCode.Int16:
                value = new Primitive32(Convert.ToInt16(rawValue));
                return true;
            case TypeCode.UInt16:
            case TypeCode.Char:
                value = new Primitive32(Convert.ToUInt16(rawValue));
                return true;
            case TypeCode.Int32:
                value = new Primitive32(Convert.ToInt32(rawValue));
                return true;
            case TypeCode.UInt32:
                value = new Primitive32(Convert.ToUInt32(rawValue));
                return true;
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
            case TypeCode.Int64:
            case TypeCode.UInt64:
                value = new Primitive32(Convert.ToSingle(rawValue));
                return true;
            default:
                value = default;
                return false;
        }
    }

    private Primitive32(bool boolValue) {
        floatValue = default;
        intValue = boolValue ? 1 : 0;
        type = TypeCode.Boolean;
    }

    private Primitive32(byte byteValue) {
        floatValue = default;
        intValue = byteValue;
        type = TypeCode.Byte;
    }

    private Primitive32(sbyte sbyteValue) {
        floatValue = default;
        intValue = sbyteValue;
        type = TypeCode.SByte;
    }

    private Primitive32(short int16Value) {
        floatValue = default;
        intValue = int16Value;
        type = TypeCode.Int16;
    }

    private Primitive32(ushort uint16Value) {
        floatValue = default;
        intValue = uint16Value;
        type = TypeCode.UInt16;
    }

    private Primitive32(int int32Value) {
        floatValue = default;
        intValue = int32Value;
        type = TypeCode.Int32;
    }

    private Primitive32(uint uint32Value) {
        floatValue = default;
        intValue = (int)uint32Value;
        type = TypeCode.UInt32;
    }

    private Primitive32(float floatValue) {
        intValue = default;
        this.floatValue = floatValue;
        type = TypeCode.Single;
    }

    public object? Unwrap() => type switch {
        TypeCode.Boolean => intValue != 0,
        TypeCode.Byte => unchecked((byte)intValue),
        TypeCode.SByte => unchecked((sbyte)intValue),
        TypeCode.Int16 => unchecked((short)intValue),
        TypeCode.UInt16 => unchecked((ushort)intValue),
        TypeCode.Int32 => intValue,
        TypeCode.UInt32 => unchecked((uint)intValue),
        TypeCode.Single => floatValue,
        _ => null,
    };

    public override string ToString() => type switch {
        TypeCode.Boolean => (intValue != 0).ToString(),
        TypeCode.Byte => intValue.ToString(),
        TypeCode.SByte => intValue.ToString(),
        TypeCode.Int16 => intValue.ToString(),
        TypeCode.UInt16 => intValue.ToString(),
        TypeCode.Int32 => intValue.ToString(),
        TypeCode.UInt32 => ((uint)intValue).ToString(),
        TypeCode.Single => floatValue.ToString(CultureInfo.InvariantCulture),
        _ => "null",
    };

    public string ToString(IFormatProvider? provider) => type switch {
        TypeCode.Boolean => (intValue != 0).ToString(provider),
        TypeCode.Byte => intValue.ToString(provider),
        TypeCode.SByte => intValue.ToString(provider),
        TypeCode.Int16 => intValue.ToString(provider),
        TypeCode.UInt16 => intValue.ToString(provider),
        TypeCode.Int32 => intValue.ToString(provider),
        TypeCode.UInt32 => ((uint)intValue).ToString(provider),
        TypeCode.Single => floatValue.ToString(provider),
        _ => "null",
    };

    public string ToString(string? format, IFormatProvider? formatProvider = null) => type switch {
        TypeCode.Boolean => (intValue != 0).ToString(formatProvider),
        TypeCode.Byte => intValue.ToString(format, formatProvider),
        TypeCode.SByte => intValue.ToString(format, formatProvider),
        TypeCode.Int16 => intValue.ToString(format, formatProvider),
        TypeCode.UInt16 => intValue.ToString(format, formatProvider),
        TypeCode.Int32 => intValue.ToString(format, formatProvider),
        TypeCode.UInt32 => ((uint)intValue).ToString(format, formatProvider),
        TypeCode.Single => floatValue.ToString(format, formatProvider),
        _ => "null",
    };

    public TypeCode GetTypeCode() => type;

    bool IConvertible.ToBoolean(IFormatProvider? provider) => this;

    byte IConvertible.ToByte(IFormatProvider? provider) => this;

    sbyte IConvertible.ToSByte(IFormatProvider? provider) => this;

    short IConvertible.ToInt16(IFormatProvider? provider) => this;

    ushort IConvertible.ToUInt16(IFormatProvider? provider) => this;

    int IConvertible.ToInt32(IFormatProvider? provider) => this;

    uint IConvertible.ToUInt32(IFormatProvider? provider) => this;

    long IConvertible.ToInt64(IFormatProvider? provider) => unchecked(type switch {
        TypeCode.Boolean => intValue,
        TypeCode.Byte => intValue,
        TypeCode.SByte => intValue,
        TypeCode.Int16 => intValue,
        TypeCode.UInt16 => intValue,
        TypeCode.Int32 => intValue,
        TypeCode.UInt32 => (uint)intValue,
        TypeCode.Single => (long)floatValue,
        _ => 0L,
    });

    ulong IConvertible.ToUInt64(IFormatProvider? provider) => unchecked(type switch {
        TypeCode.Boolean => (ulong)intValue,
        TypeCode.Byte => (ulong)intValue,
        TypeCode.SByte => (ulong)intValue,
        TypeCode.Int16 => (ulong)intValue,
        TypeCode.UInt16 => (ulong)intValue,
        TypeCode.Int32 => (ulong)intValue,
        TypeCode.UInt32 => (uint)intValue,
        TypeCode.Single => (ulong)floatValue,
        _ => 0UL,
    });

    char IConvertible.ToChar(IFormatProvider? provider) => (char)(ushort)this;

    DateTime IConvertible.ToDateTime(IFormatProvider? provider) =>
        throw new InvalidCastException("Value32 cannot be converted to DateTime.");

    decimal IConvertible.ToDecimal(IFormatProvider? provider) => type switch {
        TypeCode.Boolean => intValue,
        TypeCode.Byte => intValue,
        TypeCode.SByte => intValue,
        TypeCode.Int16 => intValue,
        TypeCode.UInt16 => intValue,
        TypeCode.Int32 => intValue,
        TypeCode.UInt32 => (uint)intValue,
        TypeCode.Single => (decimal)floatValue,
        _ => 0m,
    };

    float IConvertible.ToSingle(IFormatProvider? provider) => this;

    double IConvertible.ToDouble(IFormatProvider? provider) => this;

    public object ToType(Type conversionType, IFormatProvider? provider = null) =>
        Convert.ChangeType(Unwrap(), conversionType, provider)!;

    public int CompareTo(Primitive32 other) => (type > other.type ? type : other.type) switch {
        TypeCode.Boolean => ((bool)this).CompareTo((bool)other),
        TypeCode.Byte => ((byte)this).CompareTo((byte)other),
        TypeCode.SByte => ((sbyte)this).CompareTo((sbyte)other),
        TypeCode.Int16 => ((short)this).CompareTo((short)other),
        TypeCode.UInt16 => ((ushort)this).CompareTo((ushort)other),
        TypeCode.Int32 => ((int)this).CompareTo((int)other),
        TypeCode.UInt32 => ((uint)this).CompareTo((uint)other),
        TypeCode.Single => ((float)this).CompareTo((float)other),
        _ => 0,
    };

    public bool Equals(Primitive32 other) => (type > other.type ? type : other.type) switch {
        TypeCode.Boolean => (bool)this == (bool)other,
        TypeCode.Byte => (byte)this == (byte)other,
        TypeCode.SByte => (sbyte)this == (sbyte)other,
        TypeCode.Int16 => (short)this == (short)other,
        TypeCode.UInt16 => (ushort)this == (ushort)other,
        TypeCode.Int32 => (int)this == (int)other,
        TypeCode.UInt32 => (uint)this == (uint)other,
        TypeCode.Single => (float)this == (float)other,
        _ => true,
    };

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Primitive32 other && Equals(other);

    public override int GetHashCode() => intValue;

    public static implicit operator Primitive32(bool value) => new Primitive32(value);
    public static implicit operator Primitive32(byte value) => new Primitive32(value);
    public static implicit operator Primitive32(sbyte value) => new Primitive32(value);
    public static implicit operator Primitive32(short value) => new Primitive32(value);
    public static implicit operator Primitive32(ushort value) => new Primitive32(value);
    public static implicit operator Primitive32(int value) => new Primitive32(value);
    public static implicit operator Primitive32(uint value) => new Primitive32(value);
    public static implicit operator Primitive32(float value) => new Primitive32(value);

    public static implicit operator Primitive32(bool? value) => value is bool v ? new Primitive32(v) : default;
    public static implicit operator Primitive32(byte? value) => value is byte v ? new Primitive32(v) : default;
    public static implicit operator Primitive32(sbyte? value) => value is sbyte v ? new Primitive32(v) : default;
    public static implicit operator Primitive32(short? value) => value is short v ? new Primitive32(v) : default;
    public static implicit operator Primitive32(ushort? value) => value is ushort v ? new Primitive32(v) : default;
    public static implicit operator Primitive32(int? value) => value is int v ? new Primitive32(v) : default;
    public static implicit operator Primitive32(uint? value) => value is uint v ? new Primitive32(v) : default;
    public static implicit operator Primitive32(float? value) => value is float v ? new Primitive32(v) : default;

    public static implicit operator bool(Primitive32 value) => value.type == TypeCode.Single ?
        !float.IsNaN(value.floatValue) && value.floatValue != 0f : value.intValue != 0;

    public static implicit operator byte(Primitive32 value) => unchecked(value.type == TypeCode.Single ?
        (byte)value.floatValue : (byte)value.intValue);

    public static implicit operator sbyte(Primitive32 value) => unchecked(value.type == TypeCode.Single ?
        (sbyte)value.floatValue : (sbyte)value.intValue);

    public static implicit operator short(Primitive32 value) => unchecked(value.type == TypeCode.Single ?
        (short)value.floatValue : (short)value.intValue);

    public static implicit operator ushort(Primitive32 value) => unchecked(value.type == TypeCode.Single ?
        (ushort)value.floatValue : (ushort)value.intValue);

    public static implicit operator int(Primitive32 value) => unchecked(value.type == TypeCode.Single ?
        (int)value.floatValue : value.intValue);
    
    public static implicit operator uint(Primitive32 value) => unchecked(value.type == TypeCode.Single ?
        (uint)value.floatValue : (uint)value.intValue);
    
    public static implicit operator float(Primitive32 value) => value.type == TypeCode.Single ?
        value.floatValue : value.intValue;

    public static implicit operator bool?(Primitive32 value) => IsTypeCodeSupported(value.type) ? new bool?((bool)value) : null;
    public static implicit operator byte?(Primitive32 value) => IsTypeCodeSupported(value.type) ? new byte?((byte)value) : null;
    public static implicit operator sbyte?(Primitive32 value) => IsTypeCodeSupported(value.type) ? new sbyte?((sbyte)value) : null;
    public static implicit operator short?(Primitive32 value) => IsTypeCodeSupported(value.type) ? new short?((short)value) : null;
    public static implicit operator ushort?(Primitive32 value) => IsTypeCodeSupported(value.type) ? new ushort?((ushort)value) : null;
    public static implicit operator int?(Primitive32 value) => IsTypeCodeSupported(value.type) ? new int?((int)value) : null;
    public static implicit operator uint?(Primitive32 value) => IsTypeCodeSupported(value.type) ? new uint?((uint)value) : null;
    public static implicit operator float?(Primitive32 value) => IsTypeCodeSupported(value.type) ? new float?((float)value) : null;

    public static bool operator ==(Primitive32 left, Primitive32 right) => left.Equals(right);
    public static bool operator !=(Primitive32 left, Primitive32 right) => !left.Equals(right);
    public static bool operator <(Primitive32 left, Primitive32 right) => left.CompareTo(right) < 0;
    public static bool operator >(Primitive32 left, Primitive32 right) => left.CompareTo(right) > 0;
    public static bool operator <=(Primitive32 left, Primitive32 right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Primitive32 left, Primitive32 right) => left.CompareTo(right) >= 0;
}

