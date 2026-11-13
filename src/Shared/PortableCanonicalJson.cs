using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>Exact IEEE-754 decimals avoid runtime-dependent shortest float formatting.</summary>
internal static class PortableCanonicalJson
{
    internal static byte[] Normalize(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(doc.RootElement, writer);
        return stream.ToArray();
    }

    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var p in value.EnumerateObject()) { writer.WritePropertyName(p.Name); Write(p.Value, writer); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var x in value.EnumerateArray()) Write(x, writer); writer.WriteEndArray(); break;
            case JsonValueKind.Number:
                if (value.TryGetInt32(out var integer)) writer.WriteNumberValue(integer);
                else writer.WriteRawValue(FloatDecimal(PortableDecimal.Parse(value.GetRawText())));
                break;
            default: value.WriteTo(writer); break;
        }
    }

    internal static string FloatDecimal(float value)
    {
        int bits = BitConverter.SingleToInt32Bits(value);
        int exponent = (bits >> 23) & 255;
        if (exponent == 255) throw new ArgumentException("Finite float required.");
        BigInteger significand = bits & 0x7fffff;
        if (exponent != 0) significand += 1 << 23;
        if (significand.IsZero) return "0";
        int shift = exponent == 0 ? -149 : exponent - 150;
        string result;
        if (shift >= 0) result = (significand << shift).ToString(CultureInfo.InvariantCulture);
        else
        {
            int places = -shift;
            string digits = (significand * BigInteger.Pow(5, places)).ToString(CultureInfo.InvariantCulture).PadLeft(places + 1, '0');
            result = (digits.Substring(0, digits.Length - places) + "." + digits.Substring(digits.Length - places)).TrimEnd('0').TrimEnd('.');
        }
        return bits < 0 ? "-" + result : result;
    }
}
