using System;
using System.Globalization;
using System.IO;

namespace MarblesECS.Editor
{
    internal static class BalanceValueParser
    {
        internal static object Parse(string value, Type type, string address)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException(address + ": value is required.");
            value = value.Trim();
            try
            {
                if (type == typeof(string)) return value;
                if (type == typeof(bool))
                {
                    if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                    throw new FormatException("Use TRUE/FALSE or 1/0.");
                }
                if (type == typeof(float) || type == typeof(double))
                {
                    double number = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (double.IsNaN(number) || double.IsInfinity(number) || (type == typeof(float) && Math.Abs(number) > float.MaxValue))
                        throw new FormatException("A finite number is required.");
                    if (type == typeof(float)) return (float)number;
                    return number;
                }
                decimal integer = decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (integer != decimal.Truncate(integer)) throw new FormatException("An integer is required.");
                if (type == typeof(int)) return checked((int)integer);
                if (type == typeof(uint)) return checked((uint)integer);
                if (type == typeof(long)) return checked((long)integer);
                throw new NotSupportedException("Unsupported balance field type: " + type.Name);
            }
            catch (Exception error) when (error is FormatException || error is OverflowException)
            {
                throw new InvalidDataException(address + ": invalid " + type.Name + " value '" + value + "'.", error);
            }
        }
    }
}
