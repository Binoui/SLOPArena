using System;
using System.Globalization;
using System.Numerics;

namespace SlopArena.Shared;

/// <summary>Decimal rational to binary32, rounded once, nearest with ties to even.</summary>
internal static class PortableDecimal
{
    internal static float Parse(string token)
    {
        if (token.Length > 512) throw new FormatException("Portable decimal exceeds 512 characters.");
        bool negative = token[0] == '-';
        string magnitude = negative ? token.Substring(1) : token;
        int e = magnitude.IndexOfAny(new[] { 'e', 'E' });
        int exponent = e < 0 ? 0 : int.Parse(magnitude.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string digits = e < 0 ? magnitude : magnitude.Substring(0, e);
        int point = digits.IndexOf('.');
        if (point >= 0) { exponent -= digits.Length - point - 1; digits = digits.Remove(point, 1); }
        var numerator = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        if (numerator.IsZero) return 0f;
        if (exponent > 512) throw new FormatException("Portable decimal overflows binary32.");
        if (exponent < -1024) return 0f;
        BigInteger denominator = BigInteger.One;
        if (exponent >= 0) numerator *= BigInteger.Pow(10, exponent);
        else denominator = BigInteger.Pow(10, -exponent);
        // Every finite binary32 is an integer multiple of 2^-149.
        BigInteger scaled = numerator << 149;
        BigInteger Units(int bits)
        {
            int power = bits >> 23;
            BigInteger significand = bits & 0x7fffff;
            if (power == 0) return significand;
            return (significand + (1 << 23)) << (power - 1);
        }
        int low = 0, high = 0x7f800000;
        while (high - low > 1)
        {
            int mid = low + (high - low) / 2;
            if (Units(mid) * denominator <= scaled) low = mid; else high = mid;
        }
        BigInteger lower = Units(low) * denominator;
        BigInteger upper = Units(high) * denominator;
        int cmp = (scaled * 2).CompareTo(lower + upper);
        int chosen = cmp < 0 || cmp == 0 && (low & 1) == 0 ? low : high;
        if (chosen == 0x7f800000) throw new FormatException("Portable decimal overflows binary32.");
        return chosen == 0 ? 0f : BitConverter.Int32BitsToSingle(negative ? chosen | int.MinValue : chosen);
    }
}
