using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MResPacker
{
    // Independent JSON implementation for the writer's schema and read-back validation.
    // Deliberately has no reference to a runtime loader or third-party JSON package.
    internal static class MResJson
    {
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static Dictionary<string, object> Object(params object[] pairs)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }
        internal static byte[] Bytes(object value) { var b = new StringBuilder(); Write(b, value); return Utf8.GetBytes(b.ToString()); }
        private static void Write(StringBuilder b, object value)
        {
            if (value == null) { b.Append("null"); return; }
            if (value is string s) { Quote(b, s); return; }
            if (value is bool flag) { b.Append(flag ? "true" : "false"); return; }
            if (value is IDictionary<string, object> map)
            {
                b.Append('{'); bool first = true;
                foreach (var pair in map) { if (!first) b.Append(','); first = false; Quote(b, pair.Key); b.Append(':'); Write(b, pair.Value); }
                b.Append('}'); return;
            }
            if (value is IEnumerable array)
            {
                b.Append('['); bool first = true;
                foreach (var item in array) { if (!first) b.Append(','); first = false; Write(b, item); }
                b.Append(']'); return;
            }
            if (value is float f)
            { if (float.IsNaN(f) || float.IsInfinity(f)) throw new ArgumentException("Non-finite JSON number."); b.Append(f.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (value is double d)
            { if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("Non-finite JSON number."); b.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }
            if (value is int || value is uint || value is long || value is ulong || value is byte)
            { b.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            throw new ArgumentException("Unsupported JSON value " + value.GetType());
        }
        private static void Quote(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break; case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break; case '\r': b.Append("\\r"); break; case '\t': b.Append("\\t"); break;
                    default: if (c < 32) b.Append("\\u" + ((int)c).ToString("x4")); else b.Append(c); break;
                }
            }
            b.Append('"');
        }
        internal static object Parse(byte[] bytes) => new Parser(Utf8.GetString(bytes)).Parse();
        private sealed class Parser
        {
            private readonly string text; private int at;
            internal Parser(string text) { this.text = text; }
            private FormatException Error() => new FormatException("Invalid JSON near character " + at);
            private void Space() { while (at < text.Length && (text[at] == ' ' || text[at] == '\r' || text[at] == '\n' || text[at] == '\t')) at++; }
            private bool Eat(char c) { Space(); if (at >= text.Length || text[at] != c) return false; at++; return true; }
            internal object Parse() { var value = Value(0); Space(); if (at != text.Length) throw Error(); return value; }
            private object Value(int depth)
            {
                if (depth > 64) throw Error(); Space(); if (at == text.Length) throw Error();
                if (text[at] == '"') return String();
                if (Eat('{'))
                {
                    var map = new Dictionary<string, object>(StringComparer.Ordinal);
                    if (Eat('}')) return map;
                    do { Space(); if (at == text.Length || text[at] != '"') throw Error(); string key = String();
                        if (!Eat(':') || map.ContainsKey(key)) throw Error(); map.Add(key, Value(depth + 1));
                        if (Eat('}')) return map;
                    } while (Eat(','));
                    throw Error();
                }
                if (Eat('['))
                {
                    var list = new List<object>(); if (Eat(']')) return list;
                    do { list.Add(Value(depth + 1)); if (Eat(']')) return list; } while (Eat(',')); throw Error();
                }
                foreach (string literal in new[] { "true", "false", "null" })
                    if (at + literal.Length <= text.Length && string.CompareOrdinal(text, at, literal, 0, literal.Length) == 0)
                    { at += literal.Length; return literal == "null" ? null : (object)(literal == "true"); }
                int start = at; if (text[at] == '-') at++;
                if (at >= text.Length) throw Error();
                if (text[at] == '0') at++; else { if (text[at] < '1' || text[at] > '9') throw Error(); Digits(); }
                if (at < text.Length && text[at] == '.') { at++; int before = at; Digits(); if (at == before) throw Error(); }
                if (at < text.Length && (text[at] == 'e' || text[at] == 'E'))
                { at++; if (at < text.Length && (text[at] == '+' || text[at] == '-')) at++; int before = at; Digits(); if (at == before) throw Error(); }
                if (!double.TryParse(text.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                    double.IsInfinity(number) || double.IsNaN(number)) throw Error();
                return number;
            }
            private void Digits() { while (at < text.Length && text[at] >= '0' && text[at] <= '9') at++; }
            private string String()
            {
                at++; var b = new StringBuilder();
                while (at < text.Length)
                {
                    char c = text[at++]; if (c == '"') return b.ToString(); if (c < 32) throw Error();
                    if (c != '\\') { b.Append(c); continue; } if (at == text.Length) throw Error();
                    switch (text[at++])
                    {
                        case '"': b.Append('"'); break; case '\\': b.Append('\\'); break; case '/': b.Append('/'); break;
                        case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break; case 'n': b.Append('\n'); break;
                        case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                        case 'u':
                            if (at + 4 > text.Length || !ushort.TryParse(text.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) throw Error();
                            b.Append((char)code); at += 4; break;
                        default: throw Error();
                    }
                }
                throw Error();
            }
        }
    }
}
