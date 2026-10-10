using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MRes
{
    // A bounded JSON reader: rejects duplicate fields, malformed numbers and trailing input.
    // No Unity object or editor dependency is involved in parsing a package.
    internal sealed class MResJson
    {
        private readonly string text;
        private int position;
        private MResJson(string text) { this.text = text; }

        internal static Dictionary<string, object> Parse(string text, string path)
        {
            try
            {
                var reader = new MResJson(text);
                var result = reader.Value(0) as Dictionary<string, object>;
                reader.White();
                if (result == null || reader.position != text.Length) throw reader.Error();
                return result;
            }
            catch (MResFormatException e) { throw new MResFormatException(path + ": " + e.Message, e); }
        }

        private MResFormatException Error() => new MResFormatException("Invalid JSON at character " + position);
        private void White()
        {
            while (position < text.Length && (text[position] == ' ' || text[position] == '\t' ||
                text[position] == '\n' || text[position] == '\r')) position++;
        }
        private bool Take(char c)
        {
            White();
            if (position < text.Length && text[position] == c) { position++; return true; }
            return false;
        }
        private void Need(char c) { if (!Take(c)) throw Error(); }
        private object Value(int depth)
        {
            if (depth > 64) throw Error();
            White();
            if (position == text.Length) throw Error();
            char c = text[position];
            if (c == '"') return String();
            if (Take('{'))
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                if (Take('}')) return result;
                do
                {
                    White();
                    string key = String(); Need(':');
                    if (result.ContainsKey(key)) throw new MResFormatException("Duplicate JSON field: " + key);
                    result.Add(key, Value(depth + 1));
                } while (Take(','));
                Need('}'); return result;
            }
            if (Take('['))
            {
                var result = new List<object>();
                if (Take(']')) return result;
                do { result.Add(Value(depth + 1)); } while (Take(','));
                Need(']'); return result;
            }
            if (c == 't') { Literal("true"); return true; }
            if (c == 'f') { Literal("false"); return false; }
            if (c == 'n') { Literal("null"); return null; }
            int start = position;
            if (c == '-') position++;
            if (position == text.Length) throw Error();
            if (text[position] == '0') position++;
            else { if (!Digit19()) throw Error(); while (Digit()) position++; }
            if (position < text.Length && text[position] == '.')
            { position++; if (!Digit()) throw Error(); while (Digit()) position++; }
            if (position < text.Length && (text[position] == 'e' || text[position] == 'E'))
            {
                position++;
                if (position < text.Length && (text[position] == '+' || text[position] == '-')) position++;
                if (!Digit()) throw Error(); while (Digit()) position++;
            }
            if (!double.TryParse(text.Substring(start, position - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double number) || double.IsInfinity(number) || double.IsNaN(number)) throw Error();
            return number;
        }
        private bool Digit() => position < text.Length && text[position] >= '0' && text[position] <= '9';
        private bool Digit19() => position < text.Length && text[position] >= '1' && text[position] <= '9';
        private void Literal(string value)
        {
            if (position + value.Length > text.Length || string.CompareOrdinal(text, position, value, 0, value.Length) != 0) throw Error();
            position += value.Length;
        }
        private string String()
        {
            if (position == text.Length || text[position++] != '"') throw Error();
            var result = new StringBuilder();
            while (position < text.Length)
            {
                char c = text[position++];
                if (c == '"') return result.ToString();
                if (c < 32) throw Error();
                if (c == '\\')
                {
                    if (position == text.Length) throw Error();
                    c = text[position++];
                    switch (c)
                    {
                        case '"': case '\\': case '/': break;
                        case 'b': c = '\b'; break; case 'f': c = '\f'; break;
                        case 'n': c = '\n'; break; case 'r': c = '\r'; break; case 't': c = '\t'; break;
                        case 'u':
                            if (position + 4 > text.Length || !ushort.TryParse(text.Substring(position, 4),
                                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code)) throw Error();
                            c = (char)code; position += 4; break;
                        default: throw Error();
                    }
                }
                result.Append(c);
            }
            throw Error();
        }
    }

    internal sealed class MResFields
    {
        internal readonly Dictionary<string, object> Fields;
        internal readonly string Context;
        internal MResFields(Dictionary<string, object> fields, string context) { Fields = fields; Context = context; }
        internal MResFormatException Error(string key) => new MResFormatException(Context + ": invalid/missing " + key);
        internal bool Has(string key) => Fields.ContainsKey(key);
        internal string String(string key, string fallback = null, bool optional = false)
        {
            if (!Fields.TryGetValue(key, out object v) || v == null)
            { if (optional) return fallback; throw Error(key); }
            if (!(v is string s)) throw Error(key);
            return s;
        }
        internal double Number(string key, double fallback = 0, bool optional = false)
        {
            if (!Fields.TryGetValue(key, out object v)) { if (optional) return fallback; throw Error(key); }
            if (!(v is double d) || d > float.MaxValue || d < -float.MaxValue) throw Error(key);
            return d;
        }
        internal float Float(string key, float fallback = 0, bool optional = false) => (float)Number(key, fallback, optional);
        internal int Int(string key, int fallback = 0, bool optional = false)
        {
            double n = Number(key, fallback, optional);
            if (n < int.MinValue || n > int.MaxValue || n != Math.Truncate(n)) throw Error(key);
            return (int)n;
        }
        internal bool Bool(string key, bool fallback = false)
        {
            if (!Fields.TryGetValue(key, out object v)) return fallback;
            if (!(v is bool b)) throw Error(key); return b;
        }
        internal List<object> Array(string key, bool optional = false)
        {
            if (!Fields.TryGetValue(key, out object v) && optional) return new List<object>();
            if (!(v is List<object> a)) throw Error(key); return a;
        }
        internal MResFields Object(string key)
        {
            if (!Fields.TryGetValue(key, out object v)) throw Error(key);
            return From(v, Context + "/" + key);
        }
        internal static MResFields From(object value, string context)
        {
            if (!(value is Dictionary<string, object> d)) throw new MResFormatException(context + ": expected object");
            return new MResFields(d, context);
        }
    }
}
