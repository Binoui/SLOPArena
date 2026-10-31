using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlopArena.Shared;

internal sealed class HitReliabilitySingleConverter : JsonConverter<float>
{
    public override float Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetSingle();
    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
        => writer.WriteRawValue(HitReliabilityJsonNumbers.Format(value, true));
}

internal sealed class HitReliabilityDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDouble();
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        => writer.WriteRawValue(HitReliabilityJsonNumbers.Format(value, false));
}

internal static class HitReliabilityJsonNumbers
{
    // Mono's floating-point formatting and parsing cannot reliably select .NET's shortest decimal.
    internal static string Format(double value, bool single)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new JsonException("Non-finite reliability value.");
        long signedBits = BitConverter.DoubleToInt64Bits(value);
        string sign = signedBits < 0 ? "-" : "";
        if (value == 0) return sign + "0";
        int mantissaBits = single ? 23 : 52;
        ulong bits = single ? (uint)BitConverter.ToInt32(BitConverter.GetBytes((float)value), 0) : (ulong)signedBits;
        int encodedExponent = (int)((bits >> mantissaBits) & (single ? 255UL : 2047UL));
        ulong mantissa = bits & ((1UL << mantissaBits) - 1);
        if (encodedExponent != 0) mantissa |= 1UL << mantissaBits;
        int power = (encodedExponent == 0 ? 1 : encodedExponent) - (single ? 127 : 1023) - mantissaBits;
        BigInteger numerator = mantissa;
        BigInteger denominator = BigInteger.One;
        if (power >= 0) numerator <<= power;
        else denominator <<= -power;
        var culture = CultureInfo.InvariantCulture;
        int exponent = numerator.ToString(culture).Length - denominator.ToString(culture).Length;
        bool belowPower = exponent >= 0
            ? numerator < denominator * BigInteger.Pow(10, exponent)
            : numerator * BigInteger.Pow(10, -exponent) < denominator;
        if (belowPower) exponent--;

        // At a normal power of two the lower half-ULP is half as wide. Even mantissas own ties.
        BigInteger lower = ((BigInteger)mantissa << 2) -
            (encodedExponent > 1 && mantissa == (1UL << mantissaBits) ? 1 : 2);
        BigInteger upper = ((BigInteger)mantissa << 2) + 2;
        BigInteger boundaryDenominator = BigInteger.One;
        if (power >= 2) { lower <<= power - 2; upper <<= power - 2; }
        else boundaryDenominator <<= 2 - power;
        bool inclusive = (mantissa & 1) == 0;
        int limit = single ? 9 : 17;
        for (int precision = 1; precision <= limit; precision++)
        {
            int scale = precision - 1 - exponent;
            BigInteger scaledNumerator = numerator;
            BigInteger scaledDenominator = denominator;
            if (scale >= 0) scaledNumerator *= BigInteger.Pow(10, scale);
            else scaledDenominator *= BigInteger.Pow(10, -scale);
            BigInteger rounded = BigInteger.DivRem(scaledNumerator, scaledDenominator, out var remainder);
            int halfway = (remainder << 1).CompareTo(scaledDenominator);
            if (halfway > 0 || halfway == 0 && !rounded.IsEven) rounded++;
            BigInteger candidateNumerator = rounded;
            BigInteger candidateDenominator = BigInteger.One;
            if (scale >= 0) candidateDenominator = BigInteger.Pow(10, scale);
            else candidateNumerator *= BigInteger.Pow(10, -scale);
            int low = (candidateNumerator * boundaryDenominator).CompareTo(lower * candidateDenominator);
            int high = (candidateNumerator * boundaryDenominator).CompareTo(upper * candidateDenominator);
            if ((low < 0 || high > 0 || !inclusive && (low == 0 || high == 0)) && precision < limit) continue;
            string digits = rounded.ToString(culture);
            int displayExponent = exponent + (digits.Length > precision ? 1 : 0);
            digits = digits.TrimEnd('0');
            if (displayExponent < -4 || displayExponent >= limit)
                return sign + digits[0] + (digits.Length > 1 ? "." + digits.Substring(1) : "") + "E" +
                    (displayExponent < 0 ? "-" : "+") + Math.Abs(displayExponent).ToString("D2", culture);
            int point = displayExponent + 1;
            if (point <= 0) return sign + "0." + new string('0', -point) + digits;
            if (point >= digits.Length) return sign + digits + new string('0', point - digits.Length);
            return sign + digits.Insert(point, ".");
        }
        throw new JsonException("Reliability number has no round-trip decimal.");
    }
}
