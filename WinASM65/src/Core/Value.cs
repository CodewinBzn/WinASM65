// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Core Value representation

using System;
using System.Globalization;

namespace WinASM65.Core
{
    public enum ValueKind
    {
        Integer,
        Boolean
    }

    public struct Value : IEquatable<Value>
    {
        public ValueKind Kind { get; private set; }
        public long AsInteger { get; private set; }
        public bool AsBoolean { get; private set; }

        public Value(long intValue)
            : this()
        {
            Kind = ValueKind.Integer;
            AsInteger = intValue;
            AsBoolean = intValue != 0;
        }

        public Value(bool boolValue)
            : this()
        {
            Kind = ValueKind.Boolean;
            AsBoolean = boolValue;
            AsInteger = boolValue ? 1 : 0;
        }

        public static Value FromInteger(long value)
        {
            return new Value(value);
        }

        public static Value FromBoolean(bool value)
        {
            return new Value(value);
        }

        public bool IsInteger
        {
            get { return Kind == ValueKind.Integer; }
        }

        public bool IsBoolean
        {
            get { return Kind == ValueKind.Boolean; }
        }

        public byte ToByte()
        {
            return (byte)(AsInteger & 0xFF);
        }

        public ushort ToUInt16()
        {
            return (ushort)(AsInteger & 0xFFFF);
        }

        public int ToInt32()
        {
            return (int)AsInteger;
        }

        public long ToInt64()
        {
            return AsInteger;
        }

        public bool ToBoolean()
        {
            return AsBoolean;
        }

        /// <summary>
        /// True when the integer fits a one-byte operand (-128..255).
        /// Signed bounds on the low side so LDA #-1 still encodes as $FF.
        /// </summary>
        public static bool InByteRange(long value)
        {
            return value >= -128 && value <= 255;
        }

        /// <summary>
        /// True when the integer fits a two-byte operand (-32768..65535).
        /// </summary>
        public static bool InWordRange(long value)
        {
            return value >= -32768 && value <= 65535;
        }

        #region Operators

        public static implicit operator Value(long v)
        {
            return new Value(v);
        }

        public static implicit operator Value(int v)
        {
            return new Value((long)v);
        }

        public static implicit operator Value(ushort v)
        {
            return new Value((long)v);
        }

        public static implicit operator Value(byte v)
        {
            return new Value((long)v);
        }

        public static implicit operator Value(bool v)
        {
            return new Value(v);
        }

        public static explicit operator long(Value v)
        {
            return v.AsInteger;
        }

        public static explicit operator int(Value v)
        {
            return (int)v.AsInteger;
        }

        public static explicit operator ushort(Value v)
        {
            return (ushort)v.AsInteger;
        }

        public static explicit operator byte(Value v)
        {
            return (byte)v.AsInteger;
        }

        public static explicit operator bool(Value v)
        {
            return v.AsBoolean;
        }

        public static Value operator +(Value a, Value b)
        {
            return new Value(a.AsInteger + b.AsInteger);
        }

        public static Value operator -(Value a, Value b)
        {
            return new Value(a.AsInteger - b.AsInteger);
        }

        public static Value operator *(Value a, Value b)
        {
            return new Value(a.AsInteger * b.AsInteger);
        }

        public static Value operator /(Value a, Value b)
        {
            if (b.AsInteger == 0)
                throw new DivideByZeroException("Division by zero in expression evaluation.");
            return new Value(a.AsInteger / b.AsInteger);
        }

        public static Value operator %(Value a, Value b)
        {
            if (b.AsInteger == 0)
                throw new DivideByZeroException("Modulo by zero in expression evaluation.");
            return new Value(a.AsInteger % b.AsInteger);
        }

        public static Value operator &(Value a, Value b)
        {
            if (a.Kind == ValueKind.Boolean && b.Kind == ValueKind.Boolean)
                return new Value(a.AsBoolean & b.AsBoolean);
            return new Value(a.AsInteger & b.AsInteger);
        }

        public static Value operator |(Value a, Value b)
        {
            if (a.Kind == ValueKind.Boolean && b.Kind == ValueKind.Boolean)
                return new Value(a.AsBoolean | b.AsBoolean);
            return new Value(a.AsInteger | b.AsInteger);
        }

        public static Value operator ^(Value a, Value b)
        {
            if (a.Kind == ValueKind.Boolean && b.Kind == ValueKind.Boolean)
                return new Value(a.AsBoolean ^ b.AsBoolean);
            return new Value(a.AsInteger ^ b.AsInteger);
        }

        public static Value operator ~(Value a)
        {
            return new Value(~a.AsInteger);
        }

        public static Value operator +(Value a)
        {
            return new Value(+a.AsInteger);
        }

        public static Value operator -(Value a)
        {
            return new Value(-a.AsInteger);
        }

        public static Value operator !(Value a)
        {
            return new Value(!a.AsBoolean);
        }

        public static Value operator <<(Value a, int shift)
        {
            return new Value(a.AsInteger << shift);
        }

        public static Value operator >>(Value a, int shift)
        {
            return new Value(a.AsInteger >> shift);
        }

        public static Value LogicalAnd(Value a, Value b)
        {
            return new Value(a.AsBoolean && b.AsBoolean);
        }

        public static Value LogicalOr(Value a, Value b)
        {
            return new Value(a.AsBoolean || b.AsBoolean);
        }

        public static Value BitwiseShiftLeft(Value a, Value b)
        {
            return new Value(a.AsInteger << (int)b.AsInteger);
        }

        public static Value BitwiseShiftRight(Value a, Value b)
        {
            return new Value(a.AsInteger >> (int)b.AsInteger);
        }

        public static Value LessThan(Value a, Value b)
        {
            return new Value(a.AsInteger < b.AsInteger);
        }

        public static Value GreaterThan(Value a, Value b)
        {
            return new Value(a.AsInteger > b.AsInteger);
        }

        public static Value LessThanOrEqual(Value a, Value b)
        {
            return new Value(a.AsInteger <= b.AsInteger);
        }

        public static Value GreaterThanOrEqual(Value a, Value b)
        {
            return new Value(a.AsInteger >= b.AsInteger);
        }

        public static Value Equal(Value a, Value b)
        {
            if (a.Kind == ValueKind.Boolean || b.Kind == ValueKind.Boolean)
                return new Value(a.AsBoolean == b.AsBoolean);
            return new Value(a.AsInteger == b.AsInteger);
        }

        public static Value NotEqual(Value a, Value b)
        {
            if (a.Kind == ValueKind.Boolean || b.Kind == ValueKind.Boolean)
                return new Value(a.AsBoolean != b.AsBoolean);
            return new Value(a.AsInteger != b.AsInteger);
        }

        public static bool operator ==(Value a, Value b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Value a, Value b)
        {
            return !a.Equals(b);
        }

        #endregion

        public bool Equals(Value other)
        {
            if (Kind != other.Kind)
                return false;
            return Kind == ValueKind.Boolean
                ? AsBoolean == other.AsBoolean
                : AsInteger == other.AsInteger;
        }

        public override bool Equals(object obj)
        {
            if (obj is Value)
                return Equals((Value)obj);
            return false;
        }

        public override int GetHashCode()
        {
            return Kind == ValueKind.Boolean
                ? AsBoolean.GetHashCode()
                : AsInteger.GetHashCode();
        }

        public override string ToString()
        {
            return Kind == ValueKind.Boolean
                ? (AsBoolean ? "true" : "false")
                : AsInteger.ToString(CultureInfo.InvariantCulture);
        }
    }
}
