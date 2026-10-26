using System;
using System.Globalization;
using BreakInfinity;

namespace IdleHands.UnityIntegration
{
    public sealed class ResourceNumberFormatter
    {
        private static readonly string[] Suffixes = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No" };
        private readonly ResourceNumberFormat format;
        private readonly int decimals;
        private readonly bool grouping;

        public ResourceNumberFormatter(ResourceNumberFormat format, int decimals, bool grouping)
        {
            if (!Enum.IsDefined(typeof(ResourceNumberFormat), format)) throw new ArgumentOutOfRangeException(nameof(format));
            if (decimals < 0 || decimals > 12) throw new ArgumentOutOfRangeException(nameof(decimals));
            if (grouping && format != ResourceNumberFormat.Fixed && format != ResourceNumberFormat.Compact)
                throw new ArgumentException("Grouping is supported only for Fixed and Compact formats.");
            this.format = format;
            this.decimals = decimals;
            this.grouping = grouping;
        }

        public string Format(BigDouble value)
        {
            if (format == ResourceNumberFormat.Scientific) return value.ToString("E" + decimals, CultureInfo.InvariantCulture);
            if (format == ResourceNumberFormat.Fixed) return Fixed(value);
            if (value == BigDouble.Zero) return Fixed(value);
            if (format == ResourceNumberFormat.Compact)
            {
                if (value.Exponent >= -6 && value.Exponent < 3) return Fixed(value);
                if (value.Exponent < 0) return value.ToString("E" + decimals, CultureInfo.InvariantCulture);
            }
            long remainder = value.Exponent % 3;
            if (remainder < 0) remainder += 3;
            if (value.Exponent < long.MinValue + remainder)
                return value.ToString("E" + decimals, CultureInfo.InvariantCulture);
            long exponent = value.Exponent - remainder;
            long group = exponent / 3;
            double mantissa = value.Mantissa * Math.Pow(10, value.Exponent - exponent);
            if (Math.Abs(Math.Round(mantissa, decimals)) >= 1000 && exponent <= long.MaxValue - 3)
            { mantissa /= 1000; group++; exponent += 3; }
            string digits = mantissa.ToString("F" + decimals, CultureInfo.InvariantCulture);
            if (format == ResourceNumberFormat.Compact && group >= 0 && group < Suffixes.Length)
                return digits + Suffixes[(int)group];
            return digits + "e" + exponent.ToString(CultureInfo.InvariantCulture);
        }

        private string Fixed(BigDouble value)
        {
            if (value != BigDouble.Zero && (value.Exponent < -6 || value.Exponent > 15))
                return value.ToString("E" + decimals, CultureInfo.InvariantCulture);
            return value.ToDouble().ToString((grouping ? "N" : "F") + decimals, CultureInfo.InvariantCulture);
        }
    }
}
