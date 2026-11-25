using System;
using System.Globalization;

namespace MarblesECS
{
    internal static class FloatingTextFormatter
    {
        private static readonly string[] Fixed = { "0", "0.#", "0.##", "0.###", "0.####", "0.#####", "0.######" };
        private static readonly string[] Scientific = { "0e0", "0.#e0", "0.##e0", "0.###e0", "0.####e0", "0.#####e0", "0.######e0" };
        private const string CompactSuffixes = " KMBT";

        internal static int Write(FloatingTextValue value, FloatingTextType type, FloatingTextStyle style, Span<char> output)
        {
            style.Prefix.AsSpan().CopyTo(output);
            int count = style.Prefix.Length;
            if (style.ShowValue)
            {
                if (type == FloatingTextType.Multiplier) output[count++] = '×';
                else if (type != FloatingTextType.Custom && value.Number >= 0) output[count++] = '+';
                int length = WriteNumber(value, style, output.Slice(count, output.Length - count - style.Suffix.Length));
                if (length == 0) return 0;
                count += length;
            }
            style.Suffix.AsSpan().CopyTo(output.Slice(count));
            return count + style.Suffix.Length;
        }

        private static int WriteNumber(FloatingTextValue value, FloatingTextStyle style, Span<char> output)
        {
            var culture = CultureInfo.InvariantCulture;
            double magnitude = Math.Abs(value.Number);
            if (style.AbbreviateAt == 0 || magnitude < style.AbbreviateAt)
            {
                if (value.Integer.HasValue)
                {
                    if (value.Integer.Value.TryFormat(output, out int exact, provider: culture)) return exact;
                }
                else if (magnitude < 1e15)
                {
                    for (int decimals = style.Decimals; decimals >= 0; decimals--)
                        if (value.Number.TryFormat(output, out int length, Fixed[decimals], culture)) return length;
                }
            }

            if (magnitude >= 1000 && magnitude < 1e15)
            {
                double scaled = value.Number / 1000;
                int unit = 1;
                while (unit < CompactSuffixes.Length - 1 &&
                    Math.Round(Math.Abs(scaled), style.CompactDecimals, MidpointRounding.AwayFromZero) >= 1000)
                {
                    unit++;
                    scaled /= 1000;
                }
                for (int decimals = style.CompactDecimals; decimals >= 0; decimals--)
                {
                    if (!scaled.TryFormat(output.Slice(0, output.Length - 1), out int length, Fixed[decimals], culture)) continue;
                    output[length] = CompactSuffixes[unit];
                    return length + 1;
                }
            }

            for (int decimals = style.CompactDecimals; decimals >= 0; decimals--)
                if (value.Number.TryFormat(output, out int length, Scientific[decimals], culture)) return length;
            return 0;
        }
    }
}
