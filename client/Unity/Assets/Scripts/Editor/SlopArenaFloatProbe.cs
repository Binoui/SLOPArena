#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Unity.Pipeline.Commands;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Runtime float parity probe: the same seeded MathF and arithmetic results as tools/float_probe (run on .NET 8), one
    /// line per input with raw float bits, so a byte diff shows whether Unity's Mono computes floats like .NET does.
    /// </summary>
    public static class SlopArenaFloatProbe
    {
        private static uint _seed;
        private static float Next(float lo, float hi) { _seed = _seed * 1664525u + 1013904223u; return lo + (hi - lo) * ((_seed >> 8) / 16777216f); }
        private static int Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);

        [CliCommand("sloparena.diag.float_probe", "Write seeded MathF and float arithmetic results as raw bits, for runtime parity checks against .NET.", Tags = new[] { "diagnostics" })]
        public static object Run([CliArg("output", "Project-root-relative output file.", Required = true)] string output)
        {
            _seed = 12345;
            var text = new StringBuilder();
            for (int i = 0; i < 200000; i++)
            {
                float a = Next(-7f, 7f), b = Next(-7f, 7f), p = Next(0f, 30f), e = Next(-10f, 10f), u = Next(-1f, 1f);
                text.Append(Bits(MathF.Sin(a))).Append(' ').Append(Bits(MathF.Cos(a))).Append(' ').Append(Bits(MathF.Atan2(a, b))).Append(' ')
                    .Append(Bits(MathF.Sqrt(p))).Append(' ').Append(Bits(MathF.Exp(e))).Append(' ').Append(Bits(MathF.Pow(p, u))).Append(' ')
                    .Append(Bits(MathF.Acos(u))).Append(' ').Append(Bits(MathF.Asin(u))).Append(' ').Append(Bits(MathF.Atan(a))).Append(' ')
                    .Append(Bits(MathF.Tan(u))).Append(' ').Append(Bits(MathF.Log(p + 0.001f))).Append(' ').Append(Bits(a * b + p)).Append('\n');
            }
            string path = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", output);
            File.WriteAllText(path, text.ToString());
            return new { output, lines = 200000 };
        }
    }
}
#endif
